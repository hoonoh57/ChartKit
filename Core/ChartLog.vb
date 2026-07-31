Imports System.Diagnostics
Imports ChartKit.Abstractions

Namespace Core
    Public NotInheritable Class TraceChartLogger
        Implements IChartLogger

        Public Sub Info(message As String) Implements IChartLogger.Info
            Trace.TraceInformation(message)
        End Sub

        Public Sub Warning(message As String, Optional exception As Exception = Nothing) Implements IChartLogger.Warning
            Trace.TraceWarning(FormatMessage(message, exception))
        End Sub

        Public Sub [Error](message As String, exception As Exception) Implements IChartLogger.Error
            Trace.TraceError(FormatMessage(message, exception))
        End Sub

        Private Shared Function FormatMessage(message As String, exception As Exception) As String
            Return If(exception Is Nothing, message, message & Environment.NewLine & exception.ToString())
        End Function
    End Class

    Public NotInheritable Class ChartLog
        Private Shared _logger As IChartLogger = New TraceChartLogger()

        Private Sub New()
        End Sub

        Public Shared Property Logger As IChartLogger
            Get
                Return _logger
            End Get
            Set(value As IChartLogger)
                _logger = If(value, New TraceChartLogger())
            End Set
        End Property

        Public Shared Sub Info(message As String)
            _logger.Info(message)
        End Sub

        Public Shared Sub Warning(message As String, exception As Exception)
            _logger.Warning(message, exception)
        End Sub

        Public Shared Sub [Error](message As String, exception As Exception)
            _logger.Error(message, exception)
        End Sub
    End Class
End Namespace
