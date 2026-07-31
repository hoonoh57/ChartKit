Namespace Core.Signals
    Public NotInheritable Class TrendQualificationOptions
        Public Property MinimumEntryScore As Integer = QualifiedTrendRangeEvaluator.MinimumEntryScore
        Public Property ConfirmationBars As Integer = 2

        Public Function Clone() As TrendQualificationOptions
            Return New TrendQualificationOptions With {
                .MinimumEntryScore = MinimumEntryScore,
                .ConfirmationBars = ConfirmationBars}
        End Function
    End Class
End Namespace
