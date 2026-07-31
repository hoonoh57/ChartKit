namespace ChartKit.CSharp.DataSources;

public sealed record KiwoomOptions(
    bool IsMock,
    string AppKey,
    string SecretKey,
    Uri RestBaseUri,
    Uri WebSocketUri,
    string AdjustPrice,
    string DefaultSymbol,
    TimeSpan RequestInterval,
    TimeSpan RequestTimeout)
{
    public static KiwoomOptions FromEnvironment(string? envPath = null)
    {
        EnvFile.Load(envPath);
        bool isMock = EnvFile.GetBoolean("KIWOOM_MOCK", true);
        string appKey = isMock
            ? First("KIWOOM_MOCK_APP_KEY", "KIWOOM_APP_KEY")
            : First("KIWOOM_REAL_APP_KEY", "KIWOOM_APP_KEY");
        string secretKey = isMock
            ? First("KIWOOM_MOCK_SECRET_KEY", "KIWOOM_SECRET_KEY")
            : First("KIWOOM_REAL_SECRET_KEY", "KIWOOM_SECRET_KEY");
        string restHost = isMock
            ? "https://mockapi.kiwoom.com"
            : "https://api.kiwoom.com";
        string webSocketHost = isMock
            ? "wss://mockapi.kiwoom.com:10000/api/dostk/websocket"
            : "wss://api.kiwoom.com:10000/api/dostk/websocket";

        return new KiwoomOptions(
            isMock,
            appKey,
            secretKey,
            new Uri(restHost, UriKind.Absolute),
            new Uri(webSocketHost, UriKind.Absolute),
            EnvFile.Get("KIWOOM_ADJUST_PRICE", "1"),
            EnvFile.Get("DEFAULT_SYMBOL", "000660"),
            isMock ? TimeSpan.FromMilliseconds(1100) : TimeSpan.FromMilliseconds(220),
            TimeSpan.FromSeconds(30));
    }

    public void ValidateCredentials()
    {
        if (string.IsNullOrWhiteSpace(AppKey))
            throw new InvalidOperationException("Kiwoom app key is missing.");
        if (string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("Kiwoom secret key is missing.");
    }

    private static string First(params string[] keys)
    {
        foreach (string key in keys)
        {
            string value = EnvFile.Get(key);
            if (value.Length > 0) return value;
        }
        return "";
    }
}
