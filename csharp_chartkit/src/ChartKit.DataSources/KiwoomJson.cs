using System.Globalization;
using System.Text.Json;

namespace ChartKit.CSharp.DataSources;

internal static class KiwoomJson
{
    public static void AppendFirstObjectArray(
        JsonElement root,
        List<JsonElement> destination)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name.StartsWith('_') || property.Value.ValueKind != JsonValueKind.Array)
                continue;
            JsonElement.ArrayEnumerator enumerator = property.Value.EnumerateArray();
            if (!enumerator.MoveNext() || enumerator.Current.ValueKind != JsonValueKind.Object)
                continue;
            destination.Add(enumerator.Current.Clone());
            while (enumerator.MoveNext()) destination.Add(enumerator.Current.Clone());
            return;
        }
    }

    public static int ReadInt(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out JsonElement element)) return 0;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int number))
            return number;
        return element.ValueKind == JsonValueKind.String &&
               int.TryParse(element.GetString(), NumberStyles.Integer,
                   CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }

    public static double? Number(JsonElement row, string key)
    {
        if (!row.TryGetProperty(key, out JsonElement element)) return null;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double number))
            return number;
        if (element.ValueKind != JsonValueKind.String) return null;
        string raw = (element.GetString() ?? "")
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("+", "", StringComparison.Ordinal)
            .Trim();
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    public static string Text(JsonElement row, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (!row.TryGetProperty(key, out JsonElement element)) continue;
            string value = element.ValueKind == JsonValueKind.String
                ? element.GetString() ?? ""
                : element.ToString();
            if (value.Length > 0) return value;
        }
        return "";
    }

    public static DateTime ParseTime(string source, bool daily)
    {
        if (string.IsNullOrWhiteSpace(source)) return DateTime.Now;
        string text = source.Trim();
        if (daily && DateTime.TryParseExact(
                text, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime date))
            return date;

        foreach (string format in new[]
                 {
                     "yyyyMMddHHmmss", "yyyyMMddHHmm", "HHmmss", "HHmm"
                 })
        {
            if (!DateTime.TryParseExact(
                    text, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime parsed))
                continue;
            if (format.StartsWith("HH", StringComparison.Ordinal))
                return DateTime.Today.Add(parsed.TimeOfDay);
            return parsed;
        }
        return DateTime.Now;
    }

    public static double? RealtimeNumber(JsonElement values, string key) =>
        Number(values, key);
}
