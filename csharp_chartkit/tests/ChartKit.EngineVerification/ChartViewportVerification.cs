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
            maximumVisibleBars: 500,
            rightBlankBars: 12,
            maximumRightBlankBars: 120);

        ChartWindow latest = viewport.Resolve(totalBars);
        if (latest.StartIndex != 840 || latest.Count != 160 ||
            latest.RightBlankBars != 12 || latest.VisibleSlotCount != 172 ||
            !viewport.IsFollowingLatest)
            throw new InvalidOperationException("Initial viewport future-space resolution failed.");

        ChartWindow filledGap = viewport.Pan(5, totalBars);
        if (filledGap.EndExclusive != totalBars || filledGap.RightBlankBars != 7 ||
            filledGap.Count != 165 || viewport.RightOffsetBars != 0)
            throw new InvalidOperationException("Viewport gap-fill panning failed.");

        ChartWindow panned = viewport.Pan(100, totalBars);
        if (panned.StartIndex != 735 || panned.Count != 172 ||
            panned.RightBlankBars != 0 || viewport.RightOffsetBars != 93 ||
            viewport.IsFollowingLatest)
            throw new InvalidOperationException("Viewport historical panning failed.");

        double anchorBefore = panned.StartIndex + (panned.Count - 1d) * 0.5d;
        ChartWindow zoomed = viewport.Zoom(120, totalBars, 0.5f);
        double anchorAfter = zoomed.StartIndex + (zoomed.Count - 1d) * 0.5d;
        if (zoomed.Count >= panned.Count || Math.Abs(anchorAfter - anchorBefore) > 2.0d)
            throw new InvalidOperationException("Viewport anchored zoom failed.");

        ChartWindow oldest = viewport.Pan(int.MaxValue, totalBars);
        if (oldest.StartIndex != 0)
            throw new InvalidOperationException("Viewport oldest-bound clamp failed.");

        ChartWindow followed = viewport.FollowLatest(totalBars);
        if (!viewport.IsFollowingLatest || followed.EndExclusive != totalBars ||
            followed.RightBlankBars != 12)
            throw new InvalidOperationException("Viewport latest-follow failed.");

        viewport.Reset(totalBars);
        ChartWindow future = viewport.Pan(-20, totalBars);
        if (future.EndExclusive != totalBars || future.RightBlankBars != 32 ||
            future.Count != 140 || future.VisibleSlotCount != 172)
            throw new InvalidOperationException("Viewport additional future-space panning failed.");

        viewport.PanPricePixels(80f, 400f);
        if (Math.Abs(viewport.PricePanFraction - 0.2f) > 0.0001f)
            throw new InvalidOperationException("Viewport vertical price panning failed.");

        ChartWindow reset = viewport.Reset(totalBars);
        if (reset.Count != 160 || reset.EndExclusive != totalBars ||
            reset.RightBlankBars != 12 || viewport.PricePanFraction != 0f)
            throw new InvalidOperationException("Viewport reset failed.");

        Console.WriteLine("csharp_chart_viewport=PASS");
    }
}
