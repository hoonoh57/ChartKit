using ChartKit.CSharp.Contracts;
using SkiaSharp;

namespace ChartKit.CSharp.Rendering;

public sealed partial class SkiaChartRenderer
{
    private void DrawIndicatorSeries(SKCanvas canvas, SymbolSnapshot snapshot)
    {
        int colorIndex = 0;
        foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
        {
            int panel = series.Descriptor.PanelIndex;
            SKRect rect = panel == 0 ? _mainRect :
                panel <= MaximumPanelIndex ? _panelRects[panel] : SKRect.Empty;
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
                    DrawHistogram(canvas, series, valueIndex, panel, rect, paint);
                else
                    DrawLineSeries(canvas, series, valueIndex, panel, rect, paint);
            }
        }
    }

    private void DrawLineSeries(
        SKCanvas canvas,
        IndicatorSeriesSnapshot series,
        int valueIndex,
        int panel,
        SKRect rect,
        SKPaint paint)
    {
        _seriesPath.Rewind();
        int pointStart = Math.Min(_startIndex, series.Points.Length);
        int pointEnd = Math.Min(_startIndex + _visibleCount, series.Points.Length);
        bool active = false;

        for (int pointIndex = pointStart; pointIndex < pointEnd; pointIndex++)
        {
            float value = series.Points[pointIndex].GetValue(valueIndex);
            if (!float.IsFinite(value))
            {
                active = false;
                continue;
            }

            int visibleIndex = pointIndex - _startIndex;
            float x = X(visibleIndex);
            float y = panel == 0
                ? PriceY(value)
                : MapY(value, _panelMinimum[panel], _panelMaximum[panel], rect);
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
        SKRect rect,
        SKPaint paint)
    {
        _histogramPath.Rewind();
        int pointStart = Math.Min(_startIndex, series.Points.Length);
        int pointEnd = Math.Min(_startIndex + _visibleCount, series.Points.Length);
        float zero = panel == 0
            ? PriceY(0f)
            : MapY(0f, _panelMinimum[panel], _panelMaximum[panel], rect);
        zero = Math.Clamp(zero, rect.Top, rect.Bottom);
        float halfWidth = Math.Max(0.5f, _bodyWidth * 0.36f);

        for (int pointIndex = pointStart; pointIndex < pointEnd; pointIndex++)
        {
            float value = series.Points[pointIndex].GetValue(valueIndex);
            if (!float.IsFinite(value)) continue;

            int visibleIndex = pointIndex - _startIndex;
            float x = X(visibleIndex);
            float y = panel == 0
                ? PriceY(value)
                : MapY(value, _panelMinimum[panel], _panelMaximum[panel], rect);
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
