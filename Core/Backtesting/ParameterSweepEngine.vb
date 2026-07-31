Imports ChartKit.Models

Namespace Core.Backtesting
    Public NotInheritable Class ParameterSweepEngine
        Private ReadOnly _engine As New StrategyBacktestEngine()

        Public Function Evaluate(cachedCandles As IReadOnlyDictionary(Of String, List(Of CandleItem)),
                                 capturedAt As DateTime,
                                 candidates As IEnumerable(Of StrategyParameterSet)) As List(Of BacktestSummary)
            Return Evaluate(cachedCandles,
                            cachedCandles.Keys.ToDictionary(Function(x) x, Function(x) capturedAt,
                                                            StringComparer.Ordinal),
                            candidates)
        End Function

        Public Function EvaluateCases(cases As IEnumerable(Of BacktestCase),
                                      candidates As IEnumerable(Of StrategyParameterSet),
                                      Optional stability As StabilitySelectionOptions = Nothing,
                                      Optional portfolio As PortfolioSimulationOptions = Nothing) As List(Of BacktestSummary)
            Dim caseList = cases.ToList()
            Dim ranked As New List(Of BacktestSummary)()
            For Each candidate In candidates
                Dim summary As New BacktestSummary With {.Parameters = candidate.Clone()}
                For Each item In caseList
                    Try
                        Dim result = _engine.Evaluate(
                            item.Symbol, item.Candles, item.CapturedAt, candidate)
                        result.CaseId = item.CaseId
                        summary.Symbols.Add(result)
                    Catch ex As Exception
                        summary.Symbols.Add(New SymbolBacktestResult With {
                            .CaseId = item.CaseId, .Symbol = item.Symbol,
                            .CapturedAt = item.CapturedAt, .ErrorMessage = ex.Message})
                    End Try
                Next
                If portfolio IsNot Nothing Then
                    summary.Portfolio = New PortfolioSimulator().Evaluate(summary, portfolio)
                End If
                ranked.Add(summary)
            Next
            Return Rank(ranked, stability)
        End Function

        Public Function Evaluate(cachedCandles As IReadOnlyDictionary(Of String, List(Of CandleItem)),
                                 capturedAtBySymbol As IReadOnlyDictionary(Of String, DateTime),
                                 candidates As IEnumerable(Of StrategyParameterSet)) As List(Of BacktestSummary)
            Dim ranked As New List(Of BacktestSummary)()
            For Each candidate In candidates
                Dim summary As New BacktestSummary With {.Parameters = candidate.Clone()}
                For Each pair In cachedCandles
                    Try
                        Dim capturedAt As DateTime
                        If Not capturedAtBySymbol.TryGetValue(pair.Key, capturedAt) Then
                            Throw New InvalidOperationException($"{pair.Key} 포착시각이 없습니다.")
                        End If
                        summary.Symbols.Add(_engine.Evaluate(pair.Key, pair.Value, capturedAt, candidate))
                    Catch ex As Exception
                        summary.Symbols.Add(New SymbolBacktestResult With {
                            .Symbol = pair.Key, .ErrorMessage = ex.Message})
                    End Try
                Next
                ranked.Add(summary)
            Next
            Return Rank(ranked)
        End Function

        Private Shared Function Rank(items As IEnumerable(Of BacktestSummary),
                                     Optional stability As StabilitySelectionOptions = Nothing) As List(Of BacktestSummary)
            Dim values = items.ToList()
            If stability Is Nothing OrElse Not stability.Enabled Then
                Return values.OrderByDescending(Function(x) x.EqualWeightNetReturnPct).
                ThenByDescending(Function(x) x.WinningSymbolRatePct).
                ThenByDescending(Function(x) x.TotalTradeCount).ToList()
            End If

            Dim validation = stability.Validate()
            If validation.Length > 0 Then Throw New ArgumentException(validation, NameOf(stability))
            For Each item In values
                Dim neighbors = values.Where(Function(x) Not Object.ReferenceEquals(x, item)).
                    OrderBy(Function(x) ParameterDistance(item.Parameters, x.Parameters)).
                    Take(stability.NeighborCount).
                    Select(Function(x) x.EqualWeightNetReturnPct).ToList()
                item.NeighborAverageNetReturnPct =
                    If(neighbors.Count = 0, item.EqualWeightNetReturnPct, neighbors.Average())
                item.MeetsMinimumTradeCount = item.TotalTradeCount >= stability.MinimumTradeCount
                item.StabilityScore =
                    item.EqualWeightNetReturnPct +
                    stability.NeighborReturnWeight * item.NeighborAverageNetReturnPct -
                    stability.DrawdownPenaltyWeight * item.WorstSymbolDrawdownPct
            Next

            Dim eligible = values.Where(Function(x) x.MeetsMinimumTradeCount).ToList()
            Dim ranked = If(eligible.Count > 0, eligible, values)
            Return ranked.OrderByDescending(Function(x) x.StabilityScore).
                ThenByDescending(Function(x) x.EqualWeightNetReturnPct).
                ThenByDescending(Function(x) x.WinningSymbolRatePct).
                ThenByDescending(Function(x) x.TotalTradeCount).ToList()
        End Function

        Public Shared Function RankEvaluated(items As IEnumerable(Of BacktestSummary),
                                             stability As StabilitySelectionOptions) As List(Of BacktestSummary)
            If items Is Nothing Then Throw New ArgumentNullException(NameOf(items))
            Return Rank(items, stability)
        End Function

        Private Shared Function ParameterDistance(left As StrategyParameterSet,
                                                  right As StrategyParameterSet) As Double
            If left Is Nothing OrElse right Is Nothing Then Return Double.MaxValue
            Dim differences = {
                RelativeDifference(left.ShortJma.Period, right.ShortJma.Period),
                RelativeDifference(left.LongJma.Period, right.LongJma.Period),
                RelativeDifference(left.Macd.FastPeriod, right.Macd.FastPeriod),
                RelativeDifference(left.Macd.SlowPeriod, right.Macd.SlowPeriod),
                RelativeDifference(left.Macd.SignalPeriod, right.Macd.SignalPeriod)}
            Return Math.Sqrt(differences.Sum(Function(x) x * x))
        End Function

        Private Shared Function RelativeDifference(left As Integer, right As Integer) As Double
            Return Math.Abs(CDbl(left - right)) / Math.Max(1.0R, Math.Max(Math.Abs(left), Math.Abs(right)))
        End Function

        Public Shared Function Generate(baseParameters As StrategyParameterSet,
                                        shortPeriods As IEnumerable(Of Integer),
                                        longPeriods As IEnumerable(Of Integer),
                                        macdFast As IEnumerable(Of Integer),
                                        macdSlow As IEnumerable(Of Integer),
                                        macdSignal As IEnumerable(Of Integer)) As List(Of StrategyParameterSet)
            Dim output As New List(Of StrategyParameterSet)()
            For Each shortPeriod In shortPeriods.Distinct()
                For Each longPeriod In longPeriods.Distinct()
                    For Each fast In macdFast.Distinct()
                        For Each slow In macdSlow.Distinct()
                            For Each signal In macdSignal.Distinct()
                                Dim item = baseParameters.Clone()
                                item.ShortJma.Period = shortPeriod
                                item.LongJma.Period = longPeriod
                                item.Macd.FastPeriod = fast
                                item.Macd.SlowPeriod = slow
                                item.Macd.SignalPeriod = signal
                                If item.Validate().Length = 0 Then output.Add(item)
                            Next
                        Next
                    Next
                Next
            Next
            Return output
        End Function
    End Class
End Namespace
