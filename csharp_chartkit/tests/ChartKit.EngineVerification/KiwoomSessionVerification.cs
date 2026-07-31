using System.Net;
using System.Net.Http.Headers;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class KiwoomSessionVerification
{
    public static async Task RunAsync()
    {
        var clock = new FakeKiwoomClock();
        int tokenCalls = 0;
        int apiCalls = 0;
        var handler = new ScriptedHttpHandler(
            (request, _) =>
            {
                if (request.RequestUri?.AbsolutePath == "/oauth2/token")
                {
                    int tokenNumber = Interlocked.Increment(ref tokenCalls);
                    return ScriptedHttpHandler.Json(
                        HttpStatusCode.OK,
                        $"{{\"token\":\"token-{tokenNumber}\",\"expires_in\":120}}");
                }

                int apiNumber = Interlocked.Increment(ref apiCalls);
                if (apiNumber == 1)
                    return ScriptedHttpHandler.Json(
                        HttpStatusCode.Unauthorized,
                        "{\"error\":\"expired\"}");
                if (apiNumber == 3)
                {
                    HttpResponseMessage response = ScriptedHttpHandler.Json(
                        (HttpStatusCode)429,
                        "{\"error\":\"rate\"}");
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(
                        TimeSpan.FromSeconds(2));
                    return response;
                }
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"ok\":true}",
                    "N",
                    "");
            },
            () => clock.Timestamp);

        KiwoomOptions options = Options(TimeSpan.FromMilliseconds(100));
        await using var session = new KiwoomApiSession(options, handler, clock);

        Task<string>[] tokenTasks = Enumerable.Range(0, 16)
            .Select(_ => session.GetAccessTokenAsync())
            .ToArray();
        string[] tokens = await Task.WhenAll(tokenTasks);
        if (tokenCalls != 1 || tokens.Any(token => token != "token-1"))
            throw new InvalidOperationException("Token single-flight failed.");

        clock.Advance(TimeSpan.FromSeconds(61));
        string refreshed = await session.GetAccessTokenAsync();
        if (refreshed != "token-2" || tokenCalls != 2)
            throw new InvalidOperationException("Token expiry refresh failed.");

        using (KiwoomJsonResponse response = await session.PostJsonAsync(
                   "/api/test", "test-api", "{}"))
        {
            if (!response.Document.RootElement.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException("401 recovery response failed.");
        }
        if (tokenCalls != 3 || apiCalls != 2)
            throw new InvalidOperationException("401 exact-token reauthentication failed.");

        using (KiwoomJsonResponse response = await session.PostJsonAsync(
                   "/api/test", "test-api", "{}", "Y", "next-1"))
        {
            if (!response.Document.RootElement.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException("429 retry response failed.");
        }
        if (apiCalls != 4 || !clock.Delays.Any(delay => delay >= TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("429 Retry-After was not honored.");

        Task[] concurrent = Enumerable.Range(0, 8)
            .Select(async _ =>
            {
                using KiwoomJsonResponse response = await session.PostJsonAsync(
                    "/api/test", "test-api", "{}");
            })
            .ToArray();
        await Task.WhenAll(concurrent);

        HttpCall[] apiRecords = handler.Calls
            .Where(call => call.Path == "/api/test")
            .ToArray();
        if (apiRecords.Length != 12)
            throw new InvalidOperationException(
                $"Expected 12 API starts, got {apiRecords.Length}.");
        if (apiRecords[0].Authorization != "Bearer token-2" ||
            apiRecords[1].Authorization != "Bearer token-3")
            throw new InvalidOperationException("401 token replacement was not exact.");
        if (apiRecords[2].Continuation != "Y" || apiRecords[2].NextKey != "next-1")
            throw new InvalidOperationException("Continuation headers failed.");

        HttpCall[] intervalRecords = apiRecords[^8..];
        for (int index = 1; index < intervalRecords.Length; index++)
        {
            if (intervalRecords[index].Timestamp - intervalRecords[index - 1].Timestamp < 100)
                throw new InvalidOperationException("Global request-start interval failed.");
        }

        Console.WriteLine($"csharp_token_calls={tokenCalls}");
        Console.WriteLine($"csharp_api_calls={apiCalls}");
        Console.WriteLine("csharp_kiwoom_session=PASS");
    }

    internal static KiwoomOptions Options(TimeSpan interval) => new(
        IsMock: true,
        AppKey: "unit-app",
        SecretKey: "unit-secret",
        RestBaseUri: new Uri("https://unit.test"),
        WebSocketUri: new Uri("wss://unit.test/socket"),
        AdjustPrice: "1",
        DefaultSymbol: "000660",
        RequestInterval: interval,
        RequestTimeout: TimeSpan.FromSeconds(10));
}
