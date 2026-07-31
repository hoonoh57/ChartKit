namespace ChartKit.CSharp.DataSources;

public static class EnvFile
{
    private static int _loaded;

    public static void Load(string? explicitPath = null)
    {
        if (Interlocked.Exchange(ref _loaded, 1) != 0 && explicitPath is null) return;

        string? path = explicitPath;
        if (string.IsNullOrWhiteSpace(path)) path = FindDefaultPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        foreach (string sourceLine in File.ReadLines(path))
        {
            string line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int separator = line.IndexOf('=');
            if (separator <= 0) continue;

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    public static string Get(string key, string fallback = "") =>
        Environment.GetEnvironmentVariable(key)?.Trim() is { Length: > 0 } value
            ? value
            : fallback;

    public static bool GetBoolean(string key, bool fallback = false)
    {
        string value = Get(key);
        if (value.Length == 0) return fallback;
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindDefaultPath()
    {
        string? fromCurrent = FindInParents(Directory.GetCurrentDirectory());
        if (fromCurrent is not null) return fromCurrent;
        return FindInParents(AppContext.BaseDirectory);
    }

    private static string? FindInParents(string start)
    {
        DirectoryInfo? directory = new(start);
        for (int depth = 0; depth < 8 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return null;
    }
}
