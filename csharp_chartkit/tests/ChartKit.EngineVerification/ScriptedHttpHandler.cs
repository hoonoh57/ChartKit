using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace ChartKit.CSharp.EngineVerification;

internal sealed record HttpCall(
    string Path,
    string ApiId,
    string Authorization,
    string Continuation,
    string NextKey,
    string Body,
    long Timestamp);

internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _script;
    private readonly Func<long> _timestamp;
    private int _callCount;
    private readonly ConcurrentQueue<HttpCall> _calls = new();

    public ScriptedHttpHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> script,
        Func<long>? timestamp = null)
    {
        _script = script;
        _timestamp = timestamp ?? (() => 0L);
    }

    public IReadOnlyList<HttpCall> Calls => _calls.ToArray();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int call = Interlocked.Increment(ref _callCount);
        string body = request.Content is null
            ? ""
            : request.Content.ReadAsStringAsync(cancellationToken)
                .GetAwaiter().GetResult();
        _calls.Enqueue(new HttpCall(
            request.RequestUri?.AbsolutePath ?? "",
            Header(request, "api-id"),
            Header(request, "authorization"),
            Header(request, "cont-yn"),
            Header(request, "next-key"),
            body,
            _timestamp()));
        return Task.FromResult(_script(request, call));
    }

    public static HttpResponseMessage Json(
        HttpStatusCode status,
        string json,
        string continuation = "N",
        string nextKey = "")
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        response.Headers.TryAddWithoutValidation("cont-yn", continuation);
        response.Headers.TryAddWithoutValidation("next-key", nextKey);
        return response;
    }

    private static string Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.FirstOrDefault() ?? ""
            : "";
}
