namespace ChartKit.CSharp.App;

internal static class AppSelfTestRunner
{
    public static async Task<int> RunAsync(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await MacdModuleAppVerification.RunAsync(options.Timeframe);
        await MacdPanelAppVerification.RunAsync(options.Timeframe.ToString());
        return await ConsoleModes.RunSelfTestAsync(options);
    }
}
