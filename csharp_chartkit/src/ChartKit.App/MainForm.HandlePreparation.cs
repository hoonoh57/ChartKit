namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    internal void PrepareForDesktopRun()
    {
        if (IsHandleCreated)
            throw new InvalidOperationException(
                "Desktop controls must be prepared before the form handle is created.");

        InitializeDataControls();
        InstallDataRequestLifecycle();
        ApplyToolbarStyle();
    }
}
