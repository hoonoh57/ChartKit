using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.DataSources;

namespace ChartKit.CSharp.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            AppOptions options = AppOptions.Parse(args);
            return options.Mode switch
            {
                ApplicationMode.KiwoomProbe =>
                    ConsoleModes.RunProbeAsync(options).GetAwaiter().GetResult(),
                ApplicationMode.SelfTest =>
                    ConsoleModes.RunSelfTestAsync(options).GetAwaiter().GetResult(),
                _ => RunDesktop(options)
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int RunDesktop(AppOptions options)
    {
        ApplicationConfiguration.Initialize();
        IMarketDataSource source = options.Mode == ApplicationMode.Kiwoom
            ? new KiwoomRestDataSource()
            : new ReplayDataSource();
        var form = new MainForm(options, source);
        form.PrepareForDesktopRun();
        Application.Run(form);
        return 0;
    }
}
