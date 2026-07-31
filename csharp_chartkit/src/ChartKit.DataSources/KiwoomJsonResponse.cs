using System.Net;
using System.Text.Json;

namespace ChartKit.CSharp.DataSources;

public sealed class KiwoomJsonResponse : IDisposable
{
    public KiwoomJsonResponse(
        JsonDocument document,
        HttpStatusCode statusCode,
        string continuation,
        string nextKey)
    {
        Document = document;
        StatusCode = statusCode;
        Continuation = continuation;
        NextKey = nextKey;
    }

    public JsonDocument Document { get; }
    public HttpStatusCode StatusCode { get; }
    public string Continuation { get; }
    public string NextKey { get; }

    public void Dispose() => Document.Dispose();
}
