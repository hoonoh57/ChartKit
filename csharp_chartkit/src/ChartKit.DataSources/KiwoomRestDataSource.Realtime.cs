using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public sealed partial class KiwoomRestDataSource
{
    public async IAsyncEnumerable<CandleEvent> StreamAsync(
        IReadOnlyList<string> symbols,
        CandleTimeframe timeframe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(symbols);
        timeframe.Validate();
        if (timeframe.Unit is not (CandleUnit.Minute or CandleUnit.Tick))
            throw new NotSupportedException("Kiwoom realtime supports minute and tick candles.");

        string[] normalized = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("At least one symbol is required.", nameof(symbols));

        var channel = Channel.CreateBounded<CandleEvent>(new BoundedChannelOptions(8192)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task producer = Task.Run(
            () => RealtimeProducerAsync(normalized, timeframe, channel.Writer, linked.Token),
            CancellationToken.None);

        try
        {
            await foreach (CandleEvent value in channel.Reader
                               .ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
                yield return value;
        }
        finally
        {
            linked.Cancel();
            try { await producer.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RealtimeProducerAsync(
        string[] symbols,
        CandleTimeframe timeframe,
        ChannelWriter<CandleEvent> writer,
        CancellationToken cancellationToken)
    {
        var builders = new Dictionary<string, RealtimeCandleBuilder>(StringComparer.Ordinal);
        foreach (string symbol in symbols)
        {
            Candle? seedCandle = null;
            int seedTickCount = 0;
            if (TryGetSeed(symbol, out RealtimeSeed seed))
            {
                seedCandle = seed.Candle;
                seedTickCount = seed.TickCount;
            }
            builders[symbol] = new RealtimeCandleBuilder(
                timeframe, seedCandle, seedTickCount);
            ResetRealtimeDiagnostics(
                symbol,
                timeframe,
                seedCandle,
                seedTickCount);
        }

        Exception? terminal = null;
        bool reconnecting = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (string symbol in symbols)
                {
                    if (TryGetRealtimeDiagnosticsState(
                            symbol,
                            out RealtimeDiagnosticsState? diagnostics))
                        diagnostics.RecordConnectionAttempt(reconnecting);
                }

                string token = await _session.GetAccessTokenAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    using var socket = new ClientWebSocket();
                    await socket.ConnectAsync(
                        _session.Options.WebSocketUri,
                        cancellationToken).ConfigureAwait(false);
                    SetRealtimeConnectionState(
                        symbols,
                        RealtimeConnectionState.Connected);
                    await SendJsonAsync(socket, new Dictionary<string, object>
                    {
                        ["trnm"] = "LOGIN",
                        ["token"] = token
                    }, cancellationToken).ConfigureAwait(false);
                    await RunRealtimeSessionAsync(
                        socket,
                        token,
                        symbols,
                        builders,
                        writer,
                        cancellationToken).ConfigureAwait(false);
                    reconnecting = true;
                    SetRealtimeConnectionState(
                        symbols,
                        RealtimeConnectionState.Reconnecting);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    terminal = exception;
                    reconnecting = true;
                    SetRealtimeConnectionState(
                        symbols,
                        RealtimeConnectionState.Faulted,
                        exception.Message);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            SetRealtimeConnectionState(
                symbols,
                RealtimeConnectionState.Stopped,
                terminal?.Message);
            writer.TryComplete(cancellationToken.IsCancellationRequested ? null : terminal);
        }
    }

    private async Task RunRealtimeSessionAsync(
        ClientWebSocket socket,
        string token,
        string[] symbols,
        Dictionary<string, RealtimeCandleBuilder> builders,
        ChannelWriter<CandleEvent> writer,
        CancellationToken cancellationToken)
    {
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            string? json = await ReceiveTextAsync(socket, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(json)) return;
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string transaction = KiwoomJson.Text(root, "trnm");
            switch (transaction)
            {
                case "LOGIN":
                    if (KiwoomJson.ReadInt(root, "return_code") != 0)
                    {
                        _session.InvalidateToken(token);
                        throw new InvalidOperationException(
                            "Kiwoom WebSocket login failed: " +
                            KiwoomJson.Text(root, "return_msg"));
                    }
                    SetRealtimeConnectionState(
                        symbols,
                        RealtimeConnectionState.LoggedIn);
                    await SendRegistrationAsync(socket, symbols, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case "PING":
                    await SendTextAsync(socket, json, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case "REG":
                    if (KiwoomJson.ReadInt(root, "return_code") != 0)
                        throw new InvalidOperationException(
                            "Kiwoom realtime registration failed: " +
                            KiwoomJson.Text(root, "return_msg"));
                    foreach (string symbol in symbols)
                    {
                        if (TryGetRealtimeDiagnosticsState(
                                symbol,
                                out RealtimeDiagnosticsState? diagnostics))
                            diagnostics.RecordRegistration();
                    }
                    break;

                case "REAL":
                    await ProcessRealtimeAsync(
                        root, builders, writer, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task ProcessRealtimeAsync(
        JsonElement root,
        Dictionary<string, RealtimeCandleBuilder> builders,
        ChannelWriter<CandleEvent> writer,
        CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Array)
            return;

        foreach (JsonElement entry in data.EnumerateArray())
        {
            if (KiwoomJson.Text(entry, "type") != "0B") continue;
            string symbol = KiwoomJson.Text(entry, "item");
            if (!builders.TryGetValue(symbol, out RealtimeCandleBuilder? builder)) continue;
            if (!entry.TryGetProperty("values", out JsonElement values) ||
                values.ValueKind != JsonValueKind.Object)
                continue;

            double? rawPrice = KiwoomJson.RealtimeNumber(values, "10");
            if (!rawPrice.HasValue) continue;
            double rawQuantity = KiwoomJson.RealtimeNumber(values, "15") ?? 0d;
            DateTime tradeTime = ParseRealtimeTime(KiwoomJson.Text(values, "20"));
            bool hadSeed = builder.HasSeed;
            if (!builder.TryApply(
                    tradeTime,
                    (float)Math.Abs(rawPrice.Value),
                    (long)Math.Abs(rawQuantity),
                    out MarketEventKind kind,
                    out Candle candle))
            {
                if (TryGetRealtimeDiagnosticsState(
                        symbol,
                        out RealtimeDiagnosticsState? rejectedDiagnostics))
                    rejectedDiagnostics.RecordRejectedStale(tradeTime);
                continue;
            }

            if (TryGetRealtimeDiagnosticsState(
                    symbol,
                    out RealtimeDiagnosticsState? acceptedDiagnostics))
                acceptedDiagnostics.RecordAccepted(tradeTime, kind, hadSeed);

            await writer.WriteAsync(
                CandleEvent.Create(symbol, kind, candle, candle.Sequence),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task SendRegistrationAsync(
        ClientWebSocket socket,
        string[] symbols,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object>
        {
            ["trnm"] = "REG",
            ["grp_no"] = "1",
            ["refresh"] = "0",
            ["data"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["item"] = symbols,
                    ["type"] = new[] { "0B" }
                }
            }
        };
        await SendJsonAsync(socket, payload, cancellationToken).ConfigureAwait(false);
    }

    private static DateTime ParseRealtimeTime(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return DateTime.Now;
        string text = source.Trim();
        foreach (string format in new[] { "yyyyMMddHHmmss", "HHmmss" })
        {
            if (!DateTime.TryParseExact(
                    text,
                    format,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out DateTime parsed))
                continue;
            return format == "HHmmss"
                ? DateTime.Today.Add(parsed.TimeOfDay)
                : parsed;
        }
        return DateTime.Now;
    }

    private static Task SendJsonAsync(
        ClientWebSocket socket,
        object payload,
        CancellationToken cancellationToken) =>
        SendTextAsync(socket, JsonSerializer.Serialize(payload), cancellationToken);

    private static async Task SendTextAsync(
        ClientWebSocket socket,
        string text,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            true,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var stream = new MemoryStream();
            while (true)
            {
                ValueWebSocketReceiveResult result = await socket.ReceiveAsync(
                    buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Text) continue;
                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) break;
            }
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
