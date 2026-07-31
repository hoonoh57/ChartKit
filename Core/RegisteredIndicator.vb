Option Strict On
Option Explicit On
Option Infer Off

Imports ChartKit.Abstractions
Imports ChartKit.Models

Namespace Core
    Friend NotInheritable Class RegisteredIndicator
        Implements IIndicator
        Implements IIndicatorIdentity

        Private ReadOnly _source As IIndicator
        Private _registeredInstanceId As String

        Public Sub New(source As IIndicator)
            If source Is Nothing Then Throw New ArgumentNullException(NameOf(source))
            _source = source
            _registeredInstanceId = IndicatorIdentity.CreateInstanceId(_source)
        End Sub

        Friend ReadOnly Property SourceIndicator As IIndicator
            Get
                Return _source
            End Get
        End Property

        Friend ReadOnly Property RegisteredInstanceId As String
            Get
                Return _registeredInstanceId
            End Get
        End Property

        Friend Sub MarkRegistered()
            _registeredInstanceId = InstanceId
        End Sub

        Public ReadOnly Property DefinitionId As String Implements IIndicatorIdentity.DefinitionId
            Get
                Return IndicatorIdentity.DefinitionId(_source)
            End Get
        End Property

        Public ReadOnly Property InstanceId As String Implements IIndicatorIdentity.InstanceId
            Get
                Return IndicatorIdentity.CreateInstanceId(_source)
            End Get
        End Property

        Public ReadOnly Property LegacyName As String Implements IIndicatorIdentity.LegacyName
            Get
                Return _source.Name
            End Get
        End Property

        Public ReadOnly Property Name As String Implements IIndicator.Name
            Get
                Return InstanceId
            End Get
        End Property

        Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
            Get
                Return _source.DisplayName
            End Get
        End Property

        Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
            Get
                Return _source.PanelIndex
            End Get
        End Property

        Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
            Get
                Return _source.Parameters
            End Get
            Set(value As Dictionary(Of String, Object))
                _source.Parameters = value
            End Set
        End Property

        Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
            Dim results As List(Of IndicatorResult) = _source.Calculate(candles)
            NormalizeResults(results)
            Return results
        End Function

        Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem),
                                   prevResults As IReadOnlyList(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
            Dim result As IndicatorResult = _source.UpdateLast(candles, prevResults)
            NormalizeResult(result)
            Return result
        End Function

        Private Sub NormalizeResults(results As IEnumerable(Of IndicatorResult))
            If results Is Nothing Then Return
            For Each result As IndicatorResult In results
                NormalizeResult(result)
            Next
        End Sub

        Private Sub NormalizeResult(result As IndicatorResult)
            If result Is Nothing Then Return
            result.Name = InstanceId
        End Sub
    End Class
End Namespace
