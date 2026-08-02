namespace ChartKit.CSharp.App;

internal static class AppSelfTestRunner
{
    public static async Task<int> RunAsync(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await DataRequestSchedulerVerification.RunAsync();
        await VwapModuleAppVerification.RunAsync(options.Timeframe);
        await DisparityModuleAppVerification.RunAsync(options.Timeframe);
        await ObvModuleAppVerification.RunAsync(options.Timeframe);
        await JmaModuleAppVerification.RunAsync(options.Timeframe);
        await SuperTrendModuleAppVerification.RunAsync(options.Timeframe);
        await MacdModuleAppVerification.RunAsync(options.Timeframe);
        await MacdPanelAppVerification.RunAsync(options.Timeframe.ToString());
        return await ConsoleModes.RunSelfTestAsync(options);
    }
}
