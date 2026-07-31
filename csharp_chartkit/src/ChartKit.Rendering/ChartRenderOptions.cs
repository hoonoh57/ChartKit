namespace ChartKit.CSharp.Rendering;

public sealed record ChartRenderOptions(
    int VisibleBars = 160,
    float MainPanelRatio = 0.56f,
    float VolumePanelRatio = 0.10f,
    float LeftPadding = 10f,
    float RightPadding = 76f,
    float TopPadding = 24f,
    float BottomPadding = 10f,
    bool ShowText = true)
{
    public static ChartRenderOptions Default { get; } = new();

    public void Validate()
    {
        if (VisibleBars <= 0) throw new ArgumentOutOfRangeException(nameof(VisibleBars));
        if (MainPanelRatio <= 0f || MainPanelRatio >= 1f)
            throw new ArgumentOutOfRangeException(nameof(MainPanelRatio));
        if (VolumePanelRatio < 0f || MainPanelRatio + VolumePanelRatio >= 1f)
            throw new ArgumentOutOfRangeException(nameof(VolumePanelRatio));
        if (LeftPadding < 0f || RightPadding < 0f || TopPadding < 0f || BottomPadding < 0f)
            throw new ArgumentOutOfRangeException(nameof(LeftPadding));
    }
}
