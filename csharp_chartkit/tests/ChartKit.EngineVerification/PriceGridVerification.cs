using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.EngineVerification;

internal static class PriceGridVerification
{
    public static void Run()
    {
        IPriceGrid grid = KoreanEquityPriceGrid.Instance;
        if (grid.GetTickSize(1_718_000f) != 1_000)
            throw new InvalidOperationException("High-price Korean equity tick size mismatch.");
        if (grid.Snap(1_708_382f) != 1_708_000f)
            throw new InvalidOperationException("Korean equity nearest tick snapping failed.");
        if (grid.Snap(49_999f, PriceSnapMode.Ceiling) != 50_000f)
            throw new InvalidOperationException("Korean equity price-band boundary snapping failed.");

        var candles = new Candle[60];
        DateTime start = new(2026, 7, 31, 18, 0, 0, DateTimeKind.Local);
        for (int index = 0; index < candles.Length; index++)
        {
            float close = 1_690_000f + index * 500f;
            candles[index] = new Candle(
                start.AddMinutes(index),
                start.AddMinutes(index + 1),
                close - 1_000f,
                close + 2_000f,
                close - 2_000f,
                close,
                1_000L + index,
                true,
                index);
        }

        var snapshot = new SymbolSnapshot(
            "000660_AL",
            candles,
            Array.Empty<IndicatorSeriesSnapshot>(),
            1L,
            DateTimeOffset.UtcNow);
        var viewport = new ChartViewport(
            visibleBars: 40,
            minimumVisibleBars: 20,
            maximumVisibleBars: 100,
            rightBlankBars: 12);
        ChartWindow window = viewport.Resolve(candles.Length);
        var builder = new ChartFrameBuilder();
        var frame = new ChartFrame();
        builder.Build(
            snapshot,
            window,
            1200f,
            800f,
            target: frame,
            priceGrid: grid,
            transform: viewport.Transform);

        float visibleHigh = float.MinValue;
        for (int index = window.StartIndex; index < window.EndExclusive; index++)
            visibleHigh = Math.Max(visibleHigh, candles[index].High);
        if (frame.PriceRange.Maximum < visibleHigh + 4_000f)
            throw new InvalidOperationException("Chart top safety margin was too small.");
        if (frame.Window.RightBlankBars != 12 ||
            frame.X(window.Count - 1) >= frame.MainPanel.Right - frame.BarStep * 10f)
            throw new InvalidOperationException("Latest candle did not retain right-side future space.");

        for (int index = 0; index < frame.PriceTickCount; index++)
        {
            float value = frame.PriceTicks[index].Value;
            if (grid.Snap(value) != value)
                throw new InvalidOperationException($"Axis emitted a non-tradable price: {value}.");
        }

        var cursor = new ChartCursorController();
        ChartCursorSnapshot selected = cursor.Update(
            frame.X(10),
            frame.PriceY(1_708_382f),
            snapshot,
            frame);
        if (!selected.IsVisible || selected.AxisValue != 1_708_000f)
            throw new InvalidOperationException(
                $"Crosshair price was not snapped to a tradable tick: {selected.AxisValue}.");

        NumericRange before = frame.PriceRange;
        viewport.PanPricePixels(80f, frame.MainPanel.Height);
        builder.Build(
            snapshot,
            window,
            1200f,
            800f,
            target: frame,
            priceGrid: grid,
            transform: viewport.Transform);
        if (frame.PriceRange.Minimum <= before.Minimum ||
            frame.PriceRange.Maximum <= before.Maximum)
            throw new InvalidOperationException("Vertical chart drag did not move the price range.");

        Console.WriteLine("csharp_korean_price_grid=PASS");
        Console.WriteLine("csharp_chart_safety_space=PASS");
    }
}
