using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Engine;

internal abstract record EngineMessage(string Symbol);

internal sealed record EventMessage(CandleEvent Value)
    : EngineMessage(Value.Symbol);

internal sealed record HistoryMessage(
    string TargetSymbol,
    IReadOnlyList<Candle> Candles,
    TaskCompletionSource Completion)
    : EngineMessage(TargetSymbol);
