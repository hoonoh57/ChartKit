using System.Globalization;
using ChartKit.CSharp.Charting;
using SkiaSharp;

namespace ChartKit.CSharp.Rendering;

public sealed class ChartLegendRenderer : IDisposable
{
    private readonly SKPaint[] _paints = new SKPaint[ChartSeriesPalette.Count];
    private readonly SKFont _font = new(SKTypeface.Default, 12f);
    private readonly SKTextBlob?[] _blobs =
        new SKTextBlob?[ChartLegendFrame.MaximumEntryCount];
    private readonly string?[] _cachedNames =
        new string?[ChartLegendFrame.MaximumEntryCount];
    private readonly string?[] _cachedKeys =
        new string?[ChartLegendFrame.MaximumEntryCount];
    private readonly int[] _cachedValueBits =
        new int[ChartLegendFrame.MaximumEntryCount];
    private readonly bool[] _cachedHasValue =
        new bool[ChartLegendFrame.MaximumEntryCount];
    private readonly bool[] _cachedIndicatorStart =
        new bool[ChartLegendFrame.MaximumEntryCount];
    private readonly float[] _nextX = new float[ChartFrame.MaximumPanelIndex + 1];
    private readonly int[] _rows = new int[ChartFrame.MaximumPanelIndex + 1];
    private int _cachedCount;
    private int _disposed;

    public ChartLegendRenderer()
    {
        for (int index = 0; index < _paints.Length; index++)
        {
            _paints[index] = new SKPaint
            {
                Color = ChartSeriesPalette.GetColor(index),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
        }
    }

    public void Render(
        SKCanvas canvas,
        ChartFrame frame,
        ChartLegendFrame legend)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(legend);

        PrepareCaches(legend);
        Array.Clear(_nextX);
        Array.Clear(_rows);
        for (int panel = 0; panel <= ChartFrame.MaximumPanelIndex; panel++)
        {
            ChartRectF rect = ResolvePanel(frame, panel);
            _nextX[panel] = rect.Left + 6f;
        }

        for (int index = 0; index < legend.EntryCount; index++)
        {
            ChartLegendEntry entry = legend.Entries[index];
            if (entry.PanelIndex < 0 || entry.PanelIndex > ChartFrame.MaximumPanelIndex)
                continue;
            ChartRectF rect = ResolvePanel(frame, entry.PanelIndex);
            if (rect.IsEmpty || _blobs[index] is not SKTextBlob blob) continue;

            float width = blob.Bounds.Width + 14f;
            float x = _nextX[entry.PanelIndex];
            int row = _rows[entry.PanelIndex];
            if (x + width > rect.Right - 6f)
            {
                row++;
                _rows[entry.PanelIndex] = row;
                x = rect.Left + 6f;
            }

            float firstLine = entry.PanelIndex == 0 ? rect.Top + 34f : rect.Top + 14f;
            float y = firstLine + row * 15f;
            if (y > rect.Bottom - 2f) continue;

            canvas.DrawText(
                blob,
                x,
                y,
                _paints[entry.ColorIndex % _paints.Length]);
            _nextX[entry.PanelIndex] = x + width;
        }
    }

    private void PrepareCaches(ChartLegendFrame legend)
    {
        for (int index = 0; index < legend.EntryCount; index++)
        {
            ChartLegendEntry entry = legend.Entries[index];
            int valueBits = BitConverter.SingleToInt32Bits(entry.Value);
            if (_blobs[index] is not null &&
                ReferenceEquals(_cachedNames[index], entry.IndicatorName) &&
                ReferenceEquals(_cachedKeys[index], entry.ValueKey) &&
                _cachedValueBits[index] == valueBits &&
                _cachedHasValue[index] == entry.HasValue &&
                _cachedIndicatorStart[index] == entry.IsIndicatorStart)
                continue;

            string value = entry.HasValue
                ? FormatNumber(entry.Value)
                : "--";
            string label = entry.IsIndicatorStart
                ? $"{entry.IndicatorName} {entry.ValueKey} {value}"
                : $"{entry.ValueKey} {value}";
            _blobs[index]?.Dispose();
            _blobs[index] = SKTextBlob.Create(label, _font) ??
                            throw new InvalidOperationException("Legend label shaping failed.");
            _cachedNames[index] = entry.IndicatorName;
            _cachedKeys[index] = entry.ValueKey;
            _cachedValueBits[index] = valueBits;
            _cachedHasValue[index] = entry.HasValue;
            _cachedIndicatorStart[index] = entry.IsIndicatorStart;
        }

        for (int index = legend.EntryCount; index < _cachedCount; index++)
        {
            _blobs[index]?.Dispose();
            _blobs[index] = null;
            _cachedNames[index] = null;
            _cachedKeys[index] = null;
            _cachedValueBits[index] = 0;
            _cachedHasValue[index] = false;
            _cachedIndicatorStart[index] = false;
        }
        _cachedCount = legend.EntryCount;
    }

    private static ChartRectF ResolvePanel(ChartFrame frame, int panelIndex) =>
        panelIndex == 0
            ? frame.MainPanel
            : frame.PanelVisible[panelIndex]
                ? frame.PanelRects[panelIndex]
                : ChartRectF.Empty;

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
        for (int index = 0; index < _blobs.Length; index++)
            _blobs[index]?.Dispose();
        foreach (SKPaint paint in _paints) paint.Dispose();
        _font.Dispose();
    }
}
