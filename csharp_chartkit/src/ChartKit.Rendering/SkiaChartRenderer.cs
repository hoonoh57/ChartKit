using System.Globalization;
using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;
using SkiaSharp;

namespace ChartKit.CSharp.Rendering;

public sealed partial class SkiaChartRenderer : IDisposable
{
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _dateBoundaryPaint;
    private readonly SKPaint _upFillPaint;
    private readonly SKPaint _downFillPaint;
    private readonly SKPaint _upVolumePaint;
    private readonly SKPaint _downVolumePaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint[] _seriesPaints;
    private readonly SKPath _gridPath = new();
    private readonly SKPath _dateBoundaryPath = new();
    private readonly SKPath _wickPath = new();
    private readonly SKPath _upBodyPath = new();
    private readonly SKPath _downBodyPath = new();
    private readonly SKPath _upVolumePath = new();
    private readonly SKPath _downVolumePath = new();
    private readonly SKPath _seriesPath = new();
    private readonly SKPath _histogramPath = new();
    private int _disposed;

    public SkiaChartRenderer()
    {
        _backgroundPaint = Fill(new SKColor(11, 15, 20));
        _gridPaint = Stroke(new SKColor(36, 46, 58), 1f);
        _dateBoundaryPaint = Stroke(new SKColor(70, 83, 98), 1.2f);
        _upFillPaint = Fill(new SKColor(239, 83, 80));
        _downFillPaint = Fill(new SKColor(66, 133, 244));
        _upVolumePaint = Fill(new SKColor(239, 83, 80, 150));
        _downVolumePaint = Fill(new SKColor(66, 133, 244, 150));
        _textPaint = Fill(new SKColor(210, 220, 230));
        _textPaint.TextSize = 13f;
        _textPaint.IsAntialias = true;

        SKColor[] colors =
        [
            new(255, 193, 7), new(0, 188, 212), new(156, 39, 176),
            new(76, 175, 80), new(255, 152, 0), new(233, 30, 99),
            new(3, 169, 244), new(205, 220, 57), new(121, 85, 72),
            new(0, 150, 136), new(255, 235, 59), new(103, 58, 183)
        ];
        _seriesPaints = new SKPaint[colors.Length];
        for (int index = 0; index < colors.Length; index++)
            _seriesPaints[index] = Stroke(colors[index], 1.4f);
    }

    public void Render(
        SKCanvas canvas,
        SymbolSnapshot snapshot,
        ChartFrame frame,
        ChartRenderOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(frame);
        ChartRenderOptions settings = options ?? ChartRenderOptions.Default;
        settings.Validate();

        ChartRectF bounds = frame.Bounds;
        canvas.Save();
        canvas.ClipRect(ToSkRect(bounds));
        canvas.DrawRect(ToSkRect(bounds), _backgroundPaint);
        if (snapshot.Candles.Length == 0 || frame.Window.IsEmpty)
        {
            canvas.Restore();
            return;
        }
        if (frame.Window.StartIndex < 0 || frame.Window.Count <= 0 ||
            frame.Window.EndExclusive > snapshot.Candles.Length)
        {
            canvas.Restore();
            throw new ArgumentOutOfRangeException(nameof(frame));
        }

        DrawGrid(canvas, frame);
        DrawCandles(canvas, snapshot, frame);
        DrawIndicatorSeries(canvas, snapshot, frame);
        if (settings.ShowAxes) DrawAxes(canvas, frame);
        if (settings.ShowText) DrawHeader(canvas, snapshot, frame);
        canvas.Restore();
    }

    private void DrawGrid(SKCanvas canvas, ChartFrame frame)
    {
        _gridPath.Rewind();
        _dateBoundaryPath.Rewind();

        for (int index = 0; index < frame.PriceTickCount; index++)
        {
            float y = frame.PriceTicks[index].Position;
            _gridPath.MoveTo(frame.MainPanel.Left, y);
            _gridPath.LineTo(frame.MainPanel.Right, y);
        }

        float chartBottom = frame.TimeAxis.Top;
        for (int index = 0; index < frame.TimeTickCount; index++)
        {
            TimeAxisTick tick = frame.TimeTicks[index];
            SKPath path = tick.IsDateBoundary ? _dateBoundaryPath : _gridPath;
            path.MoveTo(tick.Position, frame.MainPanel.Top);
            path.LineTo(tick.Position, chartBottom);
        }

        AddHorizontalBoundary(_gridPath, frame.VolumePanel);
        for (int panel = 1; panel <= ChartFrame.MaximumPanelIndex; panel++)
        {
            if (!frame.PanelVisible[panel]) continue;
            AddHorizontalBoundary(_gridPath, frame.PanelRects[panel]);
        }

        canvas.DrawPath(_gridPath, _gridPaint);
        canvas.DrawPath(_dateBoundaryPath, _dateBoundaryPaint);
    }

    private void DrawCandles(
        SKCanvas canvas,
        SymbolSnapshot snapshot,
        ChartFrame frame)
    {
        _wickPath.Rewind();
        _upBodyPath.Rewind();
        _downBodyPath.Rewind();
        _upVolumePath.Rewind();
        _downVolumePath.Rewind();

        for (int visibleIndex = 0;
             visibleIndex < frame.Window.Count;
             visibleIndex++)
        {
            Candle candle = snapshot.Candles[
                frame.Window.StartIndex + visibleIndex];
            float x = frame.X(visibleIndex);
            bool rising = candle.Close >= candle.Open;
            SKPath bodyPath = rising ? _upBodyPath : _downBodyPath;
            SKPath volumePath = rising ? _upVolumePath : _downVolumePath;

            _wickPath.MoveTo(x, frame.PriceY(candle.High));
            _wickPath.LineTo(x, frame.PriceY(candle.Low));

            float openY = frame.PriceY(candle.Open);
            float closeY = frame.PriceY(candle.Close);
            float top = Math.Min(openY, closeY);
            float bottom = Math.Max(openY, closeY);
            if (bottom - top < 1f) bottom = top + 1f;
            bodyPath.AddRect(new SKRect(
                x - frame.BodyWidth / 2f,
                top,
                x + frame.BodyWidth / 2f,
                bottom));

            float volumeTop = frame.VolumePanel.Bottom -
                              frame.VolumePanel.Height * candle.Volume /
                              Math.Max(1f, frame.VolumeMaximum);
            volumePath.AddRect(new SKRect(
                x - frame.BodyWidth / 2f,
                volumeTop,
                x + frame.BodyWidth / 2f,
                frame.VolumePanel.Bottom));
        }

        canvas.DrawPath(_wickPath, _gridPaint);
        canvas.DrawPath(_upBodyPath, _upFillPaint);
        canvas.DrawPath(_downBodyPath, _downFillPaint);
        canvas.DrawPath(_upVolumePath, _upVolumePaint);
        canvas.DrawPath(_downVolumePath, _downVolumePaint);
    }

    private void DrawAxes(SKCanvas canvas, ChartFrame frame)
    {
        for (int index = 0; index < frame.PriceTickCount; index++)
        {
            NumericAxisTick tick = frame.PriceTicks[index];
            string label = FormatPrice(tick.Value);
            canvas.DrawText(
                label,
                frame.MainPanel.Right + 6f,
                tick.Position + 4f,
                _textPaint);
        }

        for (int index = 0; index < frame.TimeTickCount; index++)
        {
            TimeAxisTick tick = frame.TimeTicks[index];
            string label = tick.IsDateBoundary
                ? tick.Time.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture)
                : tick.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
            float textWidth = _textPaint.MeasureText(label);
            float x = Math.Clamp(
                tick.Position - textWidth * 0.5f,
                frame.TimeAxis.Left,
                Math.Max(frame.TimeAxis.Left, frame.TimeAxis.Right - textWidth));
            canvas.DrawText(label, x, frame.TimeAxis.Top + 17f, _textPaint);
        }
    }

    private void DrawHeader(
        SKCanvas canvas,
        SymbolSnapshot snapshot,
        ChartFrame frame)
    {
        Candle last = snapshot.Candles[frame.Window.EndExclusive - 1];
        canvas.DrawText(
            snapshot.Symbol,
            frame.Bounds.Left + 10f,
            frame.Bounds.Top + 17f,
            _textPaint);
        string price = FormatPrice(last.Close);
        float width = _textPaint.MeasureText(price);
        canvas.DrawText(
            price,
            frame.Bounds.Right - width - 6f,
            frame.Bounds.Top + 17f,
            _textPaint);
    }

    private static void AddHorizontalBoundary(SKPath path, ChartRectF rect)
    {
        if (rect.IsEmpty) return;
        path.MoveTo(rect.Left, rect.Top);
        path.LineTo(rect.Right, rect.Top);
        path.MoveTo(rect.Left, rect.Bottom);
        path.LineTo(rect.Right, rect.Bottom);
    }

    private static string FormatPrice(float value)
    {
        float absolute = Math.Abs(value);
        string format = absolute >= 100f ? "N0" :
                        absolute >= 1f ? "N2" : "N4";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static SKRect ToSkRect(ChartRectF rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static SKPaint Fill(SKColor color) => new()
    {
        Color = color,
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _backgroundPaint.Dispose();
        _gridPaint.Dispose();
        _dateBoundaryPaint.Dispose();
        _upFillPaint.Dispose();
        _downFillPaint.Dispose();
        _upVolumePaint.Dispose();
        _downVolumePaint.Dispose();
        _textPaint.Dispose();
        foreach (SKPaint paint in _seriesPaints) paint.Dispose();
        _gridPath.Dispose();
        _dateBoundaryPath.Dispose();
        _wickPath.Dispose();
        _upBodyPath.Dispose();
        _downBodyPath.Dispose();
        _upVolumePath.Dispose();
        _downVolumePath.Dispose();
        _seriesPath.Dispose();
        _histogramPath.Dispose();
    }
}
