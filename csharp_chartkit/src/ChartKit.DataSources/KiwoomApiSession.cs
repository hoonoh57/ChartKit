using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace ChartKit.CSharp.DataSources;

public sealed class KiwoomApiSession : IAsyncDisposable
{
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(1);
    private readonly KiwoomOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly IKiwoomClock _clock;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private readonly object _tokenSync = new();
    private string _accessToken = "";
    private DateTimeOffset _tokenExpiresAtUtc = DateTimeOffset.MinValue;
    private long _nextRequestTimestamp;
    private long _blockedUntilTimestamp;
    private int _disposed;

    public KiwoomApiSession(
        KiwoomOptions options,
        HttpMessageHandler? handler = null,
        IKiwoomClock? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? new SystemKiwoomClock();
        _ownsHttpClient = true;
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: true);
        _http.Timeout = _options.RequestTimeout;
    }

    public KiwoomOptions Options => _options;

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (TryGetUsableToken(out string cached)) return cached;

        await _tokenGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetUsableToken(out cached)) return cached;
            _options.ValidateCredentials();

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_options.RestBaseUri, "/oauth2/token"));
            string body = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["appkey"] = _options.AppKey,
                ["secretkey"] = _options.SecretKey
            });
            request.Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response =
                await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            byte[] payload =
                await response.Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw CreateHttpException(response.StatusCode, payload, "token");

            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            string token = ReadString(root, "token");
            if (token.Length == 0) token = ReadString(root, "access_token");
            if (token.Length == 0)
                throw new InvalidOperationException(
                    "Kiwoom token response did not contain a token.");

            lock (_tokenSync)
            {
                _accessToken = token;
                _tokenExpiresAtUtc = ResolveTokenExpiry(root, _clock.UtcNow);
            }
            return token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public void InvalidateToken(string exactToken)
    {
        if (string.IsNullOrEmpty(exactToken)) return;
        lock (_tokenSync)
        {
            if (!string.Equals(
                    _accessToken,
                    exactToken,
                    StringComparison.Ordinal))
                return;
            _accessToken = "";
            _tokenExpiresAtUtc = DateTimeOffset.MinValue;
        }
    }

    public async Task<KiwoomJsonResponse> PostJsonAsync(
        string path,
        string apiId,
        string jsonBody,
        string continuation = "N",
        string nextKey = "",
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(apiId))
            throw new ArgumentException("API id is required.", nameof(apiId));

        int authenticationRetries = 0;
        int throttlingRetries = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string token = await GetAccessTokenAsync(cancellationToken)
                .ConfigureAwait(false);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_options.RestBaseUri, path));
            request.Headers.TryAddWithoutValidation(
                "authorization", "Bearer " + token);
            request.Headers.TryAddWithoutValidation("api-id", apiId);
            request.Headers.TryAddWithoutValidation("cont-yn", continuation);
            request.Headers.TryAddWithoutValidation("next-key", nextKey);
            request.Content = new StringContent(
                jsonBody,
                Encoding.UTF8,
                "application/json");

            using HttpResponseMessage response =
                await SendThrottledAsync(request, cancellationToken)
                    .ConfigureAwait(false);

            if (response.StatusCode is
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                InvalidateToken(token);
                if (authenticationRetries++ == 0) continue;
            }

            if ((int)response.StatusCode == 429 && throttlingRetries++ < 3)
            {
                TimeSpan delay = ResolveRetryAfter(response, _clock.UtcNow);
                SetGlobalBlock(delay);
                await _clock.DelayAsync(delay, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            byte[] payload =
                await response.Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw CreateHttpException(response.StatusCode, payload, apiId);

            JsonDocument document = JsonDocument.Parse(payload);
            return new KiwoomJsonResponse(
                document,
                response.StatusCode,
                ReadHeader(response, "cont-yn", "N"),
                ReadHeader(response, "next-key", ""));
        }
    }

    private async Task<HttpResponseMessage> SendThrottledAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Task<HttpResponseMessage> pending;
        await _rateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            long now = _clock.Timestamp;
            long target = Math.Max(
                Volatile.Read(ref _nextRequestTimestamp),
                Volatile.Read(ref _blockedUntilTimestamp));
            if (target > now)
                await _clock.DelayAsync(
                        ToTimeSpan(target - now),
                        cancellationToken)
                    .ConfigureAwait(false);

            long intervalTicks = ToTimestampTicks(_options.RequestInterval);
            Volatile.Write(
                ref _nextRequestTimestamp,
                Math.Max(_clock.Timestamp, target) + intervalTicks);

            pending = _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        finally
        {
            _rateGate.Release();
        }

        return await pending.ConfigureAwait(false);
    }

    private bool TryGetUsableToken(out string token)
    {
        lock (_tokenSync)
        {
            token = _accessToken;
            return token.Length > 0 &&
                   _clock.UtcNow + TokenRefreshSkew < _tokenExpiresAtUtc;
        }
    }

    private void SetGlobalBlock(TimeSpan delay)
    {
        long candidate = _clock.Timestamp + ToTimestampTicks(delay);
        long current;
        do
        {
            current = Volatile.Read(ref _blockedUntilTimestamp);
            if (candidate <= current) return;
        } while (Interlocked.CompareExchange(
                     ref _blockedUntilTimestamp,
                     candidate,
                     current) != current);
    }

    private long ToTimestampTicks(TimeSpan value) =>
        Math.Max(0L,
            (long)Math.Ceiling(value.TotalSeconds * _clock.Frequency));

    private TimeSpan ToTimeSpan(long timestampTicks) =>
        TimeSpan.FromSeconds((double)timestampTicks / _clock.Frequency);

    private static TimeSpan ResolveRetryAfter(
        HttpResponseMessage response,
        DateTimeOffset now)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta &&
            delta > TimeSpan.Zero)
            return delta;
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            TimeSpan calculated = date - now;
            if (calculated > TimeSpan.Zero) return calculated;
        }
        return TimeSpan.FromSeconds(1);
    }

    private static DateTimeOffset ResolveTokenExpiry(
        JsonElement root,
        DateTimeOffset now)
    {
        if (TryReadDouble(root, "expires_in", out double seconds) &&
            seconds > 0)
            return now.AddSeconds(seconds);

        foreach (string key in new[]
                 {
                     "expires_dt", "expires_at", "expiration"
                 })
        {
            string value = ReadString(root, key);
            if (value.Length == 0) continue;
            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset offset))
                return offset.ToUniversalTime();
            if (DateTime.TryParseExact(
                    value,
                    new[]
                    {
                        "yyyyMMddHHmmss", "yyyy-MM-dd HH:mm:ss"
                    },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime local))
                return new DateTimeOffset(local).ToUniversalTime();
        }

        return now.AddMinutes(30);
    }

    private static bool TryReadDouble(
        JsonElement root,
        string key,
        out double value)
    {
        value = 0;
        if (!root.TryGetProperty(key, out JsonElement element)) return false;
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDouble(out value);
        return element.ValueKind == JsonValueKind.String &&
               double.TryParse(
                   element.GetString(),
                   NumberStyles.Any,
                   CultureInfo.InvariantCulture,
                   out value);
    }

    private static string ReadString(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out JsonElement element)) return "";
        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? ""
            : element.ToString();
    }

    private static string ReadHeader(
        HttpResponseMessage response,
        string name,
        string fallback)
    {
        if (response.Headers.TryGetValues(
                name,
                out IEnumerable<string>? values))
            return values.FirstOrDefault()?.Trim() ?? fallback;
        if (response.Content.Headers.TryGetValues(name, out values))
            return values.FirstOrDefault()?.Trim() ?? fallback;
        return fallback;
    }

    private static HttpRequestException CreateHttpException(
        HttpStatusCode statusCode,
        byte[] payload,
        string operation)
    {
        string text = Encoding.UTF8.GetString(payload);
        if (text.Length > 512) text = text[..512];
        return new HttpRequestException(
            $"Kiwoom {operation} failed with {(int)statusCode}: {text}",
            null,
            statusCode);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(KiwoomApiSession));
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;
        _tokenGate.Dispose();
        _rateGate.Dispose();
        if (_ownsHttpClient) _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
