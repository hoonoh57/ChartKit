Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Indicators
    '' SuperTrend_Indicator — ATR 기반 추세 추종. 오버레이(방향색). 원본 로직 그대로.
    Public Class SuperTrend_Indicator
        Implements IIndicator

        Private _atrPeriod As Integer = 10
        Private _multiplier As Single = 3.0F
        Private _params As New Dictionary(Of String, Object) From {{"AtrPeriod", 10}, {"Multiplier", 3.0F}}
        Private _stateATR As Single = Single.NaN
        Private _stateUpperBand As Single = Single.NaN
        Private _stateLowerBand As Single = Single.NaN
        Private _stateSuperTrend As Single = Single.NaN
        Private _stateDirection As Integer = 1
        Private _prevStateATR As Single = Single.NaN
        Private _prevStateUpperBand As Single = Single.NaN
        Private _prevStateLowerBand As Single = Single.NaN
        Private _prevStateSuperTrend As Single = Single.NaN
        Private _prevStateDirection As Integer = 1
        Private _stateIndex As Integer = -1

        Public Sub New(Optional atrPeriod As Integer = 10, Optional multiplier As Single = 3.0F)
            _atrPeriod = atrPeriod
            _multiplier = multiplier
            _params("AtrPeriod") = _atrPeriod
            _params("Multiplier") = _multiplier
        End Sub

        Public ReadOnly Property Name As String Implements IIndicator.Name
            Get
                Return $"ST_{_atrPeriod}_{_multiplier:F1}"
            End Get
        End Property
        Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
            Get
                Return $"SuperTrend({_atrPeriod},{_multiplier:F1})"
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
                If _params.ContainsKey("AtrPeriod") Then _atrPeriod = CInt(_params("AtrPeriod"))
                If _params.ContainsKey("Multiplier") Then _multiplier = CSng(_params("Multiplier"))
            End Set
        End Property

        Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
            Dim count = candles.Count
            Dim results As New List(Of IndicatorResult)(count)
            If count = 0 Then Return results
            Dim tr(count - 1) As Single
            Dim atr(count - 1) As Single
            tr(0) = candles(0).High - candles(0).Low
            For i = 1 To count - 1
                Dim c = candles(i)
                Dim pc = candles(i - 1).Close
                Dim v1 = c.High - c.Low
                Dim v2 = Math.Abs(c.High - pc)
                Dim v3 = Math.Abs(c.Low - pc)
                tr(i) = Math.Max(v1, Math.Max(v2, v3))
            Next
            Dim atrSum As Single = 0
            For i = 0 To count - 1
                atrSum += tr(i)
                If i < _atrPeriod - 1 Then
                    atr(i) = Single.NaN
                ElseIf i = _atrPeriod - 1 Then
                    atr(i) = atrSum / _atrPeriod
                Else
                    atr(i) = (atr(i - 1) * (_atrPeriod - 1) + tr(i)) / _atrPeriod
                End If
            Next
            Dim ub(count - 1) As Single
            Dim lb(count - 1) As Single
            Dim st(count - 1) As Single
            Dim dir(count - 1) As Integer
            Dim stUp(count - 1) As Single
            Dim stDown(count - 1) As Single
            For i = 0 To count - 1
                If Single.IsNaN(atr(i)) Then
                    ub(i) = Single.NaN
                    lb(i) = Single.NaN
                    st(i) = Single.NaN
                    dir(i) = 1
                    stUp(i) = Single.NaN
                    stDown(i) = Single.NaN
                    Continue For
                End If
                Dim hl2 = (candles(i).High + candles(i).Low) / 2.0F
                Dim bU = hl2 + _multiplier * atr(i)
                Dim bL = hl2 - _multiplier * atr(i)
                If i = 0 OrElse Single.IsNaN(ub(i - 1)) Then
                    ub(i) = bU
                Else
                    If bU < ub(i - 1) OrElse candles(i - 1).Close > ub(i - 1) Then
                        ub(i) = bU
                    Else
                        ub(i) = ub(i - 1)
                    End If
                End If
                If i = 0 OrElse Single.IsNaN(lb(i - 1)) Then
                    lb(i) = bL
                Else
                    If bL > lb(i - 1) OrElse candles(i - 1).Close < lb(i - 1) Then
                        lb(i) = bL
                    Else
                        lb(i) = lb(i - 1)
                    End If
                End If
                If i = 0 Then
                    If candles(i).Close > ub(i) Then dir(i) = 1 Else dir(i) = -1
                Else
                    If dir(i - 1) = 1 Then
                        If candles(i).Close < lb(i) Then dir(i) = -1 Else dir(i) = 1
                    Else
                        If candles(i).Close > ub(i) Then dir(i) = 1 Else dir(i) = -1
                    End If
                End If
                If dir(i) = 1 Then st(i) = lb(i) Else st(i) = ub(i)
                If st(i) <= 0 AndAlso i > 0 AndAlso Not Single.IsNaN(st(i - 1)) Then
                    st(i) = st(i - 1)
                End If
                If dir(i) = 1 Then
                    stUp(i) = st(i)
                    stDown(i) = Single.NaN
                Else
                    stDown(i) = st(i)
                    stUp(i) = Single.NaN
                End If
                If i > 0 AndAlso dir(i) <> dir(i - 1) AndAlso Not Single.IsNaN(st(i - 1)) Then
                    If dir(i) = 1 Then stUp(i - 1) = st(i - 1) Else stDown(i - 1) = st(i - 1)
                End If
            Next
            If count > 0 Then
                Dim lastIndex = count - 1
                _stateATR = atr(lastIndex)
                _stateUpperBand = ub(lastIndex)
                _stateLowerBand = lb(lastIndex)
                _stateSuperTrend = st(lastIndex)
                _stateDirection = dir(lastIndex)
                _stateIndex = lastIndex
                If count > 1 Then
                    _prevStateATR = atr(lastIndex - 1)
                    _prevStateUpperBand = ub(lastIndex - 1)
                    _prevStateLowerBand = lb(lastIndex - 1)
                    _prevStateSuperTrend = st(lastIndex - 1)
                    _prevStateDirection = dir(lastIndex - 1)
                Else
                    _prevStateATR = Single.NaN
                    _prevStateUpperBand = Single.NaN
                    _prevStateLowerBand = Single.NaN
                    _prevStateSuperTrend = Single.NaN
                    _prevStateDirection = 1
                End If
            End If
            For i = 0 To count - 1
                Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                    .Values = New Dictionary(Of String, Single)}
                r.Values("Value") = st(i)
                r.Values("Up") = stUp(i)
                r.Values("Down") = stDown(i)
                r.Values("Direction") = CSng(dir(i))
                r.Values("ATR") = atr(i)
                results.Add(r)
            Next
            Return results
        End Function

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem), prevResults As List(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            Dim i = candles.Count - 1
            If i < _atrPeriod OrElse Single.IsNaN(_stateATR) Then
                Dim full = Calculate(candles)
                If full.Count > 0 Then Return full(full.Count - 1)
                Return New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                    .Values = New Dictionary(Of String, Single) From {{"Value", Single.NaN}}}
            End If
            If i <= 0 Then
                Dim full = Calculate(candles)
                If full.Count > 0 Then Return full(full.Count - 1)
                Return New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                    .Values = New Dictionary(Of String, Single) From {{"Value", Single.NaN}}}
            End If

            Dim baseATR As Single
            Dim baseUpper As Single
            Dim baseLower As Single
            Dim baseSuperTrend As Single
            Dim baseDirection As Integer

            If _stateIndex = i Then
                baseATR = _prevStateATR
                baseUpper = _prevStateUpperBand
                baseLower = _prevStateLowerBand
                baseSuperTrend = _prevStateSuperTrend
                baseDirection = _prevStateDirection
            ElseIf _stateIndex = i - 1 Then
                baseATR = _stateATR
                baseUpper = _stateUpperBand
                baseLower = _stateLowerBand
                baseSuperTrend = _stateSuperTrend
                baseDirection = _stateDirection
                _prevStateATR = baseATR
                _prevStateUpperBand = baseUpper
                _prevStateLowerBand = baseLower
                _prevStateSuperTrend = baseSuperTrend
                _prevStateDirection = baseDirection
            Else
                Dim full = Calculate(candles)
                If full.Count > 0 Then Return full(full.Count - 1)
                Return New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                    .Values = New Dictionary(Of String, Single) From {{"Value", Single.NaN}}}
            End If
            If Single.IsNaN(baseATR) OrElse Single.IsNaN(baseUpper) OrElse Single.IsNaN(baseLower) Then
                Dim full = Calculate(candles)
                If full.Count > 0 Then Return full(full.Count - 1)
                Return New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                    .Values = New Dictionary(Of String, Single) From {{"Value", Single.NaN}}}
            End If

            Dim c = candles(i)
            Dim prevClose = candles(i - 1).Close
            Dim v1 = c.High - c.Low
            Dim v2 = Math.Abs(c.High - prevClose)
            Dim v3 = Math.Abs(c.Low - prevClose)
            Dim trVal = Math.Max(v1, Math.Max(v2, v3))
            Dim curATR = (baseATR * (_atrPeriod - 1) + trVal) / _atrPeriod
            Dim hl2 = (c.High + c.Low) / 2.0F
            Dim curUB = hl2 + _multiplier * curATR
            Dim curLB = hl2 - _multiplier * curATR
            If curUB < baseUpper OrElse prevClose > baseUpper Then curUB = curUB Else curUB = baseUpper
            If curLB > baseLower OrElse prevClose < baseLower Then curLB = curLB Else curLB = baseLower
            Dim curDir As Integer
            If baseDirection = 1 Then
                If c.Close < curLB Then curDir = -1 Else curDir = 1
            Else
                If c.Close > curUB Then curDir = 1 Else curDir = -1
            End If
            Dim curST = If(curDir = 1, curLB, curUB)
            If curST <= 0 AndAlso Not Single.IsNaN(baseSuperTrend) Then curST = baseSuperTrend
            Dim upVal As Single = Single.NaN
            Dim downVal As Single = Single.NaN
            If curDir = 1 Then upVal = curST Else downVal = curST
            If curDir <> baseDirection AndAlso prevResults IsNot Nothing AndAlso prevResults.Count > 1 Then
                Dim prevR = prevResults(prevResults.Count - 2)
                Dim prevSTV = prevR.Val("Value")
                If Not Single.IsNaN(prevSTV) Then
                    If curDir = 1 Then prevR.Values("Up") = prevSTV Else prevR.Values("Down") = prevSTV
                End If
            End If
            _stateATR = curATR
            _stateUpperBand = curUB
            _stateLowerBand = curLB
            _stateSuperTrend = curST
            _stateDirection = curDir
            _stateIndex = i
            Dim r As New IndicatorResult With {.Name = Name, .Index = i, .PanelIndex = 0,
                .Values = New Dictionary(Of String, Single)}
            r.Values("Value") = curST
            r.Values("Up") = upVal
            r.Values("Down") = downVal
            r.Values("Direction") = CSng(curDir)
            r.Values("ATR") = curATR
            Return r
        End Function
    End Class
End Namespace
