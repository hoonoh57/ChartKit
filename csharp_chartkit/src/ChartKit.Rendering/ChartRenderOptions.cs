namespace ChartKit.CSharp.Rendering;

public sealed record ChartRenderOptions(
    bool ShowText = true,
    bool ShowAxes = true,
    bool ShowDateBoundaries = true)
{
    public static ChartRenderOptions Default { get; } = new();

    public void Validate()
    {
    }
}
