Namespace Core.Backtesting
    Public NotInheritable Class WalkForwardEngine
        Private ReadOnly _sweep As New ParameterSweepEngine()

        Public Function Evaluate(cases As IEnumerable(Of BacktestCase),
                                 candidates As IEnumerable(Of StrategyParameterSet),
                                 options As WalkForwardOptions) As WalkForwardResult
            If cases Is Nothing Then Throw New ArgumentNullException(NameOf(cases))
            If candidates Is Nothing Then Throw New ArgumentNullException(NameOf(candidates))
            If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
            If options.TrainingDateCount < 1 OrElse options.TestDateCount < 1 OrElse
               options.StepDateCount < 1 Then
                Throw New ArgumentException("학습·검증·이동 날짜 수는 모두 1 이상이어야 합니다.")
            End If
            If options.Stability Is Nothing Then options.Stability = New StabilitySelectionOptions()
            Dim stabilityValidation = options.Stability.Validate()
            If stabilityValidation.Length > 0 Then
                Throw New ArgumentException(stabilityValidation, NameOf(options))
            End If
            If options.Portfolio Is Nothing Then options.Portfolio = New PortfolioSimulationOptions()
            Dim portfolioValidation = options.Portfolio.Validate()
            If portfolioValidation.Length > 0 Then
                Throw New ArgumentException(portfolioValidation, NameOf(options))
            End If

            Dim caseList = cases.OrderBy(Function(x) x.CapturedAt).
                ThenBy(Function(x) x.CaseId, StringComparer.Ordinal).ToList()
            Dim candidateList = candidates.Select(Function(x) x.Clone()).ToList()
            If candidateList.Count = 0 Then Throw New ArgumentException("검증할 파라미터가 없습니다.")
            Dim dates = caseList.Select(Function(x) x.CapturedAt.Date).Distinct().OrderBy(Function(x) x).ToList()
            Dim required = options.TrainingDateCount + options.TestDateCount
            If dates.Count < required Then
                Throw New InvalidOperationException(
                    $"워크포워드에는 최소 {required}개 포착 날짜가 필요하지만 현재 {dates.Count}개입니다.")
            End If

            Dim output As New WalkForwardResult()
            Dim start = 0
            Dim fold = 1
            While start + required <= dates.Count
                Dim trainDates = dates.Skip(start).Take(options.TrainingDateCount).ToHashSet()
                Dim testDates = dates.Skip(start + options.TrainingDateCount).
                    Take(options.TestDateCount).ToHashSet()
                Dim trainingCases = caseList.Where(Function(x) trainDates.Contains(x.CapturedAt.Date)).ToList()
                Dim testCases = caseList.Where(Function(x) testDates.Contains(x.CapturedAt.Date)).ToList()
                Dim trainingRank = _sweep.EvaluateCases(
                    trainingCases, candidateList, options.Stability, options.Portfolio)
                Dim selected = trainingRank(0).Parameters.Clone()
                Dim testSummary = _sweep.EvaluateCases(
                    testCases, New StrategyParameterSet() {selected}, Nothing, options.Portfolio)(0)
                output.Folds.Add(New WalkForwardFoldResult With {
                    .FoldNumber = fold,
                    .TrainingFrom = trainDates.Min(),
                    .TrainingTo = trainDates.Max(),
                    .TestFrom = testDates.Min(),
                    .TestTo = testDates.Max(),
                    .SelectedParameters = selected,
                    .TrainingSummary = trainingRank(0),
                    .TestSummary = testSummary})
                fold += 1
                start += options.StepDateCount
            End While
            Return output
        End Function
    End Class
End Namespace
