using System.Globalization;
using ChartKit.CSharp.Contracts;
using SkiaSharp;

namespace ChartKit.CSharp.Rendering;

public sealed partial class SkiaChartRenderer : IDisposable
{
    private const int MaximumPanelIndex = 7;
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _gridPaint;
    private readonly SKPaint _upPaint;
    private readonly SKPaint _downPaint;
    private readonly SKPaint _upFillPaint;
    private readonly SKPaint _downFillPaint;
    private readonly SKPaint _upVolumePaint;
    private readonly SKPaint _downVolumePaint;
    private readonly SKPaint _textPaint;
    private readonly SKPaint[] _seriesPaints;
    private readonly SKPath _gridPath = new();
    private readonly SKPath _wickPath = new();
    private readonly SKPath _upBodyPath = new();
    private readonly SKPath _downBodyPath = new();
    private readonly SKPath _upVolumePath = new();
    private readonly SKPath _downVolumePath = new();
    private readonly SKPath _seriesPath = new();
    private readonly SKPath _histogramPath = new();
    private readonly SKRect[] _panelRects = new SKRect[MaximumPanelIndex + 1];
    private readonly bool[] _panelVisible = new bool[MaximumPanelIndex + 1];
    private readonly float[] _panelMinimum = new float[MaximumPanelIndex + 1];
    private readonly float[] _panelMaximum = new float[MaximumPanelIndex + 1];

    private SKRect _mainRect;
    private SKRect _volumeRect;
    private int _startIndex;
    private int _visibleCount;
    private float _priceMinimum;
    private float _priceMaximum;
    private long _volumeMaximum;
    private float _step;
    private float _bodyWidth;
    private int _disposed;

    public SkiaChartRenderer()
    {
        _backgroundPaint = Fill(new SKColor(11, 15, 20));
        _gridPaint = Stroke(new SKColor(36, 46, 58), 1f);
        _upPaint = Stroke(new SKColor(239, 83, 80), 1f);
        _downPaint = Stroke(new SKColor(66, 133, 244), 1f);
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
        SKRect bounds,
        SymbolSnapshot snapshot,
        ChartRenderOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(snapshot);
        ChartRenderOptions settings = options ?? ChartRenderOptions.Default;
        settings.Validate();

        canvas.Save();
        canvas.ClipRect(bounds);
        canvas.DrawRect(bounds, _backgroundPaint);
        if (snapshot.Candles.Length == 0)
        {
            canvas.Restore();
            return;
        }

        PrepareLayout(bounds, snapshot, settings);
        DrawGrid(canvas);
        DrawCandles(canvas, snapshot);
        DrawIndicatorSeries(canvas, snapshot);
        if (settings.ShowText) DrawHeader(canvas, bounds, snapshot);
        canvas.Restore();
    }

    private void PrepareLayout(
        SKRect bounds,
        SymbolSnapshot snapshot,
        ChartRenderOptions options)
    {
        Array.Clear(_panelVisible);
        int panelCount = 0;
        foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
        {
            int panel = series.Descriptor.PanelIndex;
            if (panel <= 0 || panel > MaximumPanelIndex || _panelVisible[panel]) continue;
            _panelVisible[panel] = true;
            panelCount++;
        }

        float left = bounds.Left + options.LeftPadding;
        float right = Math.Max(left + 1f, bounds.Right - options.RightPadding);
        float top = bounds.Top + options.TopPadding;
        float bottom = Math.Max(top + 1f, bounds.Bottom - options.BottomPadding);
        float totalHeight = bottom - top;
        float mainHeight = totalHeight * options.MainPanelRatio;
        float volumeHeight = totalHeight * options.VolumePanelRatio;
        _mainRect = new SKRect(left, top, right, top + mainHeight);
        _volumeRect = new SKRect(left, _mainRect.Bottom, right, _mainRect.Bottom + volumeHeight);

        float remaining = Math.Max(0f, bottom - _volumeRect.Bottom);
        float panelHeight = panelCount == 0 ? 0f : remaining / panelCount;
        float panelTop = _volumeRect.Bottom;
        for (int panel = 1; panel <= MaximumPanelIndex; panel++)
        {
            if (!_panelVisible[panel])
            {
                _panelRects[panel] = SKRect.Empty;
                continue;
            }
            _panelRects[panel] = new SKRect(left, panelTop, right, panelTop + panelHeight);
            panelTop += panelHeight;
        }

        _visibleCount = Math.Min(options.VisibleBars, snapshot.Candles.Length);
        _startIndex = snapshot.Candles.Length - _visibleCount;
        _step = _mainRect.Width / Math.Max(1, _visibleCount);
        _bodyWidth = Math.Max(1f, _step * 0.72f);
        CalculateRanges(snapshot);
    }

    private void CalculateRanges(SymbolSnapshot snapshot)
    {
        _priceMinimum = float.MaxValue;
        _priceMaximum = float.MinValue;
        _volumeMaximum = 1;
        for (int index = _startIndex; index < snapshot.Candles.Length; index++)
        {
            Candle candle = snapshot.Candles[index];
            _priceMinimum = Math.Min(_priceMinimum, candle.Low);
            _priceMaximum = Math.Max(_priceMaximum, candle.High);
            _volumeMaximum = Math.Max(_volumeMaximum, candle.Volume);
        }
        if (_priceMinimum == float.MaxValue || _priceMaximum == float.MinValue)
        {
            _priceMinimum = 0f;
            _priceMaximum = 1f;
        }
        float priceMargin = Math.Max(0.01f, (_priceMaximum - _priceMinimum) * 0.05f);
        _priceMinimum -= priceMargin;
        _priceMaximum += priceMargin;

        for (int panel = 0; panel <= MaximumPanelIndex; panel++)
        {
            _panelMinimum[panel] = float.MaxValue;
            _panelMaximum[panel] = float.MinValue;
        }

        foreach (IndicatorSeriesSnapshot series in snapshot.Indicators)
        {
            int panel = series.Descriptor.PanelIndex;
            if (panel <= 0 || panel > MaximumPanelIndex) continue;
            int pointStart = Math.Max(0, series.Points.Length - _visibleCount);
            for (int pointIndex = pointStart; pointIndex < series.Points.Length; pointIndex++)
            {
                IndicatorPoint point = series.Points[pointIndex];
                for (int valueIndex = 0; valueIndex < series.Descriptor.ValueCount; valueIndex++)
                {
                    if (series.Descriptor.Kinds[valueIndex] == SeriesKind.Meta) continue;
                    float value = point.GetValue(valueIndex);
                    if (!float.IsFinite(value)) continue;
                    _panelMinimum[panel] = Math.Min(_panelMinimum[panel], value);
                    _panelMaximum[panel] = Math.Max(_panelMaximum[panel], value);
                }
            }
        }

        for (int panel = 1; panel <= MaximumPanelIndex; panel++)
        {
            if (!_panelVisible[panel]) continue;
            if (_panelMinimum[panel] == float.MaxValue || _panelMaximum[panel] == float.MinValue)
            {
                _panelMinimum[panel] = 0f;
                _panelMaximum[panel] = 1f;
            }
            float range = _panelMaximum[panel] - _panelMinimum[panel];
            float margin = Math.Max(0.001f, range * 0.08f);
            _panelMinimum[panel] -= margin;
            _panelMaximum[panel] += margin;
        }
    }

    private void DrawGrid(SKCanvas canvas)
    {
        _gridPath.Rewind();
        for (int row = 0; row <= 5; row++)
        {
            float y = _mainRect.Top + _mainRect.Height * row / 5f;
            _gridPath.MoveTo(_mainRect.Left, y);
            _gridPath.LineTo(_mainRect.Right, y);
        }
        for (int column = 0; column <= 8; column++)
        {
            float x = _mainRect.Left + _mainRect.Width * column / 8f;
            _gridPath.MoveTo(x, _mainRect.Top);
            _gridPath.LineTo(x, _volumeRect.Bottom);
        }
        _gridPath.MoveTo(_volumeRect.Left, _volumeRect.Top);
        _gridPath.LineTo(_volumeRect.Right, _volumeRect.Top);
        _gridPath.MoveTo(_volumeRect.Left, _volumeRect.Bottom);
        _gridPath.LineTo(_volumeRect.Right, _volumeRect.Bottom);
        for (int panel = 1; panel <= MaximumPanelIndex; panel++)
        {
            if (!_panelVisible[panel]) continue;
            SKRect rect = _panelRects[panel];
            _gridPath.MoveTo(rect.Left, rect.Top);
            _gridPath.LineTo(rect.Right, rect.Top);
            _gridPath.MoveTo(rect.Left, rect.Bottom);
            _gridPath.LineTo(rect.Right, rect.Bottom);
        }
        canvas.DrawPath(_gridPath, _gridPaint);
    }

    private void DrawCandles(SKCanvas canvas, SymbolSnapshot snapshot)
    {
        _wickPath.Rewind();
        _upBodyPath.Rewind();
        _downBodyPath.Rewind();
        _upVolumePath.Rewind();
        _downVolumePath.Rewind();

        for (int index = 0; index < _visibleCount; index++)
        {
            Candle candle = snapshot.Candles[_startIndex + index];
            float x = X(index);
            bool rising = candle.Close >= candle.Open;
            SKPath bodyPath = rising ? _upBodyPath : _downBodyPath;
            SKPath volumePath = rising ? _upVolumePath : _downVolumePath;

            _wickPath.MoveTo(x, PriceY(candle.High));
            _wickPath.LineTo(x, PriceY(candle.Low));

            float openY = PriceY(candle.Open);
            float closeY = PriceY(candle.Close);
            float top = Math.Min(openY, closeY);
            float bottom = Math.Max(openY, closeY);
            if (bottom - top < 1f) bottom = top + 1f;
            bodyPath.AddRect(new SKRect(
                x - _bodyWidth / 2f,
                top,
                x + _bodyWidth / 2f,
                bottom));

            float volumeTop = _volumeRect.Bottom -
                              _volumeRect.Height * candle.Volume / Math.Max(1f, _volumeMaximum);
            volumePath.AddRect(new SKRect(
                x - _bodyWidth / 2f,
                volumeTop,
                x + _bodyWidth / 2f,
                _volumeRect.Bottom));
        }

        canvas.DrawPath(_wickPath, _gridPaint);
        canvas.DrawPath(_upBodyPath, _upFillPaint);
        canvas.DrawPath(_downBodyPath, _downFillPaint);
        canvas.DrawPath(_upVolumePath, _upVolumePaint);
        canvas.DrawPath(_downVolumePath, _downVolumePaint);
    }

    private void DrawHeader(SKCanvas canvas, SKRect bounds, SymbolSnapshot snapshot)
    {
        Candle last = snapshot.Candles[^1];
        canvas.DrawText(snapshot.Symbol, bounds.Left + 10f, bounds.Top + 17f, _textPaint);
        string price = last.Close.ToString("N2", CultureInfo.InvariantCulture);
        canvas.DrawText(price, bounds.Right - 70f, bounds.Top + 17f, _textPaint);
    }

    private float X(int visibleIndex) => _mainRect.Left + (visibleIndex + 0.5f) * _step;

    private float PriceY(float value) => MapY(value, _priceMinimum, _priceMaximum, _mainRect);

    private static float MapY(float value, float minimum, float maximum, SKRect rect)
    {
        if (!float.IsFinite(value) || maximum <= minimum) return rect.MidY;
        float ratio = (value - minimum) / (maximum - minimum);
        return rect.Bottom - ratio * rect.Height;
    }

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
        _upPaint.Dispose();
        _downPaint.Dispose();
        _upFillPaint.Dispose();
        _downFillPaint.Dispose();
        _upVolumePaint.Dispose();
        _downVolumePaint.Dispose();
        _textPaint.Dispose();
        foreach (SKPaint paint in _seriesPaints) paint.Dispose();
        _gridPath.Dispose();
        _wickPath.Dispose();
        _upBodyPath.Dispose();
        _downBodyPath.Dispose();
        _upVolumePath.Dispose();
        _downVolumePath.Dispose();
        _seriesPath.Dispose();
        _histogramPath.Dispose();
    }
}
