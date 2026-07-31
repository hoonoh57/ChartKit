Imports System.Text
Imports System.Text.RegularExpressions

Namespace Core.Backtesting
    Public NotInheritable Class KiwoomPerformanceRow
        Public Property StockName As String = ""
        Public Property Return1Minute As String = ""
        Public Property Return3Minutes As String = ""
        Public Property ReturnPeriod As String = ""
        Public Property MaximumReturn As String = ""
        Public Property Volume As String = ""
        Public Property Extra As String = ""
    End Class

    Public NotInheritable Class KiwoomPerformanceClipboardParser
        Private Shared ReadOnly PercentPattern As New Regex(
            "^[+-]?(?:\d+(?:\.\d+)?|\.\d+)%$",
            RegexOptions.Compiled Or RegexOptions.CultureInvariant)

        Private Sub New()
        End Sub

        Public Shared Function Parse(text As String) As List(Of KiwoomPerformanceRow)
            Dim output As New List(Of KiwoomPerformanceRow)()
            If String.IsNullOrWhiteSpace(text) Then Return output

            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
            For Each rawLine In text.Replace(ControlChars.CrLf, ControlChars.Lf).
                                      Replace(ControlChars.Cr, ControlChars.Lf).
                                      Split(ControlChars.Lf)
                If String.IsNullOrWhiteSpace(rawLine) Then Continue For
                Dim fields = SplitTabFields(rawLine).
                    Select(AddressOf CleanField).
                    Where(Function(value) value.Length > 0).
                    ToList()
                If fields.Count < 5 Then Continue For

                Dim percentIndex = fields.FindIndex(
                    Function(value) PercentPattern.IsMatch(value.Replace(" ", "")))
                If percentIndex <= 0 Then Continue For

                Dim stockName = fields(percentIndex - 1).Trim()
                If stockName.Length = 0 OrElse stockName = "종목명" Then Continue For
                If Not seen.Add(stockName) Then Continue For

                Dim values = fields.Skip(percentIndex).ToList()
                output.Add(New KiwoomPerformanceRow With {
                    .StockName = stockName,
                    .Return1Minute = ValueAt(values, 0),
                    .Return3Minutes = ValueAt(values, 1),
                    .ReturnPeriod = ValueAt(values, 2),
                    .MaximumReturn = ValueAt(values, 3),
                    .Volume = ValueAt(values, 4),
                    .Extra = ValueAt(values, 5)})
            Next
            Return output
        End Function

        Private Shared Function SplitTabFields(line As String) As List(Of String)
            Dim output As New List(Of String)()
            Dim current As New StringBuilder()
            Dim quoted = False
            For index = 0 To line.Length - 1
                Dim ch = line(index)
                If ch = """"c Then
                    If quoted AndAlso index + 1 < line.Length AndAlso line(index + 1) = """"c Then
                        current.Append(ch)
                        index += 1
                    Else
                        quoted = Not quoted
                    End If
                ElseIf ch = ControlChars.Tab AndAlso Not quoted Then
                    output.Add(current.ToString())
                    current.Clear()
                Else
                    current.Append(ch)
                End If
            Next
            output.Add(current.ToString())
            Return output
        End Function

        Private Shared Function CleanField(value As String) As String
            Return value.Trim().Trim(""""c).Trim()
        End Function

        Private Shared Function ValueAt(values As IReadOnlyList(Of String), index As Integer) As String
            If index < 0 OrElse index >= values.Count Then Return ""
            Return values(index)
        End Function
    End Class
End Namespace
