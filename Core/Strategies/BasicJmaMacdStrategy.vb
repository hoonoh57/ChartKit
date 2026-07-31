Imports ChartKit.Abstractions
Imports ChartKit.Core.Signals
Imports ChartKit.Models

Namespace Core.Strategies
    '' Frozen causal strategy v1.
    '' A decision made with candle i is always executed at candle i+1 Open.
    Public NotInheritable Class BasicJmaMacdStrategy
        Public Const StrategyId As String = "BasicJmaMacdStrategy_v1"
        Public Const RequiredMacdName As String = "MACD_10_20_5"
        Public Const MaximumEntryGainPct As Double = 20.0R

        Private Sub New()
        End Sub

        Public Shared Function Evaluate(candles As IReadOnlyList(Of CandleItem),
                                        macdResults As IReadOnlyList(Of IndicatorResult),
                                        strengthRanges As IEnumerable(Of QualifiedTrendRange),
                                        capture As StrategyCapture,
                                        Optional reentryOptions As StrategyReentryLockOptions = Nothing) As StrategyEvaluation
            Dim evaluation As New StrategyEvaluation()
            If candles Is Nothing OrElse macdResults Is Nothing OrElse
               strengthRanges Is Nothing OrElse capture Is Nothing Then Return evaluation
            If candles.Count < 2 OrElse capture.CandleIndex < 0 OrElse
               capture.CandleIndex >= candles.Count OrElse capture.CapturePrice <= 0 Then Return evaluation

            Dim inStrength(candles.Count - 1) As Boolean
            For Each range In strengthRanges
                If range Is Nothing Then Continue For
                Dim first = Math.Max(0, range.StartIndex)
                Dim last = Math.Min(candles.Count - 1, range.EndIndex)
                For i = first To last
                    inStrength(i) = True
                Next
            Next

            Dim active As StrategyTrade = Nothing
            Dim reentryLocked = False
            Dim options = If(reentryOptions, New StrategyReentryLockOptions())
            Dim firstDecision = Math.Max(capture.CandleIndex, 0)
            For decisionIndex = firstDecision To candles.Count - 2
                If active Is Nothing Then
                    If options.SameTradingDayOnly AndAlso
                       candles(decisionIndex).Dt.Date <> capture.CapturedAt.Date Then Continue For
                    If options.MaximumTradeCount > 0 AndAlso
                       evaluation.Trades.Count >= options.MaximumTradeCount Then reentryLocked = True
                    If Not reentryLocked AndAlso
                       IsEntryDecision(candles, macdResults, inStrength, decisionIndex,
                                       capture.CapturePrice, options.MaximumEntryGainPct) Then
                        active = TryOpenTrade(candles, decisionIndex, capture.CapturePrice,
                                              options.MaximumEntryGainPct)
                        If active IsNot Nothing Then evaluation.Trades.Add(active)
                    End If
                Else
                    UpdateExcursions(active, candles(decisionIndex))
                    Dim reason = ExitDecision(macdResults, inStrength, decisionIndex)
                    If reason <> StrategyExitReason.None Then
                        CloseTrade(active, candles, decisionIndex, reason)
                        active = Nothing
                        reentryLocked = ShouldLockReentry(evaluation.Trades, options)
                    End If
                End If
            Next

            If active IsNot Nothing Then
                UpdateExcursions(active, candles(candles.Count - 1))
                active.ExitPrice = candles(candles.Count - 1).Close
                active.ExitTime = candles(candles.Count - 1).Dt
                active.ExitReason = StrategyExitReason.EndOfData
                active.IsOpen = True
            End If
            Return evaluation
        End Function

        Private Shared Function ShouldLockReentry(trades As IEnumerable(Of StrategyTrade),
                                                  options As StrategyReentryLockOptions) As Boolean
            If options Is Nothing Then Return False

            Dim compounded = 1.0R
            Dim hasClosedTrade = False
            For Each trade In trades
                If trade.IsOpen Then Continue For
                compounded *= 1.0R + trade.ReturnPct / 100.0R
                hasClosedTrade = True
            Next
            Dim cumulativePct = (compounded - 1.0R) * 100.0R
            If options.CumulativeLossLockEnabled AndAlso options.CumulativeLossThresholdPct > 0.0R AndAlso
               hasClosedTrade AndAlso cumulativePct <= -options.CumulativeLossThresholdPct Then Return True
            If options.ThresholdPct <= 0.0R Then Return False

            If options.Mode = StrategyReentryLockMode.SingleTradeReturn Then
                For Each trade In trades
                    If Not trade.IsOpen AndAlso trade.ReturnPct >= options.ThresholdPct Then Return True
                Next
                Return False
            End If

            Return hasClosedTrade AndAlso
                   cumulativePct >= options.ThresholdPct
        End Function

        Private Shared Function IsEntryDecision(candles As IReadOnlyList(Of CandleItem),
                                                macdResults As IReadOnlyList(Of IndicatorResult),
                                                inStrength As Boolean(),
                                                index As Integer,
                                                capturePrice As Single,
                                                maximumEntryGainPct As Double) As Boolean
            If index < 0 OrElse index >= candles.Count OrElse Not inStrength(index) Then Return False
            If candles(index).Close < capturePrice Then Return False
            If IsAtOrAboveEntryCap(candles(index).Close, capturePrice, maximumEntryGainPct) Then Return False
            Dim macd = ValueAt(macdResults, index, "MACD")
            Dim signal = ValueAt(macdResults, index, "Signal")
            Return Not Single.IsNaN(macd) AndAlso Not Single.IsNaN(signal) AndAlso
                   macd > 0.0F AndAlso macd > signal
        End Function

        Private Shared Function ExitDecision(macdResults As IReadOnlyList(Of IndicatorResult),
                                             inStrength As Boolean(),
                                             index As Integer) As StrategyExitReason
            If index < 0 OrElse index >= inStrength.Length OrElse Not inStrength(index) Then
                Return StrategyExitReason.StrengthRangeEnded
            End If
            Dim macd = ValueAt(macdResults, index, "MACD")
            Dim signal = ValueAt(macdResults, index, "Signal")
            If Single.IsNaN(macd) OrElse Single.IsNaN(signal) OrElse macd <= signal Then
                Return StrategyExitReason.MacdLostSignal
            End If
            Return StrategyExitReason.None
        End Function

        Private Shared Function TryOpenTrade(candles As IReadOnlyList(Of CandleItem),
                                             decisionIndex As Integer,
                                             capturePrice As Single,
                                             maximumEntryGainPct As Double) As StrategyTrade
            Dim executionIndex = decisionIndex + 1
            Dim price = candles(executionIndex).Open
            '' 판단봉은 20% 미만이어도 다음 봉 시가가 갭으로 상한을 넘으면 추격 체결하지 않는다.
            If IsAtOrAboveEntryCap(price, capturePrice, maximumEntryGainPct) Then Return Nothing
            Return New StrategyTrade With {
                .EntryDecisionIndex = decisionIndex,
                .EntryIndex = executionIndex,
                .EntryTime = candles(executionIndex).Dt,
                .EntryPrice = price,
                .ExitPrice = price,
                .IsOpen = True}
        End Function

        Private Shared Function GainPct(price As Single, basePrice As Single) As Double
            If price <= 0 OrElse basePrice <= 0 Then Return Double.NaN
            Return (CDbl(price) / CDbl(basePrice) - 1.0R) * 100.0R
        End Function

        Private Shared Function IsAtOrAboveEntryCap(price As Single, capturePrice As Single,
                                                    maximumEntryGainPct As Double) As Boolean
            If price <= 0 OrElse capturePrice <= 0 Then Return True
            If maximumEntryGainPct <= 0.0R Then Return False
            Dim capPrice = CDbl(capturePrice) * (1.0R + maximumEntryGainPct / 100.0R)
            Dim tolerance = Math.Max(0.000001R, Math.Abs(capPrice) * 0.000000001R)
            Return CDbl(price) >= capPrice - tolerance
        End Function

        Private Shared Sub CloseTrade(trade As StrategyTrade,
                                      candles As IReadOnlyList(Of CandleItem),
                                      decisionIndex As Integer,
                                      reason As StrategyExitReason)
            Dim executionIndex = decisionIndex + 1
            trade.ExitDecisionIndex = decisionIndex
            trade.ExitIndex = executionIndex
            trade.ExitTime = candles(executionIndex).Dt
            trade.ExitPrice = candles(executionIndex).Open
            trade.ExitReason = reason
            trade.IsOpen = False
        End Sub

        Private Shared Sub UpdateExcursions(trade As StrategyTrade, candle As CandleItem)
            If trade.EntryPrice <= 0 Then Return
            Dim favorable = (CDbl(candle.High) / CDbl(trade.EntryPrice) - 1.0R) * 100.0R
            Dim adverse = (CDbl(candle.Low) / CDbl(trade.EntryPrice) - 1.0R) * 100.0R
            trade.MaximumFavorableExcursionPct = Math.Max(trade.MaximumFavorableExcursionPct, favorable)
            trade.MaximumAdverseExcursionPct = Math.Min(trade.MaximumAdverseExcursionPct, adverse)
        End Sub

        Private Shared Function ValueAt(results As IReadOnlyList(Of IndicatorResult),
                                        index As Integer,
                                        key As String) As Single
            If index < 0 OrElse index >= results.Count Then Return Single.NaN
            Dim result = results(index)
            If result Is Nothing OrElse result.Values Is Nothing Then Return Single.NaN
            Dim value As Single
            Return If(result.Values.TryGetValue(key, value), value, Single.NaN)
        End Function
    End Class
End Namespace
