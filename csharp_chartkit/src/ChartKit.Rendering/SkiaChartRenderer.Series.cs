using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;
using SkiaSharp;

namespace ChartKit.CSharp.Rendering;

public sealed partial class SkiaChartRenderer
{
    private void DrawIndicatorSeries(
        SKCanvas canvas,
        SymbolSnapshot snapshot,
        ChartFrame frame)
    {
        int colorIndex = 0;
        foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
        {
            int panel = series.Descriptor.PanelIndex;
            ChartRectF rect = panel == 0
                ? frame.MainPanel
                : panel <= ChartFrame.MaximumPanelIndex
                    ? frame.PanelRects[panel]
                    : ChartRectF.Empty;
            if (rect.IsEmpty) continue;

            for (int valueIndex = 0;
                 valueIndex < series.Descriptor.ValueCount;
                 valueIndex++)
            {
                SeriesKind kind = series.Descriptor.Kinds[valueIndex];
                if (kind == SeriesKind.Meta) continue;

                SKPaint paint = _seriesPaints[colorIndex % _seriesPaints.Length];
                colorIndex++;
                if (kind == SeriesKind.Histogram)
                    DrawHistogram(canvas, series, valueIndex, panel, rect, paint, frame);
                else
                    DrawLineSeries(canvas, series, valueIndex, panel, paint, frame);
            }
        }
    }

    private void DrawLineSeries(
        SKCanvas canvas,
        IndicatorSeriesSnapshot series,
        int valueIndex,
        int panel,
        SKPaint paint,
        ChartFrame frame)
    {
        _seriesPath.Rewind();
        int pointStart = Math.Min(frame.Window.StartIndex, series.Points.Length);
        int pointEnd = Math.Min(frame.Window.EndExclusive, series.Points.Length);
        bool active = false;

        for (int pointIndex = pointStart;
             pointIndex < pointEnd;
             pointIndex++)
        {
            float value = series.Points[pointIndex].GetValue(valueIndex);
            if (!float.IsFinite(value))
            {
                active = false;
                continue;
            }

            int visibleIndex = pointIndex - frame.Window.StartIndex;
            float x = frame.X(visibleIndex);
            float y = panel == 0
                ? frame.PriceY(value)
                : frame.PanelY(panel, value);
            if (active) _seriesPath.LineTo(x, y);
            else
            {
                _seriesPath.MoveTo(x, y);
                active = true;
            }
        }

        canvas.DrawPath(_seriesPath, paint);
    }

    private void DrawHistogram(
        SKCanvas canvas,
        IndicatorSeriesSnapshot series,
        int valueIndex,
        int panel,
        ChartRectF rect,
        SKPaint paint,
        ChartFrame frame)
    {
        _histogramPath.Rewind();
        int pointStart = Math.Min(frame.Window.StartIndex, series.Points.Length);
        int pointEnd = Math.Min(frame.Window.EndExclusive, series.Points.Length);
        float zero = panel == 0
            ? frame.PriceY(0f)
            : frame.PanelY(panel, 0f);
        zero = Math.Clamp(zero, rect.Top, rect.Bottom);
        float halfWidth = Math.Max(0.5f, frame.BodyWidth * 0.36f);

        for (int pointIndex = pointStart;
             pointIndex < pointEnd;
             pointIndex++)
        {
            float value = series.Points[pointIndex].GetValue(valueIndex);
            if (!float.IsFinite(value)) continue;

            int visibleIndex = pointIndex - frame.Window.StartIndex;
            float x = frame.X(visibleIndex);
            float y = panel == 0
                ? frame.PriceY(value)
                : frame.PanelY(panel, value);
            _histogramPath.AddRect(new SKRect(
                x - halfWidth,
                Math.Min(y, zero),
                x + halfWidth,
                Math.Max(y, zero)));
        }

        SKPaintStyle original = paint.Style;
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawPath(_histogramPath, paint);
        paint.Style = original;
    }
}
