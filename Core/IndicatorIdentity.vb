Option Strict On
Option Explicit On
Option Infer Off

Imports System.Globalization
Imports System.Text
Imports ChartKit.Abstractions

Namespace Core
    Public Interface IIndicatorIdentity
        ReadOnly Property DefinitionId As String
        ReadOnly Property InstanceId As String
        ReadOnly Property LegacyName As String
    End Interface

    Public NotInheritable Class IndicatorIdentity
        Private Sub New()
        End Sub

        Public Shared Function DefinitionId(indicator As IIndicator) As String
            If indicator Is Nothing Then Throw New ArgumentNullException(NameOf(indicator))

            Dim typeName As String = indicator.GetType().Name
            Const suffix As String = "_Indicator"
            If typeName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) Then
                typeName = typeName.Substring(0, typeName.Length - suffix.Length)
            End If

            Return typeName.ToUpperInvariant()
        End Function

        Public Shared Function CreateInstanceId(indicator As IIndicator) As String
            If indicator Is Nothing Then Throw New ArgumentNullException(NameOf(indicator))

            Dim builder As New StringBuilder()
            builder.Append(DefinitionId(indicator))
            builder.Append("[")

            Dim parameters = indicator.Parameters
            If parameters IsNot Nothing AndAlso parameters.Count > 0 Then
                Dim keys As New List(Of String)(parameters.Keys)
                keys.Sort(StringComparer.Ordinal)

                For index As Integer = 0 To keys.Count - 1
                    If index > 0 Then builder.Append(";")
                    Dim key As String = keys(index)
                    builder.Append(EscapeComponent(key))
                    builder.Append("=")
                    builder.Append(CanonicalValue(parameters(key)))
                Next
            End If

            builder.Append("]")
            Return builder.ToString()
        End Function

        Public Shared Function CreateSeriesId(instanceId As String,
                                              seriesKey As String) As String
            If String.IsNullOrWhiteSpace(instanceId) Then
                Throw New ArgumentException("지표 InstanceId가 필요합니다.", NameOf(instanceId))
            End If
            If String.IsNullOrWhiteSpace(seriesKey) Then
                Throw New ArgumentException("지표 series key가 필요합니다.", NameOf(seriesKey))
            End If

            Return instanceId & "::" & EscapeComponent(seriesKey)
        End Function

        Public Shared Function SourceTypeName(indicator As IIndicator) As String
            If indicator Is Nothing Then Return ""

            Dim registration = TryCast(indicator, RegisteredIndicator)
            Dim source As IIndicator = If(registration Is Nothing,
                                          indicator,
                                          registration.SourceIndicator)
            Return source.GetType().AssemblyQualifiedName
        End Function

        Private Shared Function CanonicalValue(value As Object) As String
            If value Is Nothing Then Return "null"

            If TypeOf value Is Boolean Then
                Return If(DirectCast(value, Boolean), "true", "false")
            End If

            If TypeOf value Is DateTime Then
                Return DirectCast(value, DateTime).ToString("O", CultureInfo.InvariantCulture)
            End If

            Dim formattable = TryCast(value, IFormattable)
            If formattable IsNot Nothing Then
                Return EscapeComponent(formattable.ToString(Nothing, CultureInfo.InvariantCulture))
            End If

            Return EscapeComponent(Convert.ToString(value, CultureInfo.InvariantCulture))
        End Function

        Private Shared Function EscapeComponent(value As String) As String
            If value Is Nothing Then Return ""

            Return value.Replace("\", "\\").
                         Replace(";", "\;").
                         Replace("=", "\=").
                         Replace("[", "\[").
                         Replace("]", "\]").
                         Replace(":", "\:")
        End Function
    End Class
End Namespace
