Imports System.IO
Imports System.Text.Json
Imports ChartKit.Abstractions
Imports ChartKit.Core.Strategies

Namespace Core.Backtesting
    Public NotInheritable Class BacktestBaselineSymbol
        Public Property Symbol As String = ""
        Public Property CapturedAt As DateTime
        Public Property CapturePrice As Single
        Public Property TradeCount As Integer
        Public Property NetReturnPct As Double
        Public Property MaximumDrawdownPct As Double
        Public Property WinRatePct As Double
    End Class

    Public NotInheritable Class BacktestBaselineSnapshot
        Public Property SchemaVersion As Integer = 1
        Public Property CreatedAt As DateTime
        Public Property DataSourceName As String = ""
        Public Property StrategyId As String = BasicJmaMacdStrategy.StrategyId
        Public Property Interval As CandleInterval
        Public Property CandleCount As Integer
        Public Property Parameters As New StrategyParameterSet()
        Public Property PortfolioOptions As New PortfolioSimulationOptions()
        Public Property EqualWeightNetReturnPct As Double
        Public Property PortfolioNetReturnPct As Double
        Public Property PortfolioMaximumDrawdownPct As Double
        Public Property WinningSymbolRatePct As Double
        Public Property TotalTradeCount As Integer
        Public Property Symbols As New List(Of BacktestBaselineSymbol)()
    End Class

    Public NotInheritable Class BacktestBaselineComparison
        Public Property SameUniverse As Boolean
        Public Property SameConfiguration As Boolean
        Public Property EqualWeightNetReturnDeltaPct As Double
        Public Property PortfolioNetReturnDeltaPct As Double
        Public Property MaximumDrawdownImprovementPct As Double
        Public Property WinningSymbolRateDeltaPct As Double
        Public Property TradeCountDelta As Integer
    End Class

    Public NotInheritable Class BacktestBaselineStore
        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
            .WriteIndented = True,
            .PropertyNameCaseInsensitive = True}

        Private Sub New()
        End Sub

        Public Shared Function Create(summary As BacktestSummary,
                                      sourceName As String,
                                      interval As CandleInterval,
                                      candleCount As Integer,
                                      portfolioOptions As PortfolioSimulationOptions) As BacktestBaselineSnapshot
            If summary Is Nothing Then Throw New ArgumentNullException(NameOf(summary))
            Dim snapshot As New BacktestBaselineSnapshot With {
                .CreatedAt = Date.Now,
                .DataSourceName = If(sourceName, ""),
                .StrategyId = If(summary.Symbols.
                    Where(Function(x) x.Evaluation IsNot Nothing).
                    Select(Function(x) x.Evaluation.StrategyId).
                    FirstOrDefault(), BasicJmaMacdStrategy.StrategyId),
                .Interval = interval,
                .CandleCount = candleCount,
                .Parameters = summary.Parameters.Clone(),
                .PortfolioOptions = portfolioOptions.Clone(),
                .EqualWeightNetReturnPct = summary.EqualWeightNetReturnPct,
                .PortfolioNetReturnPct = If(summary.Portfolio Is Nothing, 0.0R, summary.Portfolio.NetReturnPct),
                .PortfolioMaximumDrawdownPct =
                    If(summary.Portfolio Is Nothing, 0.0R, summary.Portfolio.RealizedMaximumDrawdownPct),
                .WinningSymbolRatePct = summary.WinningSymbolRatePct,
                .TotalTradeCount = summary.TotalTradeCount}
            snapshot.Symbols = summary.Symbols.Select(
                Function(x) New BacktestBaselineSymbol With {
                    .Symbol = x.Symbol,
                    .CapturedAt = x.CapturedAt,
                    .CapturePrice = x.CapturePrice,
                    .TradeCount = If(x.Evaluation Is Nothing, 0, x.Evaluation.ClosedTradeCount),
                    .NetReturnPct = x.NetReturnPct,
                    .MaximumDrawdownPct = x.MaximumDrawdownPct,
                    .WinRatePct = x.WinRatePct}).ToList()
            Return snapshot
        End Function

        Public Shared Sub Save(path As String, snapshot As BacktestBaselineSnapshot)
            If snapshot Is Nothing Then Throw New ArgumentNullException(NameOf(snapshot))
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions))
        End Sub

        Public Shared Function Load(path As String) As BacktestBaselineSnapshot
            Dim snapshot = JsonSerializer.Deserialize(Of BacktestBaselineSnapshot)(
                File.ReadAllText(path), JsonOptions)
            If snapshot Is Nothing OrElse snapshot.SchemaVersion <> 1 Then
                Throw New InvalidDataException("지원하지 않는 기준선 파일입니다.")
            End If
            Return snapshot
        End Function

        Public Shared Function Compare(current As BacktestSummary,
                                       baseline As BacktestBaselineSnapshot,
                                       interval As CandleInterval,
                                       candleCount As Integer,
                                       portfolioOptions As PortfolioSimulationOptions) As BacktestBaselineComparison
            Dim currentSymbols = current.Symbols.Select(
                Function(x) $"{x.Symbol}|{x.CapturedAt:O}").OrderBy(Function(x) x).ToArray()
            Dim baselineSymbols = baseline.Symbols.Select(
                Function(x) $"{x.Symbol}|{x.CapturedAt:O}").OrderBy(Function(x) x).ToArray()
            Dim currentPortfolioReturn = If(current.Portfolio Is Nothing, 0.0R, current.Portfolio.NetReturnPct)
            Dim currentMdd = If(current.Portfolio Is Nothing, 0.0R,
                                current.Portfolio.RealizedMaximumDrawdownPct)
            Return New BacktestBaselineComparison With {
                .SameUniverse = currentSymbols.SequenceEqual(baselineSymbols),
                .SameConfiguration = ConfigurationEquals(
                    current.Parameters, baseline.Parameters, interval, baseline.Interval,
                    candleCount, baseline.CandleCount, baseline.StrategyId) AndAlso
                    PortfolioConfigurationEquals(portfolioOptions, baseline.PortfolioOptions),
                .EqualWeightNetReturnDeltaPct =
                    current.EqualWeightNetReturnPct - baseline.EqualWeightNetReturnPct,
                .PortfolioNetReturnDeltaPct =
                    currentPortfolioReturn - baseline.PortfolioNetReturnPct,
                .MaximumDrawdownImprovementPct =
                    baseline.PortfolioMaximumDrawdownPct - currentMdd,
                .WinningSymbolRateDeltaPct =
                    current.WinningSymbolRatePct - baseline.WinningSymbolRatePct,
                .TradeCountDelta = current.TotalTradeCount - baseline.TotalTradeCount}
        End Function

        Private Shared Function ConfigurationEquals(current As StrategyParameterSet,
                                                    baseline As StrategyParameterSet,
                                                    currentInterval As CandleInterval,
                                                    baselineInterval As CandleInterval,
                                                    currentCount As Integer,
                                                    baselineCount As Integer,
                                                    strategyId As String) As Boolean
            If current Is Nothing OrElse baseline Is Nothing Then Return False
            If currentInterval <> baselineInterval OrElse currentCount <> baselineCount Then Return False
            If Not String.Equals(strategyId, BasicJmaMacdStrategy.StrategyId,
                                 StringComparison.Ordinal) Then Return False
            Return current.ShortJma.Period = baseline.ShortJma.Period AndAlso
                current.ShortJma.Phase = baseline.ShortJma.Phase AndAlso
                current.ShortJma.Power = baseline.ShortJma.Power AndAlso
                current.LongJma.Period = baseline.LongJma.Period AndAlso
                current.LongJma.Phase = baseline.LongJma.Phase AndAlso
                current.LongJma.Power = baseline.LongJma.Power AndAlso
                current.Macd.FastPeriod = baseline.Macd.FastPeriod AndAlso
                current.Macd.SlowPeriod = baseline.Macd.SlowPeriod AndAlso
                current.Macd.SignalPeriod = baseline.Macd.SignalPeriod AndAlso
                current.Qualification.MinimumEntryScore =
                    baseline.Qualification.MinimumEntryScore AndAlso
                current.Qualification.ConfirmationBars =
                    baseline.Qualification.ConfirmationBars AndAlso
                current.Safety.Mode = baseline.Safety.Mode AndAlso
                NearlyEqual(current.Safety.ThresholdPct, baseline.Safety.ThresholdPct) AndAlso
                NearlyEqual(current.Safety.MaximumEntryGainPct,
                            baseline.Safety.MaximumEntryGainPct) AndAlso
                current.Safety.CumulativeLossLockEnabled =
                    baseline.Safety.CumulativeLossLockEnabled AndAlso
                NearlyEqual(current.Safety.CumulativeLossThresholdPct,
                            baseline.Safety.CumulativeLossThresholdPct) AndAlso
                current.Safety.MaximumTradeCount = baseline.Safety.MaximumTradeCount AndAlso
                current.Safety.SameTradingDayOnly = baseline.Safety.SameTradingDayOnly AndAlso
                NearlyEqual(current.Costs.CommissionPctPerSide,
                            baseline.Costs.CommissionPctPerSide) AndAlso
                NearlyEqual(current.Costs.BuySlippageBps, baseline.Costs.BuySlippageBps) AndAlso
                NearlyEqual(current.Costs.SellSlippageBps, baseline.Costs.SellSlippageBps) AndAlso
                NearlyEqual(current.Costs.SellTaxPct, baseline.Costs.SellTaxPct)
        End Function

        Private Shared Function NearlyEqual(left As Double, right As Double) As Boolean
            Return Math.Abs(left - right) <= 0.000000001R
        End Function

        Private Shared Function PortfolioConfigurationEquals(
            current As PortfolioSimulationOptions,
            baseline As PortfolioSimulationOptions) As Boolean
            If current Is Nothing OrElse baseline Is Nothing Then Return False
            Return NearlyEqual(current.InitialCapital, baseline.InitialCapital) AndAlso
                current.MaximumConcurrentPositions = baseline.MaximumConcurrentPositions
        End Function
    End Class
End Namespace
