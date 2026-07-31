using ChartKit.CSharp.EngineVerification;

try
{
    RingBufferVerification.Run();
    IndicatorVerification.Run();
    ChartViewportVerification.Run();
    await MultiSymbolVerification.RunAsync();
    await RenderingVerification.RunAsync();
    TickDataVerification.Run();
    await ReplayDataVerification.RunAsync();
    await KiwoomSessionVerification.RunAsync();
    await KiwoomHistoryVerification.RunAsync();
    RealtimeBuilderVerification.Run();
    Console.WriteLine("csharp_engine_verification=PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.WriteLine("csharp_engine_verification=FAIL");
    return 1;
}
