Imports System.Globalization
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions

Namespace Core.Backtesting
    Public NotInheritable Class CaptureRecord
        Public Property Symbol As String = ""
        Public Property CapturedAt As DateTime?
    End Class

    Public NotInheritable Class CaptureListParser
        Private Shared ReadOnly DateFormats As String() = {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd HH:mm", "yyyyMMddHHmmss", "yyyyMMddHHmm",
            "HH:mm:ss", "HH:mm", "HHmmss", "HHmm"}

        Private Sub New()
        End Sub

        Public Shared Function ParseFile(path As String,
                                         fallbackDate As DateTime) As List(Of CaptureRecord)
            If String.IsNullOrWhiteSpace(path) Then Throw New ArgumentException(NameOf(path))
            Return ParseLines(File.ReadAllLines(path, DetectEncoding(path)), fallbackDate)
        End Function

        Public Shared Function ParseLines(lines As IEnumerable(Of String),
                                          fallbackDate As DateTime) As List(Of CaptureRecord)
            Dim events = ParseEvents(lines, fallbackDate)
            Dim bySymbol As New Dictionary(Of String, CaptureRecord)(StringComparer.Ordinal)
            For Each item In events
                If Not bySymbol.ContainsKey(item.Symbol) Then
                    bySymbol(item.Symbol) = item
                ElseIf Not bySymbol(item.Symbol).CapturedAt.HasValue AndAlso item.CapturedAt.HasValue Then
                    bySymbol(item.Symbol).CapturedAt = item.CapturedAt
                End If
            Next
            Return bySymbol.Values.ToList()
        End Function

        Public Shared Function ParseEventFile(path As String,
                                              fallbackDate As DateTime) As List(Of CaptureRecord)
            If String.IsNullOrWhiteSpace(path) Then Throw New ArgumentException(NameOf(path))
            Return ParseEvents(File.ReadAllLines(path, DetectEncoding(path)), fallbackDate)
        End Function

        Public Shared Function ParseEvents(lines As IEnumerable(Of String),
                                           fallbackDate As DateTime) As List(Of CaptureRecord)
            Dim output As New List(Of CaptureRecord)()
            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
            For Each rawLine In lines
                If String.IsNullOrWhiteSpace(rawLine) Then Continue For
                Dim fields = SplitFields(rawLine)
                Dim symbol = fields.Select(AddressOf NormalizeSymbol).
                    FirstOrDefault(Function(x) x.Length = 6)
                If String.IsNullOrEmpty(symbol) Then Continue For
                Dim captured As DateTime? = Nothing
                For Each field In fields
                    Dim parsed As DateTime
                    If TryParseCapturedAt(field, fallbackDate, parsed) Then
                        captured = parsed
                        Exit For
                    End If
                Next
                Dim key = symbol & "|" & If(captured.HasValue,
                    captured.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), "")
                If seen.Add(key) Then
                    output.Add(New CaptureRecord With {.Symbol = symbol, .CapturedAt = captured})
                End If
            Next
            Return output
        End Function

        Private Shared Function NormalizeSymbol(value As String) As String
            Dim text = value.Trim().Trim(""""c)
            If Regex.IsMatch(text, "^\d{6}$") Then Return text
            Dim match = Regex.Match(text, "(?<!\d)(\d{6})(?!\d)")
            Return If(match.Success, match.Groups(1).Value, "")
        End Function

        Private Shared Function TryParseCapturedAt(value As String, fallbackDate As DateTime,
                                                   ByRef result As DateTime) As Boolean
            Dim text = value.Trim().Trim(""""c)
            For Each dateFormat In DateFormats
                Dim parsed As DateTime
                If DateTime.TryParseExact(text, dateFormat, CultureInfo.InvariantCulture,
                                          DateTimeStyles.AllowWhiteSpaces, parsed) Then
                    If dateFormat.StartsWith("H", StringComparison.Ordinal) Then
                        result = fallbackDate.Date.Add(parsed.TimeOfDay)
                    Else
                        result = parsed
                    End If
                    Return True
                End If
            Next
            Return False
        End Function

        Private Shared Function SplitFields(line As String) As List(Of String)
            Dim output As New List(Of String)()
            Dim current As New StringBuilder()
            Dim quoted = False
            For i = 0 To line.Length - 1
                Dim ch = line(i)
                If ch = """"c Then
                    If quoted AndAlso i + 1 < line.Length AndAlso line(i + 1) = """"c Then
                        current.Append(ch) : i += 1
                    Else
                        quoted = Not quoted
                    End If
                ElseIf Not quoted AndAlso (ch = ","c OrElse ch = ControlChars.Tab OrElse ch = ";"c) Then
                    output.Add(current.ToString()) : current.Clear()
                Else
                    current.Append(ch)
                End If
            Next
            output.Add(current.ToString())
            Return output
        End Function

        Private Shared Function DetectEncoding(path As String) As Encoding
            Using stream = File.OpenRead(path)
                Dim bom(2) As Byte
                Dim read = stream.Read(bom, 0, bom.Length)
                If read >= 3 AndAlso bom(0) = &HEF AndAlso bom(1) = &HBB AndAlso bom(2) = &HBF Then
                    Return New UTF8Encoding(True)
                End If
                If read >= 2 AndAlso bom(0) = &HFF AndAlso bom(1) = &HFE Then Return Encoding.Unicode
            End Using
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
            Return Encoding.GetEncoding(949)
        End Function
    End Class
End Namespace
