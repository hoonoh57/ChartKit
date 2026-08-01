using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.DataSources;

internal sealed class RealtimeCandleBuilder
{
    private readonly CandleTimeframe _timeframe;
    private Candle? _current;
    private int _tickCount;

    public RealtimeCandleBuilder(
        CandleTimeframe timeframe,
        Candle? seed,
        int seedTickCount)
    {
        _timeframe = timeframe;
        _current = seed;
        _tickCount = Math.Max(0, seedTickCount);
    }

    public bool HasSeed => _current.HasValue;

    public bool TryApply(
        DateTime tradeTime,
        float price,
        long quantity,
        out MarketEventKind kind,
        out Candle candle)
    {
        return _timeframe.Unit == CandleUnit.Tick
            ? TryApplyTick(tradeTime, price, quantity, out kind, out candle)
            : TryApplyMinute(tradeTime, price, quantity, out kind, out candle);
    }

    private bool TryApplyTick(
        DateTime tradeTime,
        float price,
        long quantity,
        out MarketEventKind kind,
        out Candle candle)
    {
        if (_current.HasValue)
        {
            Candle current = _current.Value;
            if (tradeTime.Date < current.TradingDate ||
                (tradeTime.Date == current.TradingDate &&
                 tradeTime < current.CloseTime))
            {
                kind = default;
                candle = default;
                return false;
            }
        }

        bool append = !_current.HasValue ||
                      _tickCount >= _timeframe.Value ||
                      _current.Value.TradingDate != tradeTime.Date;
        if (append)
        {
            long sequence = _current?.Sequence + 1 ?? 0;
            _current = new Candle(
                tradeTime,
                tradeTime,
                price,
                price,
                price,
                price,
                quantity,
                false,
                sequence);
            _tickCount = 1;
            kind = MarketEventKind.Append;
        }
        else
        {
            Candle current = _current!.Value;
            _current = current with
            {
                CloseTime = tradeTime,
                High = Math.Max(current.High, price),
                Low = Math.Min(current.Low, price),
                Close = price,
                Volume = current.Volume + quantity,
                IsFinal = false
            };
            _tickCount++;
            kind = MarketEventKind.Update;
        }
        candle = _current.Value;
        return true;
    }

    private bool TryApplyMinute(
        DateTime tradeTime,
        float price,
        long quantity,
        out MarketEventKind kind,
        out Candle candle)
    {
        int interval = Math.Max(1, _timeframe.Value);
        int totalMinutes = tradeTime.Hour * 60 + tradeTime.Minute;
        int bucketMinutes = totalMinutes / interval * interval;
        DateTime bucketOpen = tradeTime.Date.AddMinutes(bucketMinutes);
        DateTime bucketClose = bucketOpen.AddMinutes(interval);

        if (!_current.HasValue || _current.Value.OpenTime < bucketOpen)
        {
            long sequence = _current?.Sequence + 1 ?? 0;
            _current = new Candle(
                bucketOpen,
                bucketClose,
                price,
                price,
                price,
                price,
                quantity,
                false,
                sequence);
            kind = MarketEventKind.Append;
        }
        else if (_current.Value.OpenTime == bucketOpen)
        {
            Candle current = _current.Value;
            _current = current with
            {
                High = Math.Max(current.High, price),
                Low = Math.Min(current.Low, price),
                Close = price,
                Volume = current.Volume + quantity,
                IsFinal = false
            };
            kind = MarketEventKind.Update;
        }
        else
        {
            kind = default;
            candle = default;
            return false;
        }

        candle = _current.Value;
        return true;
    }
}
