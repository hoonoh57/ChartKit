using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public sealed partial class KiwoomRestDataSource
{
    public async Task<InstrumentMetadata> GetInstrumentMetadataAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string requested = string.IsNullOrWhiteSpace(symbol)
            ? _session.Options.DefaultSymbol
            : symbol.Trim();
        string queryCode = NormalizeInstrumentCode(requested);
        string displayName;
        try
        {
            displayName = await GetStockNameAsync(queryCode, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            displayName = queryCode;
        }
        if (string.IsNullOrWhiteSpace(displayName)) displayName = queryCode;

        string market = requested.EndsWith("_AL", StringComparison.OrdinalIgnoreCase)
            ? "NXT"
            : "KRX";
        return new InstrumentMetadata(
            requested,
            displayName,
            market,
            Name,
            DateTimeOffset.UtcNow);
    }

    private static string NormalizeInstrumentCode(string symbol)
    {
        int suffix = symbol.IndexOf('_');
        return suffix > 0 ? symbol[..suffix] : symbol;
    }
}
