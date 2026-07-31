using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

public sealed record ReplayOptions(
    TimeSpan? EventInterval = null,
    int UpdatesPerCandle = 8,
    int Seed = 1516)
{
    public TimeSpan EffectiveEventInterval =>
        EventInterval ?? TimeSpan.FromMilliseconds(40);
}

public sealed class ReplayDataSource : IMarketDataSource
{
    private readonly ReplayOptions _options;
    private readonly ConcurrentDictionary<string, Candle> _historySeeds =
        new(StringComparer.Ordinal);

    public ReplayDataSource(ReplayOptions? options = null)
    {
        _options = options ?? new ReplayOptions();
        if (_options.UpdatesPerCandle <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public string Name => "CSharp deterministic replay";

    public Task<IReadOnlyList<Candle>> GetHistoryAsync(
        HistoryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.Timeframe.Validate();
        int count = Math.Max(1, request.Count);
        int symbolOffset = StableOffset(request.Symbol);
        DateTime end = request.To ?? DateTime.Today.AddHours(15).AddMinutes(30);
        TimeSpan step = Step(request.Timeframe);
        DateTime start = end - TimeSpan.FromTicks(step.Ticks * count);
        var output = new Candle[count];
        float previous = 1000f + symbolOffset;
        for (int index = 0; index < count; index++)
        {
            double wave = Math.Sin((index + symbolOffset) / 6d) * 7d;
            float close = 1000f + symbolOffset + index * 0.12f + (float)wave;
            DateTime openTime = start + TimeSpan.FromTicks(step.Ticks * index);
            DateTime closeTime = openTime + step;
            output[index] = new Candle(
                openTime,
                closeTime,
                previous,
                Math.Max(previous, close) + 1.2f,
                Math.Min(previous, close) - 1.2f,
                close,
                1_000L + index * 31L + symbolOffset,
                true,
                index);
            previous = close;
        }
        _historySeeds[request.Symbol] = output[^1];
        return Task.FromResult<IReadOnlyList<Candle>>(output);
    }

    public async IAsyncEnumerable<CandleEvent> StreamAsync(
        IReadOnlyList<string> symbols,
        CandleTimeframe timeframe,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        timeframe.Validate();
        var states = new Dictionary<string, ReplayState>(StringComparer.Ordinal);
        foreach (string source in symbols)
        {
            string symbol = source.Trim();
            if (symbol.Length == 0 || states.ContainsKey(symbol)) continue;
            _historySeeds.TryGetValue(symbol, out Candle seed);
            bool hasSeed = _historySeeds.ContainsKey(symbol);
            states.Add(symbol, ReplayState.Create(
                symbol,
                timeframe,
                hasSeed ? seed : null,
                hasSeed ? _options.UpdatesPerCandle : 0));
        }
        var random = new Random(_options.Seed);

        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (KeyValuePair<string, ReplayState> pair in states)
            {
                ReplayState state = pair.Value;
                bool append = state.UpdateCount >= _options.UpdatesPerCandle;
                if (append) state.BeginNext(timeframe);
                float movement = (float)((random.NextDouble() - 0.48d) * 3d);
                state.Apply(movement, random.Next(10, 250));
                yield return CandleEvent.Create(
                    pair.Key,
                    append ? MarketEventKind.Append : MarketEventKind.Update,
                    state.Candle,
                    state.SourceSequence++);
            }
            await Task.Delay(_options.EffectiveEventInterval, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static TimeSpan Step(CandleTimeframe timeframe) => timeframe.Unit switch
    {
        CandleUnit.Minute => TimeSpan.FromMinutes(timeframe.Value),
        CandleUnit.Tick => TimeSpan.FromSeconds(Math.Max(1, timeframe.Value / 10d)),
        CandleUnit.Day => TimeSpan.FromDays(1),
        CandleUnit.Week => TimeSpan.FromDays(7),
        CandleUnit.Month => TimeSpan.FromDays(30),
        _ => TimeSpan.FromMinutes(1)
    };

    private static int StableOffset(string symbol)
    {
        int value = 17;
        foreach (char character in symbol)
            value = unchecked(value * 31 + character);
        return Math.Abs(value % 300);
    }

    private sealed class ReplayState
    {
        private ReplayState(Candle candle, int updateCount)
        {
            Candle = candle;
            UpdateCount = updateCount;
            SourceSequence = candle.Sequence + 1;
        }

        public Candle Candle { get; private set; }
        public int UpdateCount { get; private set; }
        public long SourceSequence { get; set; }

        public static ReplayState Create(
            string symbol,
            CandleTimeframe timeframe,
            Candle? seed,
            int updateCount)
        {
            if (seed.HasValue) return new ReplayState(seed.Value, updateCount);
            float price = 1000f + StableOffset(symbol);
            DateTime now = DateTime.Now;
            TimeSpan step = Step(timeframe);
            var candle = new Candle(
                now,
                now + step,
                price,
                price,
                price,
                price,
                0,
                false,
                0);
            return new ReplayState(candle, updateCount);
        }

        public void BeginNext(CandleTimeframe timeframe)
        {
            TimeSpan step = Step(timeframe);
            DateTime open = Candle.CloseTime;
            Candle = new Candle(
                open,
                open + step,
                Candle.Close,
                Candle.Close,
                Candle.Close,
                Candle.Close,
                0,
                false,
                Candle.Sequence + 1);
            UpdateCount = 0;
        }

        public void Apply(float movement, long quantity)
        {
            float close = Math.Max(1f, Candle.Close + movement);
            Candle = Candle with
            {
                High = Math.Max(Candle.High, close),
                Low = Math.Min(Candle.Low, close),
                Close = close,
                Volume = Candle.Volume + quantity,
                IsFinal = false
            };
            UpdateCount++;
        }
    }
}
