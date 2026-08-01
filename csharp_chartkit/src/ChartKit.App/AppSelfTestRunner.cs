namespace ChartKit.CSharp.App;

internal static class AppSelfTestRunner
{
    public static async Task<int> RunAsync(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await SuperTrendModuleAppVerification.RunAsync(options.Timeframe);
        await MacdModuleAppVerification.RunAsync(options.Timeframe);
        await MacdPanelAppVerification.RunAsync(options.Timeframe.ToString());
        return await ConsoleModes.RunSelfTestAsync(options);
    }
}
