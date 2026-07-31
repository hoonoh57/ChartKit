Imports ChartKit.Abstractions
Imports ChartKit.Core.Signals
Imports ChartKit.Core.Strategies

Namespace Core.Backtesting
    Public NotInheritable Class BacktestCostDefaults
        Private Sub New()
        End Sub

        ' 키움증권 KRX 온라인/OpenAPI 국내주식 기본 수수료(편도).
        Public Const KiwoomKrxCommissionPctPerSide As Double = 0.015R

        ' 2026년 KRX 상장주식 매도 시 적용되는 총 거래세율.
        ' KOSPI: 증권거래세 0.05% + 농어촌특별세 0.15%
        ' KOSDAQ: 증권거래세 0.20%
        Public Const KrxStockSellTaxPct2026 As Double = 0.2R

        ' 슬리피지는 증권사 수수료가 아닌 체결가격 모델의 기본 가정이다.
        ' 사용자가 종목 유동성과 주문 방식에 맞게 UI에서 변경할 수 있다.
        Public Const DefaultSlippageBpsPerSide As Double = 1.0R
    End Class

    Public NotInheritable Class JmaParameters
        Public Property Period As Integer = 10
        Public Property Phase As Integer = 50
        Public Property Power As Integer = 2
        Public Function Clone() As JmaParameters
            Return New JmaParameters With {.Period = Period, .Phase = Phase, .Power = Power}
        End Function
    End Class

    Public NotInheritable Class MacdParameters
        Public Property FastPeriod As Integer = 10
        Public Property SlowPeriod As Integer = 20
        Public Property SignalPeriod As Integer = 5
        Public Function Clone() As MacdParameters
            Return New MacdParameters With {
                .FastPeriod = FastPeriod, .SlowPeriod = SlowPeriod, .SignalPeriod = SignalPeriod}
        End Function
    End Class

    Public NotInheritable Class StrategyParameterSet
        Public Property ShortJma As New JmaParameters With {.Period = 10}
        Public Property LongJma As New JmaParameters With {.Period = 50}
        Public Property Macd As New MacdParameters()
        Public Property Qualification As New TrendQualificationOptions()
        Public Property Safety As New StrategyReentryLockOptions()
        Public Property Costs As New BacktestCostOptions()

        Public Function Clone() As StrategyParameterSet
            Return New StrategyParameterSet With {
                .ShortJma = ShortJma.Clone(),
                .LongJma = LongJma.Clone(),
                .Macd = Macd.Clone(),
                .Qualification = Qualification.Clone(),
                .Safety = Safety.Clone(),
                .Costs = Costs.Clone()}
        End Function

        Public Function Validate() As String
            If ShortJma.Period < 2 OrElse LongJma.Period < 2 Then Return "JMA 기간은 2 이상이어야 합니다."
            If ShortJma.Period = LongJma.Period Then Return "단기·장기 JMA 기간은 서로 달라야 합니다."
            If Macd.FastPeriod < 1 OrElse Macd.SlowPeriod < 2 OrElse Macd.SignalPeriod < 1 Then
                Return "MACD 기간은 1 이상이어야 합니다."
            End If
            If Macd.FastPeriod >= Macd.SlowPeriod Then Return "MACD Fast는 Slow보다 작아야 합니다."
            If Qualification.ConfirmationBars < 1 Then Return "강도 확정 봉 수는 1 이상이어야 합니다."
            Return ""
        End Function

        Public Overrides Function ToString() As String
            Return $"JMA({ShortJma.Period},{ShortJma.Phase},{ShortJma.Power})/" &
                   $"JMA({LongJma.Period},{LongJma.Phase},{LongJma.Power}) " &
                   $"MACD({Macd.FastPeriod},{Macd.SlowPeriod},{Macd.SignalPeriod}) " &
                   $"Score>={Qualification.MinimumEntryScore}, Confirm={Qualification.ConfirmationBars}"
        End Function
    End Class

    Public NotInheritable Class BacktestCostOptions
        Public Property CommissionPctPerSide As Double =
            BacktestCostDefaults.KiwoomKrxCommissionPctPerSide
        Public Property BuySlippageBps As Double =
            BacktestCostDefaults.DefaultSlippageBpsPerSide
        Public Property SellSlippageBps As Double =
            BacktestCostDefaults.DefaultSlippageBpsPerSide
        Public Property SellTaxPct As Double =
            BacktestCostDefaults.KrxStockSellTaxPct2026

        Public Function Clone() As BacktestCostOptions
            Return New BacktestCostOptions With {
                .CommissionPctPerSide = CommissionPctPerSide,
                .BuySlippageBps = BuySlippageBps,
                .SellSlippageBps = SellSlippageBps,
                .SellTaxPct = SellTaxPct}
        End Function
    End Class

    Public NotInheritable Class BacktestRequest
        Public Property Symbols As New List(Of String)()
        Public Property CapturedAt As DateTime
        Public Property Interval As CandleInterval = CandleInterval.Tick240
        Public Property CandleCount As Integer = 2000
        Public Property Parameters As New StrategyParameterSet()
    End Class

    Public NotInheritable Class BacktestCase
        Public Property CaseId As String = ""
        Public Property Symbol As String = ""
        Public Property CapturedAt As DateTime
        Public Property Candles As IReadOnlyList(Of Models.CandleItem)
    End Class

    Public NotInheritable Class PortfolioSimulationOptions
        Public Property InitialCapital As Double = 100000000.0R
        Public Property MaximumConcurrentPositions As Integer = 5

        Public Function Clone() As PortfolioSimulationOptions
            Return New PortfolioSimulationOptions With {
                .InitialCapital = InitialCapital,
                .MaximumConcurrentPositions = MaximumConcurrentPositions}
        End Function

        Public Function Validate() As String
            If InitialCapital <= 0.0R Then Return "초기자본은 0보다 커야 합니다."
            If MaximumConcurrentPositions < 1 Then Return "동시보유 한도는 1 이상이어야 합니다."
            Return ""
        End Function
    End Class

    Public NotInheritable Class PortfolioEquityPoint
        Public Property Timestamp As DateTime
        Public Property Equity As Double
        Public Property Cash As Double
        Public Property OpenPositionCount As Integer
        Public Property EventDescription As String = ""
    End Class

    Public NotInheritable Class PortfolioSimulationResult
        Public Property InitialCapital As Double
        Public Property FinalEquity As Double
        Public Property RealizedMaximumDrawdownPct As Double
        Public Property AverageExposurePct As Double
        Public Property MaximumConcurrentPositions As Integer
        Public Property ExecutedTradeCount As Integer
        Public Property RejectedEntryCount As Integer
        Public Property EquityCurve As New List(Of PortfolioEquityPoint)()
        Public ReadOnly Property NetReturnPct As Double
            Get
                If InitialCapital <= 0.0R Then Return 0.0R
                Return (FinalEquity / InitialCapital - 1.0R) * 100.0R
            End Get
        End Property
    End Class

    Public NotInheritable Class SymbolBacktestResult
        Public Property CaseId As String = ""
        Public Property Symbol As String = ""
        Public Property CapturedAt As DateTime
        Public Property CaptureIndex As Integer = -1
        Public Property CapturePrice As Single
        Public Property Evaluation As StrategyEvaluation
        Public Property ErrorMessage As String = ""
        Public Property Costs As New BacktestCostOptions()
        Public ReadOnly Property ReturnPct As Double
            Get
                Return If(Evaluation Is Nothing, 0.0R, Evaluation.TotalReturnPct)
            End Get
        End Property
        Public ReadOnly Property NetReturnPct As Double
            Get
                If Evaluation Is Nothing Then Return 0.0R
                Dim equity = 1.0R
                For Each trade In Evaluation.Trades
                    If Not trade.IsOpen Then equity *= 1.0R + NetTradeReturnPct(trade, Costs) / 100.0R
                Next
                Return (equity - 1.0R) * 100.0R
            End Get
        End Property
        Public ReadOnly Property MaximumDrawdownPct As Double
            Get
                If Evaluation Is Nothing Then Return 0.0R
                Dim equity = 1.0R
                Dim peak = 1.0R
                Dim maximumDrawdown = 0.0R
                For Each trade In Evaluation.Trades
                    If trade.IsOpen Then Continue For
                    equity *= 1.0R + NetTradeReturnPct(trade, Costs) / 100.0R
                    peak = Math.Max(peak, equity)
                    If peak > 0.0R Then
                        maximumDrawdown = Math.Max(maximumDrawdown, (peak - equity) / peak * 100.0R)
                    End If
                Next
                Return maximumDrawdown
            End Get
        End Property
        Public ReadOnly Property WinRatePct As Double
            Get
                If Evaluation Is Nothing Then Return 0.0R
                Dim closed = Evaluation.Trades.Where(Function(x) Not x.IsOpen).ToList()
                If closed.Count = 0 Then Return 0.0R
                Return closed.Where(Function(x) NetTradeReturnPct(x, Costs) > 0.0R).Count() *
                    100.0R / closed.Count
            End Get
        End Property

        Public Shared Function NetTradeReturnPct(trade As StrategyTrade,
                                                 costs As BacktestCostOptions) As Double
            If trade Is Nothing OrElse trade.EntryPrice <= 0 OrElse trade.ExitPrice <= 0 Then Return 0.0R
            If costs Is Nothing Then costs = New BacktestCostOptions()
            Dim commission = Math.Max(0.0R, costs.CommissionPctPerSide) / 100.0R
            Dim buySlip = Math.Max(0.0R, costs.BuySlippageBps) / 10000.0R
            Dim sellSlip = Math.Max(0.0R, costs.SellSlippageBps) / 10000.0R
            Dim sellTax = Math.Max(0.0R, costs.SellTaxPct) / 100.0R
            Dim paid = CDbl(trade.EntryPrice) * (1.0R + buySlip) * (1.0R + commission)
            Dim received = CDbl(trade.ExitPrice) * (1.0R - sellSlip) *
                Math.Max(0.0R, 1.0R - commission - sellTax)
            Return (received / paid - 1.0R) * 100.0R
        End Function
    End Class

    Public NotInheritable Class WalkForwardOptions
        Public Property TrainingDateCount As Integer = 3
        Public Property TestDateCount As Integer = 1
        Public Property StepDateCount As Integer = 1
        Public Property Stability As New StabilitySelectionOptions()
        Public Property Portfolio As New PortfolioSimulationOptions()
    End Class

    Public NotInheritable Class StabilitySelectionOptions
        Public Property Enabled As Boolean = True
        Public Property MinimumTradeCount As Integer = 3
        Public Property DrawdownPenaltyWeight As Double = 0.5R
        Public Property NeighborReturnWeight As Double = 0.5R
        Public Property NeighborCount As Integer = 4

        Public Function Validate() As String
            If MinimumTradeCount < 0 Then Return "최소 거래 수는 0 이상이어야 합니다."
            If DrawdownPenaltyWeight < 0.0R Then Return "MDD 패널티 가중치는 0 이상이어야 합니다."
            If NeighborReturnWeight < 0.0R Then Return "인접 견고성 가중치는 0 이상이어야 합니다."
            If NeighborCount < 1 Then Return "인접 후보 수는 1 이상이어야 합니다."
            Return ""
        End Function
    End Class

    Public NotInheritable Class WalkForwardFoldResult
        Public Property FoldNumber As Integer
        Public Property TrainingFrom As DateTime
        Public Property TrainingTo As DateTime
        Public Property TestFrom As DateTime
        Public Property TestTo As DateTime
        Public Property SelectedParameters As StrategyParameterSet
        Public Property TrainingSummary As BacktestSummary
        Public Property TestSummary As BacktestSummary
    End Class

    Public NotInheritable Class WalkForwardResult
        Public Property Folds As New List(Of WalkForwardFoldResult)()
        Public ReadOnly Property AverageOutOfSampleNetReturnPct As Double
            Get
                If Folds.Count = 0 Then Return 0.0R
                Return Folds.Average(Function(x) x.TestSummary.EqualWeightNetReturnPct)
            End Get
        End Property
        Public ReadOnly Property ProfitableFoldRatePct As Double
            Get
                If Folds.Count = 0 Then Return 0.0R
                Return Folds.Where(Function(x) x.TestSummary.EqualWeightNetReturnPct > 0.0R).Count() *
                    100.0R / Folds.Count
            End Get
        End Property
    End Class

    Public NotInheritable Class BacktestSummary
        Public Property Parameters As StrategyParameterSet
        Public Property Symbols As New List(Of SymbolBacktestResult)()
        Public Property StabilityScore As Double
        Public Property NeighborAverageNetReturnPct As Double
        Public Property MeetsMinimumTradeCount As Boolean = True
        Public Property Portfolio As PortfolioSimulationResult
        Public ReadOnly Property TestedSymbolCount As Integer
            Get
                Return Symbols.Where(Function(x) x.Evaluation IsNot Nothing).Count()
            End Get
        End Property
        Public ReadOnly Property TotalTradeCount As Integer
            Get
                Return Symbols.Where(Function(x) x.Evaluation IsNot Nothing).
                    Sum(Function(x) x.Evaluation.ClosedTradeCount)
            End Get
        End Property
        Public ReadOnly Property EqualWeightReturnPct As Double
            Get
                Dim valid = Symbols.Where(Function(x) x.Evaluation IsNot Nothing).ToList()
                If valid.Count = 0 Then Return 0.0R
                Return valid.Average(Function(x) x.ReturnPct)
            End Get
        End Property
        Public ReadOnly Property EqualWeightNetReturnPct As Double
            Get
                Dim valid = Symbols.Where(Function(x) x.Evaluation IsNot Nothing).ToList()
                If valid.Count = 0 Then Return 0.0R
                Return valid.Average(Function(x) x.NetReturnPct)
            End Get
        End Property
        Public ReadOnly Property WorstSymbolDrawdownPct As Double
            Get
                Dim valid = Symbols.Where(Function(x) x.Evaluation IsNot Nothing).ToList()
                If valid.Count = 0 Then Return 0.0R
                Return valid.Max(Function(x) x.MaximumDrawdownPct)
            End Get
        End Property
        Public ReadOnly Property WinningSymbolRatePct As Double
            Get
                Dim valid = Symbols.Where(Function(x) x.Evaluation IsNot Nothing).ToList()
                If valid.Count = 0 Then Return 0.0R
                Return valid.Where(Function(x) x.NetReturnPct > 0.0R).Count() * 100.0R / valid.Count
            End Get
        End Property
    End Class
End Namespace
