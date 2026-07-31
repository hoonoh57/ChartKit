Imports System.Net.Http
Imports System.Text.Json

Namespace DataSources
    Public NotInheritable Class CybosSymbolResolution
        Public Property StockName As String = ""
        Public Property Symbol As String = ""
        Public Property ErrorMessage As String = ""

        Public ReadOnly Property IsSuccess As Boolean
            Get
                Return Symbol.Length = 6 AndAlso Symbol.All(AddressOf Char.IsDigit)
            End Get
        End Property
    End Class

    Public NotInheritable Class CybosSymbolNameResolver
        Private Shared ReadOnly Client As New HttpClient With {
            .Timeout = TimeSpan.FromSeconds(20)}
        Private ReadOnly _baseUrl As String

        Public Sub New(Optional baseUrl As String = Nothing)
            _baseUrl = If(String.IsNullOrWhiteSpace(baseUrl),
                          EnvConfig.Get("CYBOS_API_URL", "http://localhost:8082"),
                          baseUrl).TrimEnd("/"c)
        End Sub

        Public Async Function ResolveAsync(stockName As String) As Task(Of CybosSymbolResolution)
            Dim result As New CybosSymbolResolution With {
                .StockName = If(stockName, "").Trim()}
            If result.StockName.Length = 0 Then
                result.ErrorMessage = "종목명이 비어 있습니다."
                Return result
            End If

            Dim url = _baseUrl & "/api/market/name_to_code?name=" &
                      Uri.EscapeDataString(result.StockName)
            Try
                Dim json = Await Client.GetStringAsync(url)
                Using document = JsonDocument.Parse(json)
                    Dim root = document.RootElement
                    If Not ReadSuccess(root) Then
                        result.ErrorMessage = ReadString(root, "Message", "message")
                        If result.ErrorMessage.Length = 0 Then result.ErrorMessage = "종목코드 미발견"
                        Return result
                    End If

                    Dim data As JsonElement
                    If Not TryProperty(root, data, "Data", "data") Then
                        result.ErrorMessage = "server32 응답에 Data가 없습니다."
                        Return result
                    End If
                    result.Symbol = NormalizeSymbol(ReadString(data, "code", "Code", "종목코드"))
                    If result.Symbol.Length <> 6 Then
                        result.Symbol = ""
                        result.ErrorMessage = "server32가 유효한 6자리 코드를 반환하지 않았습니다."
                    End If
                End Using
            Catch ex As Exception
                result.ErrorMessage = ex.Message
            End Try
            Return result
        End Function

        Private Shared Function NormalizeSymbol(value As String) As String
            Dim symbol = If(value, "").Trim()
            If symbol.StartsWith("A", StringComparison.OrdinalIgnoreCase) Then
                symbol = symbol.Substring(1)
            End If
            If symbol.Length = 6 AndAlso symbol.All(AddressOf Char.IsDigit) Then Return symbol
            Return ""
        End Function

        Private Shared Function ReadSuccess(element As JsonElement) As Boolean
            Dim value As JsonElement
            If Not TryProperty(element, value, "Success", "success") Then Return False
            If value.ValueKind = JsonValueKind.True Then Return True
            Dim parsed As Boolean
            Return Boolean.TryParse(value.ToString(), parsed) AndAlso parsed
        End Function

        Private Shared Function ReadString(element As JsonElement,
                                           ParamArray names() As String) As String
            Dim value As JsonElement
            If Not TryProperty(element, value, names) Then Return ""
            Return If(value.ValueKind = JsonValueKind.String,
                      If(value.GetString(), ""), value.ToString())
        End Function

        Private Shared Function TryProperty(element As JsonElement,
                                            ByRef value As JsonElement,
                                            ParamArray names() As String) As Boolean
            For Each name In names
                If element.TryGetProperty(name, value) Then Return True
            Next
            Return False
        End Function
    End Class
End Namespace
