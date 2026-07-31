Imports ChartKit.Core.Strategies

Namespace Core.Backtesting
    Public NotInheritable Class PortfolioSimulator
        Private NotInheritable Class TradeEvent
            Public Property Timestamp As DateTime
            Public Property IsExit As Boolean
            Public Property TradeKey As String = ""
            Public Property Symbol As String = ""
            Public Property Trade As StrategyTrade
            Public Property Costs As BacktestCostOptions
        End Class

        Private NotInheritable Class Position
            Public Property AllocatedCapital As Double
            Public Property Trade As StrategyTrade
            Public Property Costs As BacktestCostOptions
        End Class

        Public Function Evaluate(summary As BacktestSummary,
                                 options As PortfolioSimulationOptions) As PortfolioSimulationResult
            If summary Is Nothing Then Throw New ArgumentNullException(NameOf(summary))
            If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
            Dim validation = options.Validate()
            If validation.Length > 0 Then Throw New ArgumentException(validation, NameOf(options))

            Dim events = CreateEvents(summary)
            Dim output As New PortfolioSimulationResult With {
                .InitialCapital = options.InitialCapital,
                .FinalEquity = options.InitialCapital}
            If events.Count = 0 Then Return output

            Dim cash = options.InitialCapital
            Dim peak = options.InitialCapital
            Dim active As New Dictionary(Of String, Position)(StringComparer.Ordinal)
            Dim exposureArea = 0.0R
            Dim totalSeconds = Math.Max(0.0R, (events.Last().Timestamp - events.First().Timestamp).TotalSeconds)
            Dim previousTime = events.First().Timestamp

            For Each item In events
                Dim elapsed = Math.Max(0.0R, (item.Timestamp - previousTime).TotalSeconds)
                exposureArea += elapsed * active.Count / options.MaximumConcurrentPositions
                previousTime = item.Timestamp

                If item.IsExit Then
                    Dim position As Position = Nothing
                    If active.TryGetValue(item.TradeKey, position) Then
                        Dim netReturn = SymbolBacktestResult.NetTradeReturnPct(position.Trade, position.Costs)
                        cash += position.AllocatedCapital * (1.0R + netReturn / 100.0R)
                        active.Remove(item.TradeKey)
                        output.ExecutedTradeCount += 1
                    End If
                ElseIf active.Count >= options.MaximumConcurrentPositions Then
                    output.RejectedEntryCount += 1
                Else
                    Dim equityAtCost = cash + active.Values.Sum(Function(x) x.AllocatedCapital)
                    Dim allocation = Math.Min(cash, equityAtCost / options.MaximumConcurrentPositions)
                    If allocation > 0.0R Then
                        cash -= allocation
                        active(item.TradeKey) = New Position With {
                            .AllocatedCapital = allocation,
                            .Trade = item.Trade,
                            .Costs = item.Costs}
                    Else
                        output.RejectedEntryCount += 1
                    End If
                End If

                Dim equity = cash + active.Values.Sum(Function(x) x.AllocatedCapital)
                peak = Math.Max(peak, equity)
                If peak > 0.0R Then
                    output.RealizedMaximumDrawdownPct = Math.Max(
                        output.RealizedMaximumDrawdownPct, (peak - equity) / peak * 100.0R)
                End If
                output.MaximumConcurrentPositions = Math.Max(
                    output.MaximumConcurrentPositions, active.Count)
                output.EquityCurve.Add(New PortfolioEquityPoint With {
                    .Timestamp = item.Timestamp,
                    .Equity = equity,
                    .Cash = cash,
                    .OpenPositionCount = active.Count,
                    .EventDescription = If(item.IsExit, "EXIT ", "ENTRY ") & item.Symbol})
            Next

            output.FinalEquity = cash + active.Values.Sum(Function(x) x.AllocatedCapital)
            output.AverageExposurePct =
                If(totalSeconds <= 0.0R, 0.0R, exposureArea / totalSeconds * 100.0R)
            Return output
        End Function

        Private Shared Function CreateEvents(summary As BacktestSummary) As List(Of TradeEvent)
            Dim output As New List(Of TradeEvent)()
            For Each symbolResult In summary.Symbols
                If symbolResult.Evaluation Is Nothing Then Continue For
                For tradeIndex = 0 To symbolResult.Evaluation.Trades.Count - 1
                    Dim trade = symbolResult.Evaluation.Trades(tradeIndex)
                    If trade.IsOpen OrElse trade.EntryTime = DateTime.MinValue OrElse
                       trade.ExitTime = DateTime.MinValue Then Continue For
                    Dim key = $"{symbolResult.CaseId}|{symbolResult.Symbol}|{tradeIndex}"
                    output.Add(New TradeEvent With {
                        .Timestamp = trade.EntryTime, .IsExit = False, .TradeKey = key,
                        .Symbol = symbolResult.Symbol, .Trade = trade, .Costs = symbolResult.Costs})
                    output.Add(New TradeEvent With {
                        .Timestamp = trade.ExitTime, .IsExit = True, .TradeKey = key,
                        .Symbol = symbolResult.Symbol, .Trade = trade, .Costs = symbolResult.Costs})
                Next
            Next
            Return output.OrderBy(Function(x) x.Timestamp).
                ThenByDescending(Function(x) x.IsExit).
                ThenBy(Function(x) x.TradeKey, StringComparer.Ordinal).ToList()
        End Function
    End Class
End Namespace
