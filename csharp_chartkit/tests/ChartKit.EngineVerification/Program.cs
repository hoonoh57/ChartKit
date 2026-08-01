using ChartKit.CSharp.EngineVerification;

try
{
    RingBufferVerification.Run();
    IndicatorVerification.Run();
    ChartViewportVerification.Run();
    ChartFrameVerification.Run();
    PriceGridVerification.Run();
    ChartPanelAxisVerification.Run();
    ChartCursorVerification.Run();
    ModulePlatformContractVerification.Run();
    ModuleHostVerification.Run();
    await MultiSymbolVerification.RunAsync();
    await RenderingVerification.RunAsync();
    MarketDataNormalizerVerification.Run();
    TickDataVerification.Run();
    await ReplayDataVerification.RunAsync();
    await KiwoomSessionVerification.RunAsync();
    await KiwoomHistoryVerification.RunAsync();
    await KiwoomTickSourceOrderVerification.RunAsync();
    await TradingDayProbeVerification.RunAsync();
    RealtimeBuilderVerification.Run();
    await KiwoomRealtimeReconnectVerification.RunAsync();
    Console.WriteLine("csharp_engine_verification=PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.WriteLine("csharp_engine_verification=FAIL");
    return 1;
}
