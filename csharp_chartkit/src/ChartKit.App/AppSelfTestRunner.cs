namespace ChartKit.CSharp.App;

internal static class AppSelfTestRunner
{
    public static async Task<int> RunAsync(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await MacdModuleAppVerification.RunAsync(options.Timeframe);
        return await ConsoleModes.RunSelfTestAsync(options);
    }
}
