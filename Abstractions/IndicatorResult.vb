Option Strict On
Option Explicit On
Option Infer Off

Imports System.Linq

Namespace Abstractions
    Public Enum SeriesKind
        Line
        Histogram
        Baseline
        Meta
    End Enum

    '' 지표 계산 결과 (캔들 1개 대응). 원본 IndicatorResult 그대로.
    Public Class IndicatorResult
        Public Property Name As String = ""
        Public Property Index As Integer = 0
        '' 링버퍼 퇴출과 무관하게 증가하는 캔들 대응 번호.
        Public Property Sequence As Long = -1
        Public Property PanelIndex As Integer = 0
        Public Property Values As New Dictionary(Of String, Single)
        Public Property SeriesKinds As New Dictionary(Of String, SeriesKind)

        Public Function KindOf(key As String) As SeriesKind
            Dim kind As SeriesKind
            If SeriesKinds IsNot Nothing AndAlso SeriesKinds.TryGetValue(key, kind) Then Return kind
            If String.IsNullOrEmpty(key) Then Return SeriesKind.Line

            If String.Equals(key, "Hist", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(key, "Histogram", StringComparison.OrdinalIgnoreCase) Then
                Return SeriesKind.Histogram
            End If

            If String.Equals(key, "Upper", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(key, "Lower", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(key, "Baseline", StringComparison.OrdinalIgnoreCase) Then
                Return SeriesKind.Baseline
            End If

            If String.Equals(key, "Direction", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(key, "MA", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(key, "ATR", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(key, "Slope", StringComparison.OrdinalIgnoreCase) Then
                Return SeriesKind.Meta
            End If

            Return SeriesKind.Line
        End Function

        Public Function Val(key As String) As Single
            If Values IsNot Nothing AndAlso Values.ContainsKey(key) Then Return Values(key)
            Return Single.NaN
        End Function

        Public Overrides Function ToString() As String
            If Values Is Nothing OrElse Values.Count = 0 Then Return $"{Name}[{Index}] (empty)"
            Dim items As String = String.Join(", ", Values.Select(Function(kv) $"{kv.Key}={kv.Value:F2}"))
            Return $"{Name}[{Index}] {items}"
        End Function
    End Class
End Namespace
