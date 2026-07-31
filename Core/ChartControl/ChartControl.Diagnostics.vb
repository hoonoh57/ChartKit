Imports System.Linq
Imports System.Windows.Forms

Namespace Core
    Public Partial Class ChartControl
        Private Sub DumpChartDataCsv()
            If _candles Is Nothing OrElse _candles.Count = 0 Then Return

            Dim path As String
            Using dlg As New SaveFileDialog()
                dlg.Filter = "CSV 파일 (*.csv)|*.csv"
                dlg.FileName = "chartdump_" & DateTime.Now.ToString("yyyyMMdd_HHmmss") & ".csv"
                If dlg.ShowDialog() <> DialogResult.OK Then Return
                path = dlg.FileName
            End Using

            Dim res = _indicatorEngine.Results
            Dim indCols As New List(Of Tuple(Of String, String))
            For Each ind In _indicatorEngine.GetAll()
                Dim rlist As Models.IndicatorResultRingBuffer = Nothing
                If res.TryGetValue(ind.Name, rlist) AndAlso rlist IsNot Nothing AndAlso rlist.Count > 0 Then
                    Dim sample = rlist.FirstOrDefault(Function(x) x IsNot Nothing AndAlso x.Values IsNot Nothing AndAlso x.Values.Count > 0)
                    If sample IsNot Nothing Then
                        For Each k In sample.Values.Keys
                            indCols.Add(Tuple.Create(ind.Name, k))
                        Next
                    End If
                End If
            Next

            Dim sb As New Text.StringBuilder()
            Dim hdr As New List(Of String) From {"Index", "DateTime", "Open", "High", "Low", "Close", "Volume"}
            For Each c In indCols : hdr.Add(c.Item1 & "." & c.Item2) : Next
            For si = 0 To _signalRules.Count - 1
                Dim r = _signalRules(si)
                hdr.Add($"SIG{si}[{r.IndicatorA}x{r.IndicatorB},{If(r.CrossUp, "UP", "DN")},reqB={r.RequireBRising}]")
            Next
            sb.AppendLine(String.Join(",", hdr))

            Dim getVal = Function(indName As String, key As String, i As Integer) As Single
                             Dim rlist As Models.IndicatorResultRingBuffer = Nothing
                             If Not res.TryGetValue(indName, rlist) OrElse rlist Is Nothing OrElse i < 0 OrElse i >= rlist.Count Then Return Single.NaN
                             Dim rr = rlist(i)
                             If rr Is Nothing OrElse rr.Values Is Nothing Then Return Single.NaN
                             Dim v As Single
                             Return If(rr.Values.TryGetValue(key, v), v, Single.NaN)
                         End Function
            Dim valOf = Function(indName As String, i As Integer) As Single
                            Dim rlist As Models.IndicatorResultRingBuffer = Nothing
                            If Not res.TryGetValue(indName, rlist) OrElse rlist Is Nothing OrElse i < 0 OrElse i >= rlist.Count Then Return Single.NaN
                            Dim rr = rlist(i)
                            If rr Is Nothing OrElse rr.Values Is Nothing Then Return Single.NaN
                            Dim v As Single
                            If rr.Values.TryGetValue("Value", v) Then Return v
                            For Each kv In rr.Values
                                If Not Single.IsNaN(kv.Value) Then Return kv.Value
                            Next
                            Return Single.NaN
                        End Function

            For i = 0 To _candles.Count - 1
                Dim c = _candles(i)
                Dim row As New List(Of String) From {
                    i.ToString(), c.Dt.ToString("yyyy-MM-dd HH:mm"), c.Open.ToString("0.###"),
                    c.High.ToString("0.###"), c.Low.ToString("0.###"), c.Close.ToString("0.###"),
                    c.Volume.ToString()}
                For Each col In indCols
                    Dim v = getVal(col.Item1, col.Item2, i)
                    row.Add(If(Single.IsNaN(v), "", v.ToString("0.####")))
                Next
                For Each r In _signalRules
                    Dim hitStr = ""
                    If i >= 1 Then
                        Dim a0 = valOf(r.IndicatorA, i - 1)
                        Dim b0 = valOf(r.IndicatorB, i - 1)
                        Dim a1 = valOf(r.IndicatorA, i)
                        Dim b1 = valOf(r.IndicatorB, i)
                        If Not (Single.IsNaN(a0) OrElse Single.IsNaN(b0) OrElse Single.IsNaN(a1) OrElse Single.IsNaN(b1)) Then
                            Dim cu = a0 <= b0 AndAlso a1 > b1
                            Dim cd = a0 >= b0 AndAlso a1 < b1
                            Dim cross = If(r.CrossUp, cu, cd)
                            Dim hit = cross
                            Dim brise = b1 > b0
                            If hit AndAlso r.RequireBRising AndAlso Not brise Then hit = False
                            hitStr = $"hit={If(hit, 1, 0)};cross={If(cross, 1, 0)};Brise={If(brise, 1, 0)};b1-b0={(b1 - b0):0.###}"
                        End If
                    End If
                    row.Add("""" & hitStr & """")
                Next
                sb.AppendLine(String.Join(",", row))
            Next

            IO.File.WriteAllText(path, sb.ToString(), New Text.UTF8Encoding(True))
            MessageBox.Show("CSV 저장 완료:" & Environment.NewLine & path, "차트 데이터 출력")
        End Sub
    End Class
End Namespace
