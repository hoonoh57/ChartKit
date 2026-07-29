Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' JMA_Indicator — Jurik Moving Average (적응형 이동평균). 오버레이. 원본 로직 그대로.
    Public Class JMA_Indicator
        Implements IIndicator

        Private _period As Integer = 14
        Private _phase As Integer = 50
        Private _power As Integer = 2
        Private _params As New Dictionary(Of String, Object) From {{"Period", 14}, {"Phase", 50}, {"Power", 2}}
        Private _e0 As Double = Double.NaN
        Private _e1 As Double = Double.NaN
        Private _e2 As Double = Double.NaN
        Private _lastJMA As Double = Double.NaN
        Private _lastDirection As Integer = 0
        Private _calcCount As Integer = 0
        Private _prevE0 As Double = Double.NaN
        Private _prevE1 As Double = Double.NaN
        Private _prevE2 As Double = Double.NaN
        Private _prevJMA As Double = Double.NaN
        Private _prevDirection As Integer = 0
        Private _stateIndex As Integer = -1

        Public Sub New(Optional period As Integer = 14, Optional phase As Integer = 50, Optional power As Integer = 2)
            _period = period
            _phase = phase
            _power = power
            _params("Period") = _period
            _params("Phase") = _phase
            _params("Power") = _power
        End Sub

        Public ReadOnly Property Name As String Implements IIndicator.Name
            Get
                Return $"JMA_{_period}"
            End Get
        End Property

        Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
            Get
                Return $"JMA({_period},{_phase},{_power})"
            End Get
        End Property

        Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
            Get
                Return 0
            End Get
        End Property

        Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
            Get
                Return _params
            End Get
            Set(value As Dictionary(Of String, Object))
                _params = value
                If _params.ContainsKey("Period") Then
                    _period = CInt(_params("Period"))
                End If
                If _params.ContainsKey("Phase") Then
                    _phase = CInt(_params("Phase"))
                End If
                If _params.ContainsKey("Power") Then
                    _power = CInt(_params("Power"))
                End If
            End Set
        End Property

        Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
            Dim count = candles.Count
            Dim results As New List(Of IndicatorResult)(count)
            If count = 0 Then Return results

            Dim phaseRatio = CalcPhaseRatio(_phase)
            Dim beta = 0.45 * (_period - 1) / (0.45 * (_period - 1) + 2)
            Dim alpha = Math.Pow(beta, _power)

            Dim e0 As Double = Double.NaN
            Dim e1 As Double = Double.NaN
            Dim e2 As Double = Double.NaN
            Dim prevJMA As Double = Double.NaN
            Dim prevDir As Integer = 0

            Dim jmaArr(count - 1) As Double
            Dim upArr(count - 1) As Double
            Dim downArr(count - 1) As Double
            Dim slopeArr(count - 1) As Double
            Dim e0State(count - 1) As Double
            Dim e1State(count - 1) As Double
            Dim e2State(count - 1) As Double
            Dim dirState(count - 1) As Integer

            For i = 0 To count - 1
                Dim src = CDbl(candles(i).Close)
                If Double.IsNaN(e0) Then
                    e0 = src
                    e1 = 0.0
                    e2 = 0.0
                    prevJMA = src
                End If

                e0 = (1 - alpha) * src + alpha * e0
                e1 = (src - e0) * (1 - beta) + beta * e1
                e2 = (e0 + phaseRatio * e1 - prevJMA) * Math.Pow(1 - alpha, 2) + Math.Pow(alpha, 2) * e2

                Dim currentJMA As Double
                If i < _period Then
                    Dim sum As Double = 0
                    For j = 0 To i
                        sum += CDbl(candles(j).Close)
                    Next
                    currentJMA = Math.Round(sum / (i + 1), 4)
                Else
                    currentJMA = Math.Round(e2 + prevJMA, 4)
                End If

                jmaArr(i) = currentJMA

                Dim curDir As Integer
                If currentJMA > prevJMA AndAlso Not Double.IsNaN(prevJMA) Then
                    curDir = 1
                ElseIf currentJMA < prevJMA AndAlso Not Double.IsNaN(prevJMA) Then
                    curDir = -1
                Else
                    If prevDir = 0 Then
                        curDir = 1
                    Else
                        curDir = prevDir
                    End If
                End If

                If curDir = 1 Then
                    upArr(i) = currentJMA
                    downArr(i) = Double.NaN
                Else
                    downArr(i) = currentJMA
                    upArr(i) = Double.NaN
                End If

                If i > 0 AndAlso curDir <> prevDir AndAlso prevDir <> 0 Then
                    upArr(i - 1) = jmaArr(i - 1)
                    downArr(i - 1) = jmaArr(i - 1)
                End If

                If i > 0 AndAlso Not Double.IsNaN(jmaArr(i - 1)) AndAlso jmaArr(i - 1) <> 0 Then
                    slopeArr(i) = Math.Round((currentJMA / jmaArr(i - 1) - 1) * 100, 1)
                Else
                    slopeArr(i) = 0
                End If

                e0State(i) = e0
                e1State(i) = e1
                e2State(i) = e2
                dirState(i) = curDir

                prevJMA = currentJMA
                prevDir = curDir
            Next

            _e0 = e0
            _e1 = e1
            _e2 = e2
            _lastJMA = prevJMA
            _lastDirection = prevDir
            _calcCount = count
            _stateIndex = count - 1
            If count > 1 Then
                Dim prevIdx = count - 2
                _prevE0 = e0State(prevIdx)
                _prevE1 = e1State(prevIdx)
                _prevE2 = e2State(prevIdx)
                _prevJMA = jmaArr(prevIdx)
                _prevDirection = dirState(prevIdx)
            Else
                _prevE0 = Double.NaN
                _prevE1 = Double.NaN
                _prevE2 = Double.NaN
                _prevJMA = Double.NaN
                _prevDirection = 0
            End If

            For i = 0 To count - 1
                Dim r As New IndicatorResult With {
                    .Name = Name, .Index = i, .PanelIndex = 0,
                    .Values = New Dictionary(Of String, Single)}
                r.Values("Value") = CSng(jmaArr(i))
                r.Values("Up") = If(Double.IsNaN(upArr(i)), Single.NaN, CSng(upArr(i)))
                r.Values("Down") = If(Double.IsNaN(downArr(i)), Single.NaN, CSng(downArr(i)))
                r.Values("Slope") = CSng(slopeArr(i))
                results.Add(r)
            Next
            Return results
        End Function

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            Dim i = candles.Count - 1
            If i < 0 OrElse Double.IsNaN(_e0) OrElse _calcCount = 0 Then
                Dim full = Calculate(candles)
                If full.Count > 0 Then
                    Return full(full.Count - 1)
                End If
                Dim emptyR As New IndicatorResult With {.Name = Name, .Index = Math.Max(0, i), .PanelIndex = 0}
                emptyR.Values = New Dictionary(Of String, Single)
                emptyR.Values("Value") = Single.NaN
                Return emptyR
            End If
            If i <= 0 Then
                Dim full = Calculate(candles)
                If full.Count > 0 Then Return full(full.Count - 1)
                Dim emptyR As New IndicatorResult With {.Name = Name, .Index = Math.Max(0, i), .PanelIndex = 0}
                emptyR.Values = New Dictionary(Of String, Single)
                emptyR.Values("Value") = Single.NaN
                Return emptyR
            End If

            Dim src = CDbl(candles(i).Close)
            Dim phaseRatio = CalcPhaseRatio(_phase)
            Dim beta = 0.45 * (_period - 1) / (0.45 * (_period - 1) + 2)
            Dim alpha = Math.Pow(beta, _power)

            Dim lE0 As Double
            Dim lE1 As Double
            Dim lE2 As Double
            Dim lPrevJMA As Double
            Dim lPrevDir As Integer

            If _stateIndex = i Then
                lE0 = _prevE0
                lE1 = _prevE1
                lE2 = _prevE2
                lPrevJMA = _prevJMA
                lPrevDir = _prevDirection
            ElseIf _stateIndex = i - 1 Then
                lE0 = _e0
                lE1 = _e1
                lE2 = _e2
                lPrevJMA = _lastJMA
                lPrevDir = _lastDirection
                _prevE0 = lE0
                _prevE1 = lE1
                _prevE2 = lE2
                _prevJMA = lPrevJMA
                _prevDirection = lPrevDir
            Else
                Dim full = Calculate(candles)
                If full.Count > 0 Then Return full(full.Count - 1)
                Dim emptyR As New IndicatorResult With {.Name = Name, .Index = Math.Max(0, i), .PanelIndex = 0}
                emptyR.Values = New Dictionary(Of String, Single)
                emptyR.Values("Value") = Single.NaN
                Return emptyR
            End If
            If Double.IsNaN(lE0) OrElse Double.IsNaN(lPrevJMA) Then
                Dim full = Calculate(candles)
                If full.Count > 0 Then Return full(full.Count - 1)
                Dim emptyR As New IndicatorResult With {.Name = Name, .Index = Math.Max(0, i), .PanelIndex = 0}
                emptyR.Values = New Dictionary(Of String, Single)
                emptyR.Values("Value") = Single.NaN
                Return emptyR
            End If

            lE0 = (1 - alpha) * src + alpha * lE0
            lE1 = (src - lE0) * (1 - beta) + beta * lE1
            lE2 = (lE0 + phaseRatio * lE1 - lPrevJMA) * Math.Pow(1 - alpha, 2) + Math.Pow(alpha, 2) * lE2

            Dim currentJMA As Double
            If i < _period Then
                Dim sum As Double = 0
                For j = 0 To i
                    sum += CDbl(candles(j).Close)
                Next
                currentJMA = Math.Round(sum / (i + 1), 4)
            Else
                currentJMA = Math.Round(lE2 + lPrevJMA, 4)
            End If

            Dim curDir As Integer
            If currentJMA > lPrevJMA AndAlso Not Double.IsNaN(lPrevJMA) Then
                curDir = 1
            ElseIf currentJMA < lPrevJMA AndAlso Not Double.IsNaN(lPrevJMA) Then
                curDir = -1
            Else
                curDir = lPrevDir
            End If

            Dim upVal As Single = Single.NaN
            Dim downVal As Single = Single.NaN

            If curDir = 1 Then
                upVal = CSng(currentJMA)
            Else
                downVal = CSng(currentJMA)
            End If

            If curDir <> lPrevDir AndAlso lPrevDir <> 0 AndAlso prevResults IsNot Nothing AndAlso prevResults.Count > 1 Then
                Dim prevR As IndicatorResult = Nothing
                For k = prevResults.Count - 1 To 0 Step -1
                    If prevResults(k).Index = i - 1 Then prevR = prevResults(k) : Exit For
                Next
                Dim prvV = If(prevR Is Nothing, Single.NaN, prevR.Val("Value"))
                If Not Single.IsNaN(prvV) Then
                    prevR.Values("Up") = prvV
                    prevR.Values("Down") = prvV
                End If
            End If

            Dim slope As Single = 0
            If Not Double.IsNaN(lPrevJMA) AndAlso lPrevJMA <> 0 Then
                slope = CSng(Math.Round((currentJMA / lPrevJMA - 1) * 100, 1))
            End If

            _e0 = lE0
            _e1 = lE1
            _e2 = lE2
            _lastJMA = currentJMA
            _lastDirection = curDir
            _calcCount = Math.Max(_calcCount, i + 1)
            _stateIndex = i

            Dim r As New IndicatorResult With {
                .Name = Name, .Index = i, .PanelIndex = 0,
                .Values = New Dictionary(Of String, Single)}
            r.Values("Value") = CSng(currentJMA)
            r.Values("Up") = upVal
            r.Values("Down") = downVal
            r.Values("Slope") = slope
            Return r
        End Function

        Private Shared Function CalcPhaseRatio(phase As Integer) As Double
            If phase < -100 Then Return 0.5
            If phase > 100 Then Return 2.5
            Return phase / 100.0 + 1.5
        End Function
    End Class
End Namespace
