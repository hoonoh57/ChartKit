using SkiaSharp;

namespace ChartKit.CSharp.Rendering;

public static class ChartSeriesPalette
{
    private static readonly SKColor[] Colors =
    [
        new(255, 193, 7), new(0, 188, 212), new(156, 39, 176),
        new(76, 175, 80), new(255, 152, 0), new(233, 30, 99),
        new(3, 169, 244), new(205, 220, 57), new(121, 85, 72),
        new(0, 150, 136), new(255, 235, 59), new(103, 58, 183)
    ];

    public static int Count => Colors.Length;

    public static SKColor GetColor(int colorIndex)
    {
        int normalized = colorIndex % Colors.Length;
        if (normalized < 0) normalized += Colors.Length;
        return Colors[normalized];
    }
}
