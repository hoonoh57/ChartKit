using System.Net;
using System.Text.Json;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.EngineVerification;

internal static class KiwoomRealtimeReconnectVerification
{
    public static async Task RunAsync()
    {
        const string symbol = "000660";
        DateTime seedClose = new(2026, 8, 3, 9, 5, 0);

        var firstSocket = new ScriptedKiwoomWebSocket(
            Message(LoginSuccess()),
            Message(RegistrationSuccess()),
            Message(Realtime(symbol, "20260803090300", 103, 5)),
            ScriptedKiwoomWebSocket.InboundFrame.Close);
        var secondSocket = new ScriptedKiwoomWebSocket(
            Message(LoginSuccess()),
            Message(RegistrationSuccess()),
            Message(Realtime(symbol, "20260803090400", 104, 7)),
            Message(Realtime(symbol, "20260803090500", 105, 9)));

        var sockets = new Queue<IKiwoomWebSocket>(
            new IKiwoomWebSocket[] { firstSocket, secondSocket });
        var socketGate = new object();
        IKiwoomWebSocket CreateSocket()
        {
            lock (socketGate)
            {
                if (sockets.Count == 0)
                    throw new InvalidOperationException(
                        "Realtime reconnect opened an unexpected extra socket.");
                return sockets.Dequeue();
            }
        }

        var handler = new ScriptedHttpHandler((request, _) =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path == "/oauth2/token")
            {
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    "{\"token\":\"reconnect-token\",\"expires_in\":3600}");
            }

            if (path == "/api/dostk/chart")
            {
                return ScriptedHttpHandler.Json(
                    HttpStatusCode.OK,
                    BuildSeedHistory(seedClose));
            }

            return ScriptedHttpHandler.Json(HttpStatusCode.NotFound, "{}");
        });

        await using var session = new KiwoomApiSession(
            KiwoomSessionVerification.Options(TimeSpan.Zero),
            handler,
            new FakeKiwoomClock());
        await using var source = new KiwoomRestDataSource(
            options: null,
            session: session,
            webSocketFactory: CreateSocket);

        IReadOnlyList<Candle> history = await source.GetHistoryAsync(
            new HistoryRequest(symbol, CandleTimeframe.Minute(5), 1),
            CancellationToken.None);
        if (history.Count != 1 ||
            history[0].OpenTime != seedClose.AddMinutes(-5) ||
            history[0].CloseTime != seedClose ||
            history[0].Sequence != 0)
        {
            throw new InvalidOperationException(
                "Realtime reconnect seed history fixture failed.");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var events = new List<CandleEvent>(3);
        await foreach (CandleEvent value in source.StreamAsync(
                           [symbol],
                           CandleTimeframe.Minute(5),
                           timeout.Token))
        {
            events.Add(value);
            if (events.Count == 3) break;
        }

        VerifyEvents(events);
        VerifySocketContract(firstSocket, "first");
        VerifySocketContract(secondSocket, "second");

        RealtimeDiagnosticsSnapshot diagnostics =
            source.GetRealtimeDiagnostics(symbol);
        if (diagnostics.BoundaryState != RealtimeBoundaryState.SeedUpdated ||
            diagnostics.FirstEventKind != MarketEventKind.Update ||
            diagnostics.FirstRealtimeTime != new DateTime(2026, 8, 3, 9, 3, 0) ||
            diagnostics.AcceptedEvents != 3 ||
            diagnostics.UpdateEvents != 2 ||
            diagnostics.AppendEvents != 1 ||
            diagnostics.RejectedStaleEvents != 0 ||
            diagnostics.ConnectionAttempts != 2 ||
            diagnostics.RegistrationCount != 2)
        {
            throw new InvalidOperationException(
                $"Realtime reconnect diagnostics mismatch: {diagnostics}.");
        }

        Console.WriteLine("csharp_realtime_reconnect_continuity=PASS");
        Console.WriteLine("csharp_realtime_one_registration_per_connection=PASS");
        Console.WriteLine("csharp_realtime_builder_survives_reconnect=PASS");
    }

    private static void VerifyEvents(IReadOnlyList<CandleEvent> events)
    {
        if (events.Count != 3)
            throw new InvalidOperationException(
                $"Expected 3 realtime events, got {events.Count}.");

        CandleEvent first = events[0];
        if (first.Kind != MarketEventKind.Update ||
            first.Candle.Sequence != 0 ||
            first.Candle.Open != 100f ||
            first.Candle.High != 103f ||
            first.Candle.Low != 99f ||
            first.Candle.Close != 103f ||
            first.Candle.Volume != 105)
        {
            throw new InvalidOperationException(
                $"First realtime seed update mismatch: {first}.");
        }

        CandleEvent second = events[1];
        if (second.Kind != MarketEventKind.Update ||
            second.Candle.Sequence != 0 ||
            second.Candle.High != 104f ||
            second.Candle.Close != 104f ||
            second.Candle.Volume != 112)
        {
            throw new InvalidOperationException(
                "Realtime builder was reset during reconnect instead of " +
                $"continuing the current candle: {second}.");
        }

        CandleEvent third = events[2];
        if (third.Kind != MarketEventKind.Append ||
            third.Candle.Sequence != 1 ||
            third.Candle.OpenTime != new DateTime(2026, 8, 3, 9, 5, 0) ||
            third.Candle.CloseTime != new DateTime(2026, 8, 3, 9, 10, 0) ||
            third.Candle.Open != 105f ||
            third.Candle.High != 105f ||
            third.Candle.Low != 105f ||
            third.Candle.Close != 105f ||
            third.Candle.Volume != 9)
        {
            throw new InvalidOperationException(
                $"Realtime post-reconnect append mismatch: {third}.");
        }
    }

    private static void VerifySocketContract(
        ScriptedKiwoomWebSocket socket,
        string connectionName)
    {
        if (socket.ConnectCount != 1)
            throw new InvalidOperationException(
                $"{connectionName} socket connect count was {socket.ConnectCount}.");

        string[] messages = socket.SentMessages;
        int loginCount = messages.Count(message =>
            TransactionName(message) == "LOGIN");
        int registrationCount = messages.Count(message =>
            TransactionName(message) == "REG");
        if (loginCount != 1 || registrationCount != 1 || messages.Length != 2)
        {
            throw new InvalidOperationException(
                $"{connectionName} socket sent LOGIN={loginCount}, " +
                $"REG={registrationCount}, total={messages.Length}.");
        }
    }

    private static string TransactionName(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("trnm").GetString() ?? string.Empty;
    }

    private static ScriptedKiwoomWebSocket.InboundFrame Message(string text) =>
        ScriptedKiwoomWebSocket.InboundFrame.Message(text);

    private static string LoginSuccess() =>
        "{\"trnm\":\"LOGIN\",\"return_code\":0,\"return_msg\":\"OK\"}";

    private static string RegistrationSuccess() =>
        "{\"trnm\":\"REG\",\"return_code\":0,\"return_msg\":\"OK\"}";

    private static string Realtime(
        string symbol,
        string timestamp,
        int price,
        int quantity) =>
        "{\"trnm\":\"REAL\",\"data\":[{" +
        "\"type\":\"0B\"," +
        $"\"item\":\"{symbol}\"," +
        "\"values\":{" +
        $"\"10\":\"+{price}\"," +
        $"\"15\":\"{quantity}\"," +
        $"\"20\":\"{timestamp}\"" +
        "}}]}";

    private static string BuildSeedHistory(DateTime closeTime) =>
        "{\"stk_min_pole_chart_qry\":[{" +
        "\"cur_prc\":\"+101\"," +
        "\"open_pric\":\"+100\"," +
        "\"high_pric\":\"+102\"," +
        "\"low_pric\":\"+99\"," +
        "\"trde_qty\":\"100\"," +
        $"\"cntr_tm\":\"{closeTime:yyyyMMddHHmmss}\"" +
        "}]}";
}
