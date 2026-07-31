using ChartKit.CSharp.Charting;

namespace ChartKit.CSharp.EngineVerification;

internal static class ChartViewportVerification
{
    public static void Run()
    {
        const int totalBars = 1_000;
        var viewport = new ChartViewport(
            visibleBars: 160,
            minimumVisibleBars: 20,
            maximumVisibleBars: 500);

        ChartWindow latest = viewport.Resolve(totalBars);
        if (latest.StartIndex != 840 || latest.Count != 160 ||
            !viewport.IsFollowingLatest)
            throw new InvalidOperationException("Initial viewport resolution failed.");

        ChartWindow panned = viewport.Pan(100, totalBars);
        if (panned.StartIndex != 740 || viewport.RightOffsetBars != 100 ||
            viewport.IsFollowingLatest)
            throw new InvalidOperationException("Viewport panning failed.");

        double anchorBefore = panned.StartIndex + (panned.Count - 1d) * 0.5d;
        ChartWindow zoomed = viewport.Zoom(120, totalBars, 0.5f);
        double anchorAfter = zoomed.StartIndex + (zoomed.Count - 1d) * 0.5d;
        if (zoomed.Count >= panned.Count || Math.Abs(anchorAfter - anchorBefore) > 1.0d)
            throw new InvalidOperationException("Viewport anchored zoom failed.");

        ChartWindow oldest = viewport.Pan(int.MaxValue, totalBars);
        if (oldest.StartIndex != 0)
            throw new InvalidOperationException("Viewport oldest-bound clamp failed.");

        ChartWindow followed = viewport.FollowLatest(totalBars);
        if (!viewport.IsFollowingLatest || followed.EndExclusive != totalBars)
            throw new InvalidOperationException("Viewport latest-follow failed.");

        ChartWindow reset = viewport.Reset(totalBars);
        if (reset.Count != 160 || reset.EndExclusive != totalBars)
            throw new InvalidOperationException("Viewport reset failed.");

        Console.WriteLine("csharp_chart_viewport=PASS");
    }
}
