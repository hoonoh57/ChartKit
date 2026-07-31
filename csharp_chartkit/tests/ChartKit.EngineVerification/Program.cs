using ChartKit.CSharp.EngineVerification;

try
{
    RingBufferVerification.Run();
    IndicatorVerification.Run();
    await MultiSymbolVerification.RunAsync();
    Console.WriteLine("csharp_engine_verification=PASS");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Console.WriteLine("csharp_engine_verification=FAIL");
    return 1;
}
