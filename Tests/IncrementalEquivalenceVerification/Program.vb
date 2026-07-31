Option Strict On
Option Explicit On
Option Infer Off

Imports System.Linq
Imports System.Reflection
Imports ChartKit.Abstractions
Imports ChartKit.Core
Imports ChartKit.Indicators
Imports ChartKit.Models

Namespace Verification
    Public Module Program
        Private Const InitialCandleCount As Integer = 48
        Private Const FloatTolerance As Double = 0.0001R

        Public Function Main() As Integer
            Try
                Dim indicatorTypes As List(Of Type) = DiscoverIncrementalIndicatorTypes()
                If indicatorTypes.Count = 0 Then
                    Throw New InvalidOperationException("증분 지표 형식을 찾지 못했습니다.")
                End If

                Dim scenarioCount As Integer = 0
                For Each indicatorType As Type In indicatorTypes
                    Dim templates As List(Of IIndicator) = BuildTemplates(indicatorType)
                    For Each template As IIndicator In templates
                        VerifyIncrementalParity(template)
                        scenarioCount += 1
                    Next
                Next

                Console.WriteLine("incremental_indicator_count=" & indicatorTypes.Count.ToString())
                Console.WriteLine("incremental_scenario_count=" & scenarioCount.ToString())
                Console.WriteLine("incremental_equivalence_verification=PASS")
                Return 0
            Catch ex As Exception
                Console.Error.WriteLine("incremental_equivalence_verification=FAIL")
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

        Private Function DiscoverIncrementalIndicatorTypes() As List(Of Type)
            Dim baseType As Type = GetType(IncrementalIndicatorBase)
            Dim assembly As Assembly = baseType.Assembly
            Dim result As List(Of Type) = assembly.GetTypes().
                Where(Function(candidate As Type) candidate.IsClass AndAlso
                                                      Not candidate.IsAbstract AndAlso
                                                      baseType.IsAssignableFrom(candidate)).
                OrderBy(Function(candidate As Type) candidate.FullName, StringComparer.Ordinal).
                ToList()
            Return result
        End Function

        Private Function BuildTemplates(indicatorType As Type) As List(Of IIndicator)
            Dim result As New List(Of IIndicator)()
            Dim seen As New HashSet(Of String)(StringComparer.Ordinal)

            AddTemplateIfUnique(result, seen, CreateDefaultIndicator(indicatorType))

            Dim alternate As IIndicator = CreateDefaultIndicator(indicatorType)
            Dim alternateParameters As New Dictionary(Of String, Object)(alternate.Parameters)
            Dim changed As Boolean = ApplyAlternateParameters(alternateParameters, False)
            If changed Then
                alternate.Parameters = alternateParameters
                AddTemplateIfUnique(result, seen, alternate)
            End If

            If alternate.Parameters.ContainsKey("Type") Then
                Dim weighted As IIndicator = CreateDefaultIndicator(indicatorType)
                Dim weightedParameters As New Dictionary(Of String, Object)(weighted.Parameters)
                weightedParameters("Period") = 7
                weightedParameters("Type") = "WMA"
                weighted.Parameters = weightedParameters
                AddTemplateIfUnique(result, seen, weighted)
            End If

            Return result
        End Function

        Private Sub AddTemplateIfUnique(target As List(Of IIndicator),
                                        seen As HashSet(Of String),
                                        indicator As IIndicator)
            Dim instanceId As String = IndicatorIdentity.CreateInstanceId(indicator)
            If seen.Add(instanceId) Then target.Add(indicator)
        End Sub

        Private Function ApplyAlternateParameters(parameters As Dictionary(Of String, Object),
                                                  useWeightedMovingAverage As Boolean) As Boolean
            Dim changed As Boolean = False

            changed = SetIfPresent(parameters, "Period", 7) OrElse changed
            changed = SetIfPresent(parameters, "SignalPeriod", 5) OrElse changed
            changed = SetIfPresent(parameters, "Fast", 5) OrElse changed
            changed = SetIfPresent(parameters, "Slow", 11) OrElse changed
            changed = SetIfPresent(parameters, "Signal", 4) OrElse changed
            changed = SetIfPresent(parameters, "MAPeriod", 7) OrElse changed
            changed = SetIfPresent(parameters, "AtrPeriod", 7) OrElse changed
            changed = SetIfPresent(parameters, "Multiplier", 2.0F) OrElse changed
            changed = SetIfPresent(parameters, "StdDev1", 1.5F) OrElse changed
            changed = SetIfPresent(parameters, "StdDev2", 2.5F) OrElse changed
            changed = SetIfPresent(parameters, "Phase", -50) OrElse changed
            changed = SetIfPresent(parameters, "Power", 3) OrElse changed
            changed = SetIfPresent(parameters, "Type", If(useWeightedMovingAverage, "WMA", "EMA")) OrElse changed

            Return changed
        End Function

        Private Function SetIfPresent(parameters As Dictionary(Of String, Object),
                                      key As String,
                                      value As Object) As Boolean
            If Not parameters.ContainsKey(key) Then Return False
            parameters(key) = value
            Return True
        End Function

        Private Function CreateDefaultIndicator(indicatorType As Type) As IIndicator
            Dim constructors As ConstructorInfo() = indicatorType.GetConstructors(BindingFlags.Public Or BindingFlags.Instance)
            Array.Sort(Of ConstructorInfo)(
                constructors,
                Function(left As ConstructorInfo, right As ConstructorInfo) As Integer
                    Return left.GetParameters().Length.CompareTo(right.GetParameters().Length)
                End Function)

            For Each constructor As ConstructorInfo In constructors
                Dim parameters As ParameterInfo() = constructor.GetParameters()
                Dim allOptional As Boolean = True
                For Each parameter As ParameterInfo In parameters
                    If Not parameter.IsOptional AndAlso Not parameter.HasDefaultValue Then
                        allOptional = False
                        Exit For
                    End If
                Next
                If Not allOptional Then Continue For

                Dim arguments As Object()
                If parameters.Length = 0 Then
                    arguments = Array.Empty(Of Object)()
                Else
                    ReDim arguments(parameters.Length - 1)
                    For index As Integer = 0 To parameters.Length - 1
                        arguments(index) = ParameterDefaultValue(parameters(index))
                    Next
                End If

                Dim created As Object = constructor.Invoke(arguments)
                Dim indicator As IIndicator = TryCast(created, IIndicator)
                If indicator IsNot Nothing Then Return indicator
            Next

            Throw New InvalidOperationException(
                "선택적 기본 생성자로 지표를 만들 수 없습니다: " & indicatorType.FullName)
        End Function

        Private Function ParameterDefaultValue(parameter As ParameterInfo) As Object
            Dim value As Object = parameter.DefaultValue
            If value IsNot Nothing AndAlso
               value IsNot DBNull.Value AndAlso
               value IsNot Missing.Value Then
                Return value
            End If

            If parameter.ParameterType.IsValueType Then
                Return Activator.CreateInstance(parameter.ParameterType)
            End If
            Return Nothing
        End Function

        Private Function CloneIndicator(template As IIndicator) As IIndicator
            Dim clone As IIndicator = CreateDefaultIndicator(template.GetType())
            clone.Parameters = New Dictionary(Of String, Object)(template.Parameters)
            Return clone
        End Function

        Private Sub VerifyIncrementalParity(template As IIndicator)
            Dim allCandles As List(Of CandleItem) = CreateScenarioCandles()
            Dim workingCandles As New List(Of CandleItem)()
            For index As Integer = 0 To InitialCandleCount - 1
                workingCandles.Add(allCandles(index).Copy())
            Next

            Dim incremental As IIndicator = CloneIndicator(template)
            Dim incrementalResults As List(Of IndicatorResult) = incremental.Calculate(workingCandles)
            CompareAllResults(template, workingCandles, incrementalResults, "initial")

            For sourceIndex As Integer = InitialCandleCount To allCandles.Count - 1
                workingCandles.Add(allCandles(sourceIndex).Copy())
                Dim appended As IndicatorResult = incremental.UpdateLast(workingCandles, incrementalResults)
                incrementalResults.Add(appended)
                CompareAllResults(template, workingCandles, incrementalResults, "append-" & sourceIndex.ToString())

                Dim firstMutation As CandleItem = MutateLastCandle(workingCandles(workingCandles.Count - 1), 1)
                workingCandles(workingCandles.Count - 1) = firstMutation
                Dim firstUpdated As IndicatorResult = incremental.UpdateLast(workingCandles, incrementalResults)
                incrementalResults(incrementalResults.Count - 1) = firstUpdated
                CompareAllResults(template, workingCandles, incrementalResults, "update1-" & sourceIndex.ToString())

                Dim secondMutation As CandleItem = MutateLastCandle(workingCandles(workingCandles.Count - 1), 2)
                workingCandles(workingCandles.Count - 1) = secondMutation
                Dim secondUpdated As IndicatorResult = incremental.UpdateLast(workingCandles, incrementalResults)
                incrementalResults(incrementalResults.Count - 1) = secondUpdated
                CompareAllResults(template, workingCandles, incrementalResults, "update2-" & sourceIndex.ToString())

                Dim repeated As IndicatorResult = incremental.UpdateLast(workingCandles, incrementalResults)
                incrementalResults(incrementalResults.Count - 1) = repeated
                CompareAllResults(template, workingCandles, incrementalResults, "repeat-" & sourceIndex.ToString())
            Next
        End Sub

        Private Sub CompareAllResults(template As IIndicator,
                                      candles As IReadOnlyList(Of CandleItem),
                                      incrementalResults As IReadOnlyList(Of IndicatorResult),
                                      stage As String)
            Dim fresh As IIndicator = CloneIndicator(template)
            Dim expectedResults As List(Of IndicatorResult) = fresh.Calculate(candles)
            Dim context As String = template.GetType().Name & " " &
                                    IndicatorIdentity.CreateInstanceId(template) & " " & stage

            ExpectEqual(incrementalResults.Count, expectedResults.Count, context & " result count")
            For index As Integer = 0 To expectedResults.Count - 1
                CompareResult(expectedResults(index), incrementalResults(index), context & " index=" & index.ToString())
            Next
        End Sub

        Private Sub CompareResult(expected As IndicatorResult,
                                  actual As IndicatorResult,
                                  context As String)
            If expected Is Nothing OrElse actual Is Nothing Then
                If expected Is actual Then Return
                Throw New InvalidOperationException(context & " null result mismatch")
            End If

            ExpectEqual(actual.Name, expected.Name, context & " name")
            ExpectEqual(actual.Index, expected.Index, context & " index")
            ExpectEqual(actual.PanelIndex, expected.PanelIndex, context & " panel")

            Dim expectedKeys As List(Of String) = expected.Values.Keys.
                OrderBy(Function(key As String) key, StringComparer.Ordinal).
                ToList()
            Dim actualKeys As List(Of String) = actual.Values.Keys.
                OrderBy(Function(key As String) key, StringComparer.Ordinal).
                ToList()
            ExpectEqual(String.Join("|", actualKeys), String.Join("|", expectedKeys), context & " value keys")

            For Each key As String In expectedKeys
                CompareSingle(expected.Values(key), actual.Values(key), context & " value=" & key)
                ExpectEqual(CInt(actual.KindOf(key)), CInt(expected.KindOf(key)), context & " kind=" & key)
            Next
        End Sub

        Private Sub CompareSingle(expected As Single,
                                  actual As Single,
                                  context As String)
            If Single.IsNaN(expected) OrElse Single.IsNaN(actual) Then
                If Single.IsNaN(expected) AndAlso Single.IsNaN(actual) Then Return
                Throw New InvalidOperationException(context & $": expected={expected}, actual={actual}")
            End If

            If Single.IsInfinity(expected) OrElse Single.IsInfinity(actual) Then
                If expected.Equals(actual) Then Return
                Throw New InvalidOperationException(context & $": expected={expected}, actual={actual}")
            End If

            Dim difference As Double = Math.Abs(CDbl(expected) - CDbl(actual))
            Dim scale As Double = Math.Max(1.0R, Math.Max(Math.Abs(CDbl(expected)), Math.Abs(CDbl(actual))))
            If difference > FloatTolerance * scale Then
                Throw New InvalidOperationException(
                    context & $": expected={expected:R}, actual={actual:R}, diff={difference:R}")
            End If
        End Sub

        Private Function CreateScenarioCandles() As List(Of CandleItem)
            Dim result As New List(Of CandleItem)()
            AddTradingDay(result, New DateTime(2026, 7, 30), 72, 1000.0F)
            AddTradingDay(result, New DateTime(2026, 7, 31), 72, 1035.0F)
            Return result
        End Function

        Private Sub AddTradingDay(target As List(Of CandleItem),
                                  tradingDate As DateTime,
                                  count As Integer,
                                  startingPrice As Single)
            Dim previousClose As Single = startingPrice
            For index As Integer = 0 To count - 1
                Dim openTime As DateTime = tradingDate.Date.AddHours(9).AddMinutes(index)
                Dim wave As Double = Math.Sin(index / 4.0R) * 4.5R + Math.Cos(index / 11.0R) * 2.0R
                Dim drift As Double = index * 0.18R
                Dim closeValue As Single = CSng(startingPrice + drift + wave + ((index Mod 7) - 3) * 0.15R)
                Dim highValue As Single = Math.Max(previousClose, closeValue) + CSng(0.8R + (index Mod 5) * 0.12R)
                Dim lowValue As Single = Math.Min(previousClose, closeValue) - CSng(0.7R + (index Mod 3) * 0.11R)

                target.Add(New CandleItem With {
                    .Dt = openTime,
                    .Sequence = target.Count,
                    .OpenTime = openTime,
                    .CloseTime = openTime.AddMinutes(1),
                    .IsFinal = True,
                    .Open = previousClose,
                    .High = highValue,
                    .Low = lowValue,
                    .Close = closeValue,
                    .Volume = 1000L + CLng((index Mod 13) * 137 + index * 11)
                })
                previousClose = closeValue
            Next
        End Sub

        Private Function MutateLastCandle(source As CandleItem,
                                          mutationNumber As Integer) As CandleItem
            Dim result As CandleItem = source.Copy()
            Dim adjustment As Single = CSng(0.35R * mutationNumber)
            result.Close += adjustment
            result.High = Math.Max(result.High, result.Close + 0.25F)
            result.Low = Math.Min(result.Low, result.Close - 0.25F)
            result.Volume += 100L * mutationNumber
            result.IsFinal = False
            Return result
        End Function

        Private Sub ExpectEqual(actual As Integer,
                                expected As Integer,
                                description As String)
            If actual <> expected Then
                Throw New InvalidOperationException(
                    description & $": expected={expected}, actual={actual}")
            End If
        End Sub

        Private Sub ExpectEqual(actual As String,
                                expected As String,
                                description As String)
            If Not String.Equals(actual, expected, StringComparison.Ordinal) Then
                Throw New InvalidOperationException(
                    description & $": expected={expected}, actual={actual}")
            End If
        End Sub
    End Module
End Namespace
