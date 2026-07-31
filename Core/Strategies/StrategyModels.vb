Namespace Core.Strategies
    Public Enum StrategyReentryLockMode
        SingleTradeReturn = 0
        CumulativeClosedReturn = 1
    End Enum

    Public NotInheritable Class StrategyReentryLockOptions
        Public Const DefaultThresholdPct As Double = 10.0R

        Public Property Mode As StrategyReentryLockMode = StrategyReentryLockMode.CumulativeClosedReturn
        Public Property ThresholdPct As Double = DefaultThresholdPct
        Public Property MaximumEntryGainPct As Double = 20.0R
        Public Property CumulativeLossLockEnabled As Boolean
        Public Property CumulativeLossThresholdPct As Double = 5.0R
        Public Property MaximumTradeCount As Integer
        Public Property SameTradingDayOnly As Boolean = True

        Public Function Clone() As StrategyReentryLockOptions
            Return New StrategyReentryLockOptions With {
                .Mode = Mode,
                .ThresholdPct = ThresholdPct,
                .MaximumEntryGainPct = MaximumEntryGainPct,
                .CumulativeLossLockEnabled = CumulativeLossLockEnabled,
                .CumulativeLossThresholdPct = CumulativeLossThresholdPct,
                .MaximumTradeCount = MaximumTradeCount,
                .SameTradingDayOnly = SameTradingDayOnly}
        End Function
    End Class

    Public Enum StrategyExitReason
        None = 0
        StrengthRangeEnded = 1
        MacdLostSignal = 2
        EndOfData = 3
    End Enum

    Public NotInheritable Class StrategyCapture
        Public Property CandleIndex As Integer
        Public Property CapturedAt As DateTime
        Public Property CapturePrice As Single
    End Class

    Public NotInheritable Class StrategyTrade
        Public Property EntryDecisionIndex As Integer
        Public Property EntryIndex As Integer
        Public Property EntryTime As DateTime
        Public Property EntryPrice As Single

        Public Property ExitDecisionIndex As Integer = -1
        Public Property ExitIndex As Integer = -1
        Public Property ExitTime As DateTime
        Public Property ExitPrice As Single
        Public Property ExitReason As StrategyExitReason

        Public Property MaximumFavorableExcursionPct As Double
        Public Property MaximumAdverseExcursionPct As Double
        Public Property IsOpen As Boolean

        Public ReadOnly Property ReturnPct As Double
            Get
                If EntryPrice <= 0 OrElse ExitPrice <= 0 Then Return 0.0R
                Return (CDbl(ExitPrice) / CDbl(EntryPrice) - 1.0R) * 100.0R
            End Get
        End Property
    End Class

    Public NotInheritable Class StrategyEvaluation
        Public Property StrategyId As String = BasicJmaMacdStrategy.StrategyId
        Public Property Trades As New List(Of StrategyTrade)()

        Public ReadOnly Property ClosedTradeCount As Integer
            Get
                Dim count = 0
                For Each trade In Trades
                    If Not trade.IsOpen Then count += 1
                Next
                Return count
            End Get
        End Property

        Public ReadOnly Property TotalReturnPct As Double
            Get
                Dim compounded = 1.0R
                For Each trade In Trades
                    If Not trade.IsOpen Then compounded *= 1.0R + trade.ReturnPct / 100.0R
                Next
                Return (compounded - 1.0R) * 100.0R
            End Get
        End Property
    End Class
End Namespace
