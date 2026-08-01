using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.App;

internal enum ApplicationMode
{
    Replay,
    Kiwoom,
    KiwoomProbe,
    SelfTest
}

internal sealed record AppOptions(
    ApplicationMode Mode,
    string[] Symbols,
    CandleTimeframe Timeframe,
    int HistoryCount,
    int RealtimeProbeSeconds,
    string ProfilePath)
{
    public static AppOptions Parse(string[] args)
    {
        ApplicationMode mode = ApplicationMode.Replay;
        var symbols = new List<string>();
        CandleTimeframe timeframe = CandleTimeframe.Minute(1);
        int historyCount = 240;
        int realtimeProbeSeconds = 0;
        string profilePath = GetDefaultProfilePath();

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index].Trim();
            switch (argument.ToLowerInvariant())
            {
                case "--replay":
                    mode = ApplicationMode.Replay;
                    break;
                case "--kiwoom":
                    mode = ApplicationMode.Kiwoom;
                    break;
                case "--kiwoom-probe":
                    mode = ApplicationMode.KiwoomProbe;
                    break;
                case "--self-test":
                    mode = ApplicationMode.SelfTest;
                    break;
                case "--symbol":
                    symbols.Add(RequiredValue(args, ref index, argument));
                    break;
                case "--symbols":
                    symbols.AddRange(RequiredValue(args, ref index, argument)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--timeframe":
                    timeframe = ParseTimeframe(RequiredValue(args, ref index, argument));
                    break;
                case "--count":
                    historyCount = ParsePositiveInt(RequiredValue(args, ref index, argument), argument);
                    break;
                case "--realtime-seconds":
                    realtimeProbeSeconds = ParseNonNegativeInt(
                        RequiredValue(args, ref index, argument), argument);
                    break;
                case "--profile":
                    profilePath = Path.GetFullPath(
                        RequiredValue(args, ref index, argument));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (symbols.Count == 0)
        {
            if (mode == ApplicationMode.Replay || mode == ApplicationMode.SelfTest)
            {
                for (int index = 1; index <= 20; index++) symbols.Add($"S{index:000}");
            }
            else
            {
                KiwoomOptions options = KiwoomOptions.FromEnvironment();
                symbols.Add(options.DefaultSymbol);
            }
        }

        string[] normalized = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
            throw new ArgumentException("At least one symbol is required.");

        timeframe.Validate();
        return new AppOptions(
            mode,
            normalized,
            timeframe,
            historyCount,
            realtimeProbeSeconds,
            profilePath);
    }

    internal static CandleTimeframe ParseTimeframe(string value)
    {
        string text = value.Trim();
        if (text.Equals("D", StringComparison.OrdinalIgnoreCase)) return CandleTimeframe.Day;
        if (text.Equals("W", StringComparison.OrdinalIgnoreCase)) return CandleTimeframe.Week;
        if (text.Equals("M", StringComparison.Ordinal)) return CandleTimeframe.Month;
        if (text.EndsWith("T", StringComparison.OrdinalIgnoreCase))
            return CandleTimeframe.Tick(ParsePositiveInt(text[..^1], "timeframe"));
        if (text.EndsWith("m", StringComparison.OrdinalIgnoreCase))
            return CandleTimeframe.Minute(ParsePositiveInt(text[..^1], "timeframe"));
        throw new ArgumentException($"Unsupported timeframe: {value}");
    }

    internal static bool TryParseTimeframe(
        string value,
        out CandleTimeframe timeframe)
    {
        try
        {
            timeframe = ParseTimeframe(value);
            return true;
        }
        catch (ArgumentException)
        {
            timeframe = default;
            return false;
        }
    }

    private static string GetDefaultProfilePath()
    {
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;
        return Path.Combine(root, "ChartKit", "chart-profile.json");
    }

    private static string RequiredValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
            throw new ArgumentException($"Missing value for {option}.");
        return args[index];
    }

    private static int ParsePositiveInt(string value, string option)
    {
        if (!int.TryParse(value, out int result) || result <= 0)
            throw new ArgumentException($"{option} requires a positive integer.");
        return result;
    }

    private static int ParseNonNegativeInt(string value, string option)
    {
        if (!int.TryParse(value, out int result) || result < 0)
            throw new ArgumentException($"{option} requires a non-negative integer.");
        return result;
    }
}
