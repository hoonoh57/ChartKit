using System.Text;
using System.Text.Json;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public sealed partial class KiwoomRestDataSource : IInstrumentSearchSource
{
    private static readonly InstrumentMarket[] InstrumentMarkets =
    [
        new("0", "KOSPI", true),
        new("10", "KOSDAQ", true),
        new("8", "ETF", false)
    ];

    private readonly SemaphoreSlim _instrumentCatalogGate = new(1, 1);
    private InstrumentSearchResult[]? _instrumentCatalog;

    public async Task<IReadOnlyList<InstrumentSearchResult>> SearchInstrumentsAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        int requestedLimit = Math.Clamp(limit, 1, 100);
        InstrumentSearchResult[] catalog =
            await EnsureInstrumentCatalogAsync(cancellationToken).ConfigureAwait(false);

        string normalizedQuery = NormalizeInstrumentSearchText(query);
        if (normalizedQuery.Length == 0)
            return Array.Empty<InstrumentSearchResult>();

        return catalog
            .Select(item => new RankedInstrument(item, Score(item, normalizedQuery)))
            .Where(static item => item.Score < int.MaxValue)
            .OrderBy(static item => item.Score)
            .ThenBy(static item => item.Value.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Value.Symbol, StringComparer.Ordinal)
            .Take(requestedLimit)
            .Select(static item => item.Value)
            .ToArray();
    }

    private async Task<InstrumentSearchResult[]> EnsureInstrumentCatalogAsync(
        CancellationToken cancellationToken)
    {
        InstrumentSearchResult[]? cached = Volatile.Read(ref _instrumentCatalog);
        if (cached is not null) return cached;

        await _instrumentCatalogGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _instrumentCatalog;
            if (cached is not null) return cached;

            var bySymbol = new Dictionary<string, InstrumentSearchResult>(
                StringComparer.Ordinal);
            List<Exception>? optionalFailures = null;

            foreach (InstrumentMarket market in InstrumentMarkets)
            {
                try
                {
                    await AppendMarketAsync(
                            market,
                            bySymbol,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (!market.Required)
                {
                    optionalFailures ??= [];
                    optionalFailures.Add(exception);
                }
            }

            if (bySymbol.Count == 0)
            {
                if (optionalFailures is { Count: > 0 })
                {
                    throw new AggregateException(
                        "Kiwoom instrument catalog was empty.",
                        optionalFailures);
                }

                throw new InvalidOperationException(
                    "Kiwoom instrument catalog was empty.");
            }

            cached = bySymbol.Values
                .OrderBy(static item => item.DisplayName, StringComparer.Ordinal)
                .ThenBy(static item => item.Symbol, StringComparer.Ordinal)
                .ToArray();
            Volatile.Write(ref _instrumentCatalog, cached);
            return cached;
        }
        finally
        {
            _instrumentCatalogGate.Release();
        }
    }

    private async Task AppendMarketAsync(
        InstrumentMarket market,
        Dictionary<string, InstrumentSearchResult> destination,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["mrkt_tp"] = market.Code
        });
        string continuation = "N";
        string nextKey = string.Empty;

        for (int page = 0; page < 20; page++)
        {
            string requestedKey = nextKey;
            using KiwoomJsonResponse response = await _session.PostJsonAsync(
                    "/api/dostk/stkinfo",
                    "ka10099",
                    body,
                    continuation,
                    nextKey,
                    cancellationToken)
                .ConfigureAwait(false);

            JsonElement root = response.Document.RootElement;
            int returnCode = KiwoomJson.ReadInt(root, "return_code");
            if (returnCode != 0)
            {
                throw new InvalidOperationException(
                    $"Kiwoom ka10099 failed for market {market.Code}: " +
                    KiwoomJson.Text(root, "return_msg"));
            }

            var rows = new List<JsonElement>();
            if (root.TryGetProperty("list", out JsonElement list) &&
                list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement row in list.EnumerateArray())
                {
                    if (row.ValueKind == JsonValueKind.Object)
                        rows.Add(row);
                }
            }
            else
            {
                KiwoomJson.AppendFirstObjectArray(root, rows);
            }

            foreach (JsonElement row in rows)
            {
                string symbol = NormalizeCatalogSymbol(
                    KiwoomJson.Text(row, "code", "stk_cd", "stock_code"));
                string displayName = KiwoomJson.Text(
                        row,
                        "name",
                        "stk_nm",
                        "stock_name")
                    .Trim();
                if (!IsSixDigitKrxSymbol(symbol) || displayName.Length == 0)
                    continue;

                string resolvedMarket = KiwoomJson.Text(
                        row,
                        "marketName",
                        "market_name",
                        "mrkt_nm")
                    .Trim();
                if (resolvedMarket.Length == 0) resolvedMarket = market.Name;
                bool nxtEnabled = string.Equals(
                    KiwoomJson.Text(
                        row,
                        "nxtEnable",
                        "nxt_enable",
                        "nxt_yn"),
                    "Y",
                    StringComparison.OrdinalIgnoreCase);

                var candidate = new InstrumentSearchResult(
                    symbol,
                    displayName,
                    resolvedMarket,
                    nxtEnabled);
                if (!destination.TryGetValue(symbol, out InstrumentSearchResult? current) ||
                    IsPreferred(candidate, current))
                {
                    destination[symbol] = candidate;
                }
            }

            continuation = response.Continuation.Trim().ToUpperInvariant();
            nextKey = response.NextKey.Trim();
            if (continuation != "Y" ||
                nextKey.Length == 0 ||
                string.Equals(nextKey, requestedKey, StringComparison.Ordinal))
                break;
        }
    }

    private static bool IsPreferred(
        InstrumentSearchResult candidate,
        InstrumentSearchResult current) =>
        !string.Equals(candidate.Market, "ETF", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(current.Market, "ETF", StringComparison.OrdinalIgnoreCase);

    private static int Score(
        InstrumentSearchResult item,
        string normalizedQuery)
    {
        string symbol = NormalizeInstrumentSearchText(item.Symbol);
        string name = NormalizeInstrumentSearchText(item.DisplayName);
        if (string.Equals(symbol, normalizedQuery, StringComparison.Ordinal)) return 0;
        if (symbol.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 1;
        if (string.Equals(name, normalizedQuery, StringComparison.Ordinal)) return 2;
        if (name.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 3;
        return name.Contains(normalizedQuery, StringComparison.Ordinal)
            ? 4
            : int.MaxValue;
    }

    private static string NormalizeInstrumentSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (char character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        }
        return builder.ToString();
    }

    private static string NormalizeCatalogSymbol(string value)
    {
        string symbol = value.Trim().ToUpperInvariant();
        if (symbol.Length == 7 && symbol[0] == 'A' &&
            symbol.Skip(1).All(char.IsDigit))
            symbol = symbol[1..];
        int suffix = symbol.IndexOf('_');
        return suffix > 0 ? symbol[..suffix] : symbol;
    }

    private static bool IsSixDigitKrxSymbol(string value) =>
        value.Length == 6 && value.All(char.IsDigit);

    private readonly record struct InstrumentMarket(
        string Code,
        string Name,
        bool Required);

    private readonly record struct RankedInstrument(
        InstrumentSearchResult Value,
        int Score);
}
