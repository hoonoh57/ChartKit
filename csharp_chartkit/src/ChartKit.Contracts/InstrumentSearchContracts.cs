namespace ChartKit.CSharp.Contracts;

public sealed record InstrumentSearchResult(
    string Symbol,
    string DisplayName,
    string Market,
    bool NxtEnabled);

public interface IInstrumentSearchSource
{
    Task<IReadOnlyList<InstrumentSearchResult>> SearchInstrumentsAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
