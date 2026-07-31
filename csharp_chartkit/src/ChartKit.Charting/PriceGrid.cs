namespace ChartKit.CSharp.Charting;

public enum PriceSnapMode
{
    Nearest,
    Floor,
    Ceiling
}

public interface IPriceGrid
{
    float Snap(float price, PriceSnapMode mode = PriceSnapMode.Nearest);
    int GetTickSize(float price);
    float SelectAxisStep(NumericRange range, int targetTickCount);
}

public sealed class KoreanEquityPriceGrid : IPriceGrid
{
    public static KoreanEquityPriceGrid Instance { get; } = new();

    private KoreanEquityPriceGrid()
    {
    }

    public int GetTickSize(float price)
    {
        float value = Math.Max(0f, price);
        if (value < 2_000f) return 1;
        if (value < 5_000f) return 5;
        if (value < 20_000f) return 10;
        if (value < 50_000f) return 50;
        if (value < 200_000f) return 100;
        if (value < 500_000f) return 500;
        return 1_000;
    }

    public float Snap(float price, PriceSnapMode mode = PriceSnapMode.Nearest)
    {
        if (!float.IsFinite(price)) return price;
        if (price <= 0f) return 0f;

        int tick = GetTickSize(price);
        double units = price / tick;
        double snappedUnits = mode switch
        {
            PriceSnapMode.Floor => Math.Floor(units),
            PriceSnapMode.Ceiling => Math.Ceiling(units),
            _ => Math.Round(units, MidpointRounding.AwayFromZero)
        };
        float snapped = (float)(snappedUnits * tick);

        // A snap can land exactly on the next price band. Re-snap once using
        // the destination band's tick so every returned value is orderable.
        int destinationTick = GetTickSize(snapped);
        if (destinationTick == tick) return snapped;

        double destinationUnits = snapped / destinationTick;
        double destinationSnapped = mode switch
        {
            PriceSnapMode.Floor => Math.Floor(destinationUnits),
            PriceSnapMode.Ceiling => Math.Ceiling(destinationUnits),
            _ => Math.Round(destinationUnits, MidpointRounding.AwayFromZero)
        };
        return (float)(destinationSnapped * destinationTick);
    }

    public float SelectAxisStep(NumericRange range, int targetTickCount)
    {
        if (!range.IsValid) return 1f;
        int target = Math.Max(2, targetTickCount);
        int minimumTick = GetTickSize(range.Maximum);
        double rawStep = range.Span / Math.Max(1, target - 1);
        double multiples = Math.Max(1d, rawStep / minimumTick);
        double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(multiples)));
        double normalized = multiples / magnitude;
        double nice = normalized <= 1d ? 1d :
                      normalized <= 2d ? 2d :
                      normalized <= 5d ? 5d : 10d;
        double step = nice * magnitude * minimumTick;
        return (float)Math.Max(minimumTick, step);
    }
}

public sealed class FixedTickPriceGrid : IPriceGrid
{
    private readonly int _tickSize;

    public FixedTickPriceGrid(int tickSize)
    {
        if (tickSize <= 0) throw new ArgumentOutOfRangeException(nameof(tickSize));
        _tickSize = tickSize;
    }

    public int GetTickSize(float price) => _tickSize;

    public float Snap(float price, PriceSnapMode mode = PriceSnapMode.Nearest)
    {
        if (!float.IsFinite(price)) return price;
        double units = price / _tickSize;
        double snapped = mode switch
        {
            PriceSnapMode.Floor => Math.Floor(units),
            PriceSnapMode.Ceiling => Math.Ceiling(units),
            _ => Math.Round(units, MidpointRounding.AwayFromZero)
        };
        return (float)(snapped * _tickSize);
    }

    public float SelectAxisStep(NumericRange range, int targetTickCount)
    {
        if (!range.IsValid) return _tickSize;
        double raw = range.Span / Math.Max(1, targetTickCount - 1);
        int multiples = Math.Max(1, (int)Math.Ceiling(raw / _tickSize));
        return multiples * _tickSize;
    }
}
