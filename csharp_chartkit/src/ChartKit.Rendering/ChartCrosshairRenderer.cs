using System.Globalization;
using ChartKit.CSharp.Charting;
using ChartKit.CSharp.Contracts;
using SkiaSharp;

namespace ChartKit.CSharp.Rendering;

public sealed class ChartCrosshairRenderer : IDisposable
{
    private readonly SKPaint _linePaint = new()
    {
        Color = new SKColor(180, 190, 200, 190),
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1f,
        IsAntialias = false,
        PathEffect = SKPathEffect.CreateDash(new[] { 4f, 4f }, 0f)
    };
    private readonly SKPaint _labelBackgroundPaint = new()
    {
        Color = new SKColor(36, 46, 58, 235),
        Style = SKPaintStyle.Fill,
        IsAntialias = false
    };
    private readonly SKPaint _labelTextPaint = new()
    {
        Color = new SKColor(235, 240, 245),
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };
    private readonly SKFont _font = new(SKTypeface.Default, 13f);
    private readonly SKPath _path = new();
    private SKTextBlob? _priceBlob;
    private SKTextBlob? _timeBlob;
    private SKTextBlob? _ohlcvBlob;
    private int _lastCandleIndex = -1;
    private int _lastPriceBits;
    private int _disposed;

    public void Render(
        SKCanvas canvas,
        ChartFrame frame,
        ChartCursorSnapshot cursor)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(frame);
        if (!cursor.IsVisible || frame.Window.IsEmpty) return;

        UpdateLabels(cursor);
        _path.Rewind();
        _path.MoveTo(cursor.X, frame.MainPanel.Top);
        _path.LineTo(cursor.X, frame.TimeAxis.Top);
        _path.MoveTo(frame.MainPanel.Left, cursor.Y);
        _path.LineTo(frame.MainPanel.Right, cursor.Y);
        canvas.DrawPath(_path, _linePaint);

        DrawPriceLabel(canvas, frame, cursor.Y);
        DrawTimeLabel(canvas, frame, cursor.X);
        canvas.DrawText(
            _ohlcvBlob!,
            frame.MainPanel.Left + 6f,
            frame.MainPanel.Top + 16f,
            _labelTextPaint);
    }

    private void UpdateLabels(ChartCursorSnapshot cursor)
    {
        int priceBits = BitConverter.SingleToInt32Bits(cursor.Price);
        if (_lastCandleIndex == cursor.CandleIndex && _lastPriceBits == priceBits) return;

        _lastCandleIndex = cursor.CandleIndex;
        _lastPriceBits = priceBits;
        string priceLabel = FormatNumber(cursor.Price);
        string timeLabel = cursor.Candle.OpenTime.ToString(
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture);
        Candle candle = cursor.Candle;
        string ohlcvLabel =
            $"O {FormatNumber(candle.Open)}  H {FormatNumber(candle.High)}  " +
            $"L {FormatNumber(candle.Low)}  C {FormatNumber(candle.Close)}  " +
            $"V {candle.Volume.ToString("N0", CultureInfo.InvariantCulture)}";

        _priceBlob?.Dispose();
        _timeBlob?.Dispose();
        _ohlcvBlob?.Dispose();
        _priceBlob = SKTextBlob.Create(priceLabel, _font) ??
                     throw new InvalidOperationException("Price label shaping failed.");
        _timeBlob = SKTextBlob.Create(timeLabel, _font) ??
                    throw new InvalidOperationException("Time label shaping failed.");
        _ohlcvBlob = SKTextBlob.Create(ohlcvLabel, _font) ??
                     throw new InvalidOperationException("OHLCV label shaping failed.");
    }

    private void DrawPriceLabel(SKCanvas canvas, ChartFrame frame, float y)
    {
        float width = _priceBlob!.Bounds.Width + 10f;
        float height = 20f;
        float top = Math.Clamp(
            y - height * 0.5f,
            frame.MainPanel.Top,
            frame.MainPanel.Bottom - height);
        var rect = new SKRect(
            frame.MainPanel.Right + 2f,
            top,
            Math.Min(frame.Bounds.Right, frame.MainPanel.Right + 2f + width),
            top + height);
        canvas.DrawRect(rect, _labelBackgroundPaint);
        canvas.DrawText(
            _priceBlob,
            rect.Left + 5f,
            rect.Top + 15f,
            _labelTextPaint);
    }

    private void DrawTimeLabel(SKCanvas canvas, ChartFrame frame, float x)
    {
        float width = _timeBlob!.Bounds.Width + 10f;
        float height = 20f;
        float left = Math.Clamp(
            x - width * 0.5f,
            frame.TimeAxis.Left,
            Math.Max(frame.TimeAxis.Left, frame.TimeAxis.Right - width));
        var rect = new SKRect(
            left,
            frame.TimeAxis.Top,
            left + width,
            Math.Min(frame.Bounds.Bottom, frame.TimeAxis.Top + height));
        canvas.DrawRect(rect, _labelBackgroundPaint);
        canvas.DrawText(
            _timeBlob,
            rect.Left + 5f,
            rect.Top + 15f,
            _labelTextPaint);
    }

    private static string FormatNumber(float value)
    {
        float absolute = Math.Abs(value);
        string format = absolute >= 100f ? "N0" :
                        absolute >= 1f ? "N2" : "N4";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _priceBlob?.Dispose();
        _timeBlob?.Dispose();
        _ohlcvBlob?.Dispose();
        _font.Dispose();
        _linePaint.PathEffect?.Dispose();
        _linePaint.Dispose();
        _labelBackgroundPaint.Dispose();
        _labelTextPaint.Dispose();
        _path.Dispose();
    }
}
