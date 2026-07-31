namespace ChartKit.CSharp.Rendering;

public sealed record ChartRenderOptions(
    bool ShowText = true,
    bool ShowAxes = true)
{
    public static ChartRenderOptions Default { get; } = new();

    public void Validate()
    {
    }
}
