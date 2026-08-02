using System.Globalization;
using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Scene;
using SkiaSharp;

namespace ChartKit.CSharp.Rendering;

public readonly record struct ChartRenderPlanRenderResult(
    int RenderedPrimitives,
    int SkippedPrimitives,
    int RenderedPoints);

public sealed class SkiaChartRenderPlanRenderer : IDisposable
{
    private static readonly SKColor AccentColor = new(255, 193, 7);
    private readonly SKPaint _strokePaint = new()
    {
        Style = SKPaintStyle.Stroke,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };
    private readonly SKPaint _fillPaint = new()
    {
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };
    private readonly SKPath _path = new();
    private int _disposed;

    public ChartRenderPlanRenderResult Render(
        SKCanvas canvas,
        ChartRenderPlan plan,
        ChartFrame frame)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(frame);

        if (plan.Primitives.Count == 0 || frame.Window.IsEmpty)
            return default;

        int renderedPrimitives = 0;
        int skippedPrimitives = 0;
        int renderedPoints = 0;

        canvas.Save();
        canvas.ClipRect(ToSkRect(frame.Bounds));
        for (int index = 0; index < plan.Primitives.Count; index++)
        {
            RenderPrimitivePlan primitive = plan.Primitives[index];
            if (!TryResolvePanel(primitive.PanelId, frame, out PanelBinding panel))
            {
                skippedPrimitives++;
                continue;
            }

            ConfigurePaints(primitive.Style);
            canvas.Save();
            canvas.ClipRect(ToSkRect(panel.Rect));
            bool rendered = DrawPrimitive(
                canvas,
                primitive,
                frame,
                panel,
                out int pointCount);
            canvas.Restore();

            if (rendered)
            {
                renderedPrimitives++;
                renderedPoints += pointCount;
            }
            else
            {
                skippedPrimitives++;
            }
        }
        canvas.Restore();

        return new ChartRenderPlanRenderResult(
            renderedPrimitives,
            skippedPrimitives,
            renderedPoints);
    }

    private bool DrawPrimitive(
        SKCanvas canvas,
        RenderPrimitivePlan primitive,
        ChartFrame frame,
        PanelBinding panel,
        out int pointCount)
    {
        return primitive.RenderKind switch
        {
            RenderPrimitiveKind.Polyline =>
                DrawPolyline(canvas, primitive, frame, panel, out pointCount),
            RenderPrimitiveKind.Line =>
                DrawLines(canvas, primitive, frame, panel, out pointCount),
            RenderPrimitiveKind.Marker =>
                DrawMarkers(canvas, primitive, frame, panel, out pointCount),
            RenderPrimitiveKind.Rectangle =>
                DrawRectangles(canvas, primitive, frame, panel, out pointCount),
            RenderPrimitiveKind.Histogram =>
                DrawHistogram(canvas, primitive, frame, panel, out pointCount),
            RenderPrimitiveKind.FillArea =>
                DrawFillArea(canvas, primitive, frame, panel, out pointCount),
            _ => Unsupported(out pointCount)
        };
    }

    private bool DrawPolyline(
        SKCanvas canvas,
        RenderPrimitivePlan primitive,
        ChartFrame frame,
        PanelBinding panel,
        out int pointCount)
    {
        _path.Rewind();
        bool active = false;
        int mapped = 0;
        int segments = 0;

        for (int index = 0; index < primitive.RenderPoints.Count; index++)
        {
            RenderPoint point = primitive.RenderPoints[index];
            if (!TryMapPoint(point, frame, panel, out float x, out float y))
            {
                active = false;
                continue;
            }

            mapped++;
            if (active)
            {
                _path.LineTo(x, y);
                segments++;
            }
            else
            {
                _path.MoveTo(x, y);
                active = true;
            }
        }

        pointCount = mapped;
        if (segments == 0) return false;
        canvas.DrawPath(_path, _strokePaint);
        return true;
    }

    private bool DrawLines(
        SKCanvas canvas,
        RenderPrimitivePlan primitive,
        ChartFrame frame,
        PanelBinding panel,
        out int pointCount)
    {
        _path.Rewind();
        int mapped = 0;
        int segments = 0;
        for (int index = 0; index + 1 < primitive.RenderPoints.Count; index += 2)
        {
            RenderPoint first = primitive.RenderPoints[index];
            RenderPoint second = primitive.RenderPoints[index + 1];
            if (!TryMapPoint(first, frame, panel, out float x1, out float y1) ||
                !TryMapPoint(second, frame, panel, out float x2, out float y2))
            {
                continue;
            }

            _path.MoveTo(x1, y1);
            _path.LineTo(x2, y2);
            mapped += 2;
            segments++;
        }

        pointCount = mapped;
        if (segments == 0) return false;
        canvas.DrawPath(_path, _strokePaint);
        return true;
    }

    private bool DrawMarkers(
        SKCanvas canvas,
        RenderPrimitivePlan primitive,
        ChartFrame frame,
        PanelBinding panel,
        out int pointCount)
    {
        _path.Rewind();
        float radius = Math.Max(2f, primitive.Style.StrokeWidth * 1.75f);
        int mapped = 0;
        for (int index = 0; index < primitive.RenderPoints.Count; index++)
        {
            if (!TryMapPoint(
                    primitive.RenderPoints[index],
                    frame,
                    panel,
                    out float x,
                    out float y))
            {
                continue;
            }
            _path.AddCircle(x, y, radius);
            mapped++;
        }

        pointCount = mapped;
        if (mapped == 0) return false;
        canvas.DrawPath(_path, _fillPaint);
        return true;
    }

    private bool DrawRectangles(
        SKCanvas canvas,
        RenderPrimitivePlan primitive,
        ChartFrame frame,
        PanelBinding panel,
        out int pointCount)
    {
        _path.Rewind();
        int mapped = 0;
        int rectangles = 0;
        for (int index = 0; index + 1 < primitive.RenderPoints.Count; index += 2)
        {
            if (!TryMapPoint(
                    primitive.RenderPoints[index],
                    frame,
                    panel,
                    out float x1,
                    out float y1) ||
                !TryMapPoint(
                    primitive.RenderPoints[index + 1],
                    frame,
                    panel,
                    out float x2,
                    out float y2))
            {
                continue;
            }

            _path.AddRect(new SKRect(
                Math.Min(x1, x2),
                Math.Min(y1, y2),
                Math.Max(x1, x2),
                Math.Max(y1, y2)));
            mapped += 2;
            rectangles++;
        }

        pointCount = mapped;
        if (rectangles == 0) return false;
        if (primitive.Style.Fill.Length > 0)
            canvas.DrawPath(_path, _fillPaint);
        canvas.DrawPath(_path, _strokePaint);
        return true;
    }

    private bool DrawHistogram(
        SKCanvas canvas,
        RenderPrimitivePlan primitive,
        ChartFrame frame,
        PanelBinding panel,
        out int pointCount)
    {
        _path.Rewind();
        float baseline = ResolveBaseline(frame, panel);
        float halfWidth = Math.Max(0.5f, frame.BodyWidth * 0.36f);
        int mapped = 0;

        for (int index = 0; index < primitive.RenderPoints.Count; index++)
        {
            if (!TryMapPoint(
                    primitive.RenderPoints[index],
                    frame,
                    panel,
                    out float x,
                    out float y))
            {
                continue;
            }

            _path.AddRect(new SKRect(
                x - halfWidth,
                Math.Min(y, baseline),
                x + halfWidth,
                Math.Max(y, baseline)));
            mapped++;
        }

        pointCount = mapped;
        if (mapped == 0) return false;
        canvas.DrawPath(_path, _fillPaint);
        return true;
    }

    private bool DrawFillArea(
        SKCanvas canvas,
        RenderPrimitivePlan primitive,
        ChartFrame frame,
        PanelBinding panel,
        out int pointCount)
    {
        _path.Rewind();
        int mapped = 0;
        for (int index = 0; index < primitive.RenderPoints.Count; index++)
        {
            if (!TryMapPoint(
                    primitive.RenderPoints[index],
                    frame,
                    panel,
                    out float x,
                    out float y))
            {
                continue;
            }

            if (mapped == 0) _path.MoveTo(x, y);
            else _path.LineTo(x, y);
            mapped++;
        }

        pointCount = mapped;
        if (mapped < 3) return false;
        _path.Close();
        canvas.DrawPath(_path, _fillPaint);
        if (primitive.Style.StrokeWidth > 0f)
            canvas.DrawPath(_path, _strokePaint);
        return true;
    }

    private static bool Unsupported(out int pointCount)
    {
        pointCount = 0;
        return false;
    }

    private static bool TryMapPoint(
        RenderPoint point,
        ChartFrame frame,
        PanelBinding panel,
        out float x,
        out float y)
    {
        if (point.X < frame.Window.StartIndex ||
            point.X >= frame.Window.EndExclusive ||
            !double.IsFinite(point.Y) ||
            point.Y < -float.MaxValue ||
            point.Y > float.MaxValue)
        {
            x = default;
            y = default;
            return false;
        }

        int visibleIndex = checked((int)(point.X - frame.Window.StartIndex));
        x = frame.X(visibleIndex);
        float value = (float)point.Y;
        y = panel.Kind switch
        {
            PanelCoordinateKind.Price => frame.PriceY(value),
            PanelCoordinateKind.Volume => ChartFrame.MapY(
                value,
                new NumericRange(0f, Math.Max(1f, frame.VolumeMaximum)),
                panel.Rect),
            PanelCoordinateKind.Indicator => frame.PanelY(panel.PanelIndex, value),
            _ => panel.Rect.MidY
        };
        return float.IsFinite(x) && float.IsFinite(y);
    }

    private static float ResolveBaseline(ChartFrame frame, PanelBinding panel)
    {
        float baseline = panel.Kind switch
        {
            PanelCoordinateKind.Price => frame.PriceY(0f),
            PanelCoordinateKind.Volume => panel.Rect.Bottom,
            PanelCoordinateKind.Indicator => frame.PanelY(panel.PanelIndex, 0f),
            _ => panel.Rect.Bottom
        };
        return Math.Clamp(baseline, panel.Rect.Top, panel.Rect.Bottom);
    }

    private static bool TryResolvePanel(
        string panelId,
        ChartFrame frame,
        out PanelBinding panel)
    {
        if (string.Equals(panelId, "price.main", StringComparison.Ordinal))
        {
            panel = new PanelBinding(
                frame.MainPanel,
                PanelCoordinateKind.Price,
                0);
            return !panel.Rect.IsEmpty;
        }

        if (string.Equals(panelId, "volume.main", StringComparison.Ordinal))
        {
            panel = new PanelBinding(
                frame.VolumePanel,
                PanelCoordinateKind.Volume,
                0);
            return !panel.Rect.IsEmpty;
        }

        if (TryReadPanelIndex(panelId, "panel.", out int panelIndex) ||
            TryReadPanelIndex(panelId, "indicator.", out panelIndex))
        {
            if (panelIndex > 0 &&
                panelIndex <= ChartFrame.MaximumPanelIndex &&
                frame.PanelVisible[panelIndex] &&
                !frame.PanelRects[panelIndex].IsEmpty)
            {
                panel = new PanelBinding(
                    frame.PanelRects[panelIndex],
                    PanelCoordinateKind.Indicator,
                    panelIndex);
                return true;
            }
        }

        panel = default;
        return false;
    }

    private static bool TryReadPanelIndex(
        string value,
        string prefix,
        out int panelIndex)
    {
        panelIndex = 0;
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        ReadOnlySpan<char> suffix = value.AsSpan(prefix.Length);
        return int.TryParse(
            suffix,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out panelIndex);
    }

    private void ConfigurePaints(RenderPrimitiveStyle style)
    {
        SKColor stroke = ApplyOpacity(ParseColor(style.Stroke), style.Opacity);
        SKColor fill = ApplyOpacity(
            style.Fill.Length == 0 ? stroke : ParseColor(style.Fill),
            style.Opacity);
        _strokePaint.Color = stroke;
        _strokePaint.StrokeWidth = style.StrokeWidth;
        _fillPaint.Color = fill;
    }

    private static SKColor ParseColor(string value)
    {
        if (TryParseHexColor(value, out SKColor color)) return color;
        if (string.Equals(value, "accent", StringComparison.OrdinalIgnoreCase))
            return AccentColor;
        if (string.Equals(value, "positive", StringComparison.OrdinalIgnoreCase))
            return new SKColor(239, 83, 80);
        if (string.Equals(value, "negative", StringComparison.OrdinalIgnoreCase))
            return new SKColor(66, 133, 244);
        if (string.Equals(value, "neutral", StringComparison.OrdinalIgnoreCase))
            return new SKColor(180, 190, 200);
        if (string.Equals(value, "warning", StringComparison.OrdinalIgnoreCase))
            return new SKColor(255, 152, 0);
        if (string.Equals(value, "white", StringComparison.OrdinalIgnoreCase))
            return SKColors.White;
        if (string.Equals(value, "black", StringComparison.OrdinalIgnoreCase))
            return SKColors.Black;
        return AccentColor;
    }

    private static bool TryParseHexColor(string value, out SKColor color)
    {
        color = default;
        if (value.Length == 7 && value[0] == '#')
        {
            if (TryReadHexByte(value.AsSpan(1, 2), out byte red) &&
                TryReadHexByte(value.AsSpan(3, 2), out byte green) &&
                TryReadHexByte(value.AsSpan(5, 2), out byte blue))
            {
                color = new SKColor(red, green, blue);
                return true;
            }
            return false;
        }

        if (value.Length == 9 && value[0] == '#')
        {
            if (TryReadHexByte(value.AsSpan(1, 2), out byte alpha) &&
                TryReadHexByte(value.AsSpan(3, 2), out byte red) &&
                TryReadHexByte(value.AsSpan(5, 2), out byte green) &&
                TryReadHexByte(value.AsSpan(7, 2), out byte blue))
            {
                color = new SKColor(red, green, blue, alpha);
                return true;
            }
        }
        return false;
    }

    private static bool TryReadHexByte(ReadOnlySpan<char> value, out byte result) =>
        byte.TryParse(
            value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out result);

    private static SKColor ApplyOpacity(SKColor color, float opacity)
    {
        byte alpha = (byte)Math.Clamp(
            (int)MathF.Round(color.Alpha * opacity),
            byte.MinValue,
            byte.MaxValue);
        return color.WithAlpha(alpha);
    }

    private static SKRect ToSkRect(ChartRectF rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _strokePaint.Dispose();
        _fillPaint.Dispose();
        _path.Dispose();
    }

    private enum PanelCoordinateKind
    {
        Price,
        Volume,
        Indicator
    }

    private readonly record struct PanelBinding(
        ChartRectF Rect,
        PanelCoordinateKind Kind,
        int PanelIndex);
}
