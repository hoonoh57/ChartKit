using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Charting;

public sealed class ChartFrameBuilder
{
    public ChartFrame Build(
        SymbolSnapshot snapshot,
        ChartWindow window,
        float width,
        float height,
        ChartLayoutOptions? options = null,
        ChartFrame? target = null,
        IPriceGrid? priceGrid = null,
        ChartViewTransform? transform = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ChartLayoutOptions settings = options ?? ChartLayoutOptions.Default;
        settings.Validate();
        if (width <= 0f) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0f) throw new ArgumentOutOfRangeException(nameof(height));
        if (window.StartIndex < 0 || window.Count <= 0 ||
            window.EndExclusive > snapshot.Candles.Length)
            throw new ArgumentOutOfRangeException(nameof(window));

        ChartFrame frame = target ?? new ChartFrame();
        frame.Window = window;
        frame.PriceGrid = priceGrid ?? KoreanEquityPriceGrid.Instance;
        frame.Bounds = new ChartRectF(0f, 0f, width, height);
        Array.Clear(frame.PanelVisible);
        Array.Clear(frame.PanelRects);
        Array.Clear(frame.PanelRanges);
        Array.Clear(frame.PanelTickCounts);
        frame.PriceTickCount = 0;
        frame.TimeTickCount = 0;

        int panelCount = ResolveVisiblePanels(snapshot, frame);
        ResolvePanelLayout(frame, settings, panelCount);
        ResolveRanges(
            snapshot,
            frame,
            settings,
            transform ?? ChartViewTransform.Default);
        ResolvePriceTicks(frame, settings.TargetPriceTickCount);
        ResolvePanelTicks(frame, settings.TargetPanelTickCount);
        ResolveTimeTicks(snapshot, frame, settings.TargetTimeTickCount);
        return frame;
    }

    private static int ResolveVisiblePanels(SymbolSnapshot snapshot, ChartFrame frame)
    {
        int panelCount = 0;
        foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
        {
            int panel = series.Descriptor.PanelIndex;
            if (panel <= 0 || panel > ChartFrame.MaximumPanelIndex ||
                frame.PanelVisible[panel])
                continue;
            frame.PanelVisible[panel] = true;
            panelCount++;
        }
        return panelCount;
    }

    private static void ResolvePanelLayout(
        ChartFrame frame,
        ChartLayoutOptions options,
        int panelCount)
    {
        float left = frame.Bounds.Left + options.LeftPadding;
        float right = Math.Max(left + 1f, frame.Bounds.Right - options.RightPadding);
        float top = frame.Bounds.Top + options.TopPadding;
        float bottom = Math.Max(top + 1f, frame.Bounds.Bottom - options.BottomPadding);
        float totalHeight = bottom - top;
        float mainHeight = totalHeight * options.MainPanelRatio;
        float volumeHeight = totalHeight * options.VolumePanelRatio;

        frame.MainPanel = new ChartRectF(left, top, right, top + mainHeight);
        frame.VolumePanel = new ChartRectF(
            left,
            frame.MainPanel.Bottom,
            right,
            frame.MainPanel.Bottom + volumeHeight);
        frame.TimeAxis = new ChartRectF(left, bottom, right, frame.Bounds.Bottom);

        float remaining = Math.Max(0f, bottom - frame.VolumePanel.Bottom);
        float panelHeight = panelCount == 0 ? 0f : remaining / panelCount;
        float panelTop = frame.VolumePanel.Bottom;
        for (int panel = 1; panel <= ChartFrame.MaximumPanelIndex; panel++)
        {
            if (!frame.PanelVisible[panel]) continue;
            frame.PanelRects[panel] = new ChartRectF(
                left,
                panelTop,
                right,
                panelTop + panelHeight);
            panelTop += panelHeight;
        }

        frame.BarStep = frame.MainPanel.Width /
                        Math.Max(1, frame.Window.VisibleSlotCount);
        frame.BodyWidth = Math.Max(1f, frame.BarStep * 0.72f);
    }

    private static void ResolveRanges(
        SymbolSnapshot snapshot,
        ChartFrame frame,
        ChartLayoutOptions options,
        ChartViewTransform transform)
    {
        float priceMinimum = float.MaxValue;
        float priceMaximum = float.MinValue;
        long volumeMaximum = 1L;
        for (int index = frame.Window.StartIndex;
             index < frame.Window.EndExclusive;
             index++)
        {
            Candle candle = snapshot.Candles[index];
            priceMinimum = Math.Min(priceMinimum, candle.Low);
            priceMaximum = Math.Max(priceMaximum, candle.High);
            volumeMaximum = Math.Max(volumeMaximum, candle.Volume);
        }

        // Overlay indicators share the price panel and must participate in the
        // range calculation so neither candles nor overlays can hit the roof.
        foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
        {
            if (series.Descriptor.PanelIndex != 0) continue;
            int pointStart = Math.Min(frame.Window.StartIndex, series.Points.Length);
            int pointEnd = Math.Min(frame.Window.EndExclusive, series.Points.Length);
            for (int pointIndex = pointStart; pointIndex < pointEnd; pointIndex++)
            {
                IndicatorPoint point = series.Points[pointIndex];
                for (int valueIndex = 0;
                     valueIndex < series.Descriptor.ValueCount;
                     valueIndex++)
                {
                    if (series.Descriptor.Kinds[valueIndex] == SeriesKind.Meta) continue;
                    float value = point.GetValue(valueIndex);
                    if (!float.IsFinite(value)) continue;
                    priceMinimum = Math.Min(priceMinimum, value);
                    priceMaximum = Math.Max(priceMaximum, value);
                }
            }
        }

        if (priceMinimum == float.MaxValue || priceMaximum == float.MinValue)
        {
            priceMinimum = 0f;
            priceMaximum = 1f;
        }

        IPriceGrid grid = frame.PriceGrid;
        float rawRange = Math.Max(0f, priceMaximum - priceMinimum);
        int referenceTick = grid.GetTickSize(Math.Max(priceMaximum, 0f));
        float minimumMargin = Math.Max(
            referenceTick,
            referenceTick * options.MinimumPriceMarginTicks);
        float basis = Math.Max(rawRange, minimumMargin);
        float topMargin = Math.Max(
            minimumMargin,
            basis * options.PriceTopMarginRatio);
        float bottomMargin = Math.Max(
            minimumMargin,
            basis * options.PriceBottomMarginRatio);

        float minimum = priceMinimum - bottomMargin;
        float maximum = priceMaximum + topMargin;
        float span = Math.Max(referenceTick, maximum - minimum);
        float shift = span * transform.PricePanFraction;
        minimum += shift;
        maximum += shift;
        minimum = grid.Snap(Math.Max(0f, minimum), PriceSnapMode.Floor);
        maximum = grid.Snap(Math.Max(minimum + referenceTick, maximum), PriceSnapMode.Ceiling);
        if (maximum <= minimum)
            maximum = minimum + referenceTick * 2f;

        frame.PriceRange = new NumericRange(minimum, maximum);
        frame.VolumeMaximum = volumeMaximum;

        Span<float> minima = stackalloc float[ChartFrame.MaximumPanelIndex + 1];
        Span<float> maxima = stackalloc float[ChartFrame.MaximumPanelIndex + 1];
        minima.Fill(float.MaxValue);
        maxima.Fill(float.MinValue);

        foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
        {
            int panel = series.Descriptor.PanelIndex;
            if (panel <= 0 || panel > ChartFrame.MaximumPanelIndex ||
                !frame.PanelVisible[panel])
                continue;

            int pointStart = Math.Min(frame.Window.StartIndex, series.Points.Length);
            int pointEnd = Math.Min(frame.Window.EndExclusive, series.Points.Length);
            for (int pointIndex = pointStart; pointIndex < pointEnd; pointIndex++)
            {
                IndicatorPoint point = series.Points[pointIndex];
                for (int valueIndex = 0;
                     valueIndex < series.Descriptor.ValueCount;
                     valueIndex++)
                {
                    if (series.Descriptor.Kinds[valueIndex] == SeriesKind.Meta) continue;
                    float value = point.GetValue(valueIndex);
                    if (!float.IsFinite(value)) continue;
                    minima[panel] = Math.Min(minima[panel], value);
                    maxima[panel] = Math.Max(maxima[panel], value);
                }
            }
        }

        for (int panel = 1; panel <= ChartFrame.MaximumPanelIndex; panel++)
        {
            if (!frame.PanelVisible[panel]) continue;
            float panelMinimum = minima[panel];
            float panelMaximum = maxima[panel];
            if (panelMinimum == float.MaxValue || panelMaximum == float.MinValue)
            {
                panelMinimum = 0f;
                panelMaximum = 1f;
            }
            float range = panelMaximum - panelMinimum;
            float margin = Math.Max(0.001f, range * 0.08f);
            frame.PanelRanges[panel] = new NumericRange(
                panelMinimum - margin,
                panelMaximum + margin);
        }
    }

    private static void ResolvePriceTicks(ChartFrame frame, int targetCount)
    {
        NumericRange range = frame.PriceRange;
        IPriceGrid grid = frame.PriceGrid;
        float step = grid.SelectAxisStep(range, targetCount);
        double first = Math.Ceiling(range.Minimum / step) * step;
        int count = 0;
        float previous = float.NaN;
        for (double value = first;
             value <= range.Maximum + step * 0.25d &&
             count < ChartFrame.MaximumAxisTickCount;
             value += step)
        {
            float tickValue = grid.Snap((float)value, PriceSnapMode.Nearest);
            if (float.IsFinite(previous) && tickValue <= previous) continue;
            if (tickValue < range.Minimum || tickValue > range.Maximum) continue;
            frame.PriceTicks[count++] = new NumericAxisTick(
                tickValue,
                ChartFrame.MapY(tickValue, range, frame.MainPanel));
            previous = tickValue;
        }
        frame.PriceTickCount = count;
    }

    private static void ResolvePanelTicks(ChartFrame frame, int targetCount)
    {
        for (int panel = 1; panel <= ChartFrame.MaximumPanelIndex; panel++)
        {
            if (!frame.PanelVisible[panel]) continue;
            NumericRange range = frame.PanelRanges[panel];
            ChartRectF rect = frame.PanelRects[panel];
            if (!range.IsValid || rect.IsEmpty) continue;

            double rawStep = range.Span / Math.Max(1, targetCount - 1);
            double step = NiceStep(rawStep);
            int count = 0;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                count = WritePanelTicks(frame, panel, range, rect, step);
                if (count >= 2) break;
                step = NiceStep(step * 0.5d);
            }

            if (count < 2)
            {
                frame.SetPanelTick(
                    panel,
                    0,
                    new NumericAxisTick(range.Minimum, rect.Bottom));
                frame.SetPanelTick(
                    panel,
                    1,
                    new NumericAxisTick(range.Maximum, rect.Top));
                count = 2;
            }
            frame.PanelTickCounts[panel] = count;
        }
    }

    private static int WritePanelTicks(
        ChartFrame frame,
        int panel,
        NumericRange range,
        ChartRectF rect,
        double step)
    {
        double first = Math.Ceiling(range.Minimum / step) * step;
        int count = 0;
        float previous = float.NaN;
        for (double value = first;
             value <= range.Maximum + step * 0.25d &&
             count < ChartFrame.MaximumAxisTickCount;
             value += step)
        {
            float tickValue = (float)value;
            if (!float.IsFinite(tickValue) ||
                tickValue < range.Minimum || tickValue > range.Maximum)
                continue;
            if (float.IsFinite(previous) && tickValue <= previous) continue;

            frame.SetPanelTick(
                panel,
                count++,
                new NumericAxisTick(
                    tickValue,
                    ChartFrame.MapY(tickValue, range, rect)));
            previous = tickValue;
        }
        return count;
    }

    private static double NiceStep(double rawStep)
    {
        if (!double.IsFinite(rawStep) || rawStep <= 0d) return 1d;
        double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(rawStep)));
        double normalized = rawStep / magnitude;
        double nice = normalized <= 1d ? 1d :
                      normalized <= 2d ? 2d :
                      normalized <= 2.5d ? 2.5d :
                      normalized <= 5d ? 5d : 10d;
        return nice * magnitude;
    }

    private static void ResolveTimeTicks(
        SymbolSnapshot snapshot,
        ChartFrame frame,
        int targetCount)
    {
        int count = Math.Min(targetCount, frame.Window.Count);
        int previousCandleIndex = -1;
        int written = 0;
        for (int slot = 0;
             slot < count && written < ChartFrame.MaximumAxisTickCount;
             slot++)
        {
            int relative = count == 1
                ? 0
                : (int)Math.Round(
                    slot * (frame.Window.Count - 1d) / (count - 1d),
                    MidpointRounding.AwayFromZero);
            int candleIndex = frame.Window.StartIndex + relative;
            if (candleIndex == previousCandleIndex) continue;

            Candle candle = snapshot.Candles[candleIndex];
            bool boundary = candleIndex == 0 ||
                            snapshot.Candles[candleIndex - 1].TradingDate != candle.TradingDate;
            frame.TimeTicks[written++] = new TimeAxisTick(
                candle.OpenTime,
                candleIndex,
                frame.X(relative),
                boundary);
            previousCandleIndex = candleIndex;
        }
        frame.TimeTickCount = written;
    }
}
