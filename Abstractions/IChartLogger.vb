Namespace Abstractions
    Public Interface IChartLogger
        Sub Info(message As String)
        Sub Warning(message As String, Optional exception As Exception = Nothing)
        Sub [Error](message As String, exception As Exception)
    End Interface
End Namespace
