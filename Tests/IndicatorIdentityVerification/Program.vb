Option Strict On
Option Explicit On
Option Infer Off

Imports ChartKit.Abstractions
Imports ChartKit.Core
Imports ChartKit.Models

Namespace Verification
    Public Module Program
        Public Function Main() As Integer
            Try
                VerifyCanonicalIdentityIgnoresParameterOrder()
                VerifyLegacyNameCollisionKeepsBothInstances()
                VerifyUniqueLegacyAliasCompatibility()
                VerifyDuplicateInstanceIsIgnored()
                VerifyParameterEditCanReregister()
                VerifySeriesIdentity()

                Console.WriteLine("indicator_identity_verification=PASS")
                Return 0
            Catch ex As Exception
                Console.Error.WriteLine("indicator_identity_verification=FAIL")
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

        Private Sub VerifyCanonicalIdentityIgnoresParameterOrder()
            Dim first As New Fake_Indicator(
                "JMA_14",
                New Dictionary(Of String, Object) From {
                    {"Period", 14},
                    {"Phase", 50},
                    {"Power", 2}
                })
            Dim second As New Fake_Indicator(
                "JMA_14",
                New Dictionary(Of String, Object) From {
                    {"Power", 2},
                    {"Period", 14},
                    {"Phase", 50}
                })

            Dim firstId As String = IndicatorIdentity.CreateInstanceId(first)
            Dim secondId As String = IndicatorIdentity.CreateInstanceId(second)

            ExpectEqual(firstId, secondId, "파라미터 순서에 따른 InstanceId 변화")
            Expect(
                firstId.StartsWith("FAKE[", StringComparison.Ordinal),
                "DefinitionId가 형식명에서 안정적으로 생성되지 않음")
        End Sub

        Private Sub VerifyLegacyNameCollisionKeepsBothInstances()
            Dim engine As New IndicatorEngine()
            engine.Register(New Fake_Indicator(
                "JMA_14",
                New Dictionary(Of String, Object) From {
                    {"Period", 14}, {"Phase", 50}, {"Power", 2}
                }))
            engine.Register(New Fake_Indicator(
                "JMA_14",
                New Dictionary(Of String, Object) From {
                    {"Period", 14}, {"Phase", -50}, {"Power", 2}
                }))

            Dim registered As List(Of IIndicator) = engine.GetAll()
            Expect(registered.Count = 2, "같은 옛 이름의 서로 다른 지표가 하나로 축소됨")

            Dim firstIdentity = TryCast(registered(0), IIndicatorIdentity)
            Dim secondIdentity = TryCast(registered(1), IIndicatorIdentity)
            Expect(firstIdentity IsNot Nothing, "첫 번째 등록 지표 identity 누락")
            Expect(secondIdentity IsNot Nothing, "두 번째 등록 지표 identity 누락")
            Expect(
                Not String.Equals(
                    firstIdentity.InstanceId,
                    secondIdentity.InstanceId,
                    StringComparison.Ordinal),
                "서로 다른 파라미터가 같은 InstanceId를 생성함")
            ExpectEqual(registered(0).Name, firstIdentity.InstanceId, "Name 호환키가 InstanceId가 아님")
            ExpectEqual(registered(1).Name, secondIdentity.InstanceId, "두 번째 Name 호환키가 InstanceId가 아님")

            engine.CalculateAll(CreateCandles())

            Expect(engine.Results.ContainsKey(firstIdentity.InstanceId), "첫 번째 정식 결과키 누락")
            Expect(engine.Results.ContainsKey(secondIdentity.InstanceId), "두 번째 정식 결과키 누락")
            Expect(
                Not engine.Results.ContainsKey("JMA_14"),
                "충돌하는 옛 이름을 임의 지표에 연결함")
            ExpectEqual(
                engine.Results(firstIdentity.InstanceId)(0).Name,
                firstIdentity.InstanceId,
                "IndicatorResult.Name이 정식 InstanceId로 정규화되지 않음")
        End Sub

        Private Sub VerifyUniqueLegacyAliasCompatibility()
            Dim engine As New IndicatorEngine()
            engine.Register(New Fake_Indicator(
                "MACD_12_26_9",
                New Dictionary(Of String, Object) From {
                    {"Fast", 12}, {"Slow", 26}, {"Signal", 9}
                }))
            engine.CalculateAll(CreateCandles())

            Dim registered As IIndicator = engine.GetAll()(0)
            Dim identity = DirectCast(registered, IIndicatorIdentity)

            Expect(engine.Results.ContainsKey(identity.InstanceId), "정식 MACD InstanceId 누락")
            Expect(engine.Results.ContainsKey("MACD_12_26_9"), "단일 옛 이름 별칭 누락")
            Expect(
                Object.ReferenceEquals(
                    engine.Results(identity.InstanceId),
                    engine.Results("MACD_12_26_9")),
                "옛 이름 별칭이 정식 결과 버퍼와 다름")
        End Sub

        Private Sub VerifyDuplicateInstanceIsIgnored()
            Dim engine As New IndicatorEngine()
            Dim parameters As New Dictionary(Of String, Object) From {
                {"Period", 20}, {"Type", "SMA"}
            }

            engine.Register(New Fake_Indicator("SMA_20", parameters))
            engine.Register(New Fake_Indicator(
                "another-legacy-name",
                New Dictionary(Of String, Object)(parameters)))

            Expect(engine.GetAll().Count = 1, "동일 InstanceId가 중복 등록됨")
        End Sub

        Private Sub VerifyParameterEditCanReregister()
            Dim engine As New IndicatorEngine()
            engine.Register(New Fake_Indicator(
                "JMA_14",
                New Dictionary(Of String, Object) From {
                    {"Period", 14}, {"Phase", 50}, {"Power", 2}
                }))

            Dim registered As IIndicator = engine.GetAll()(0)
            Dim oldId As String = registered.Name
            Dim changed As New Dictionary(Of String, Object)(registered.Parameters) From {
                {"Phase", -50}
            }
            registered.Parameters = changed
            Dim newId As String = registered.Name

            Expect(
                Not String.Equals(oldId, newId, StringComparison.Ordinal),
                "파라미터 변경 후 InstanceId가 갱신되지 않음")

            engine.Remove(oldId)
            engine.Register(registered)
            engine.CalculateAll(CreateCandles())

            Expect(engine.GetAll().Count = 1, "파라미터 변경 재등록 후 지표 수 이상")
            Expect(engine.Results.ContainsKey(newId), "변경된 InstanceId 결과 누락")
            Expect(Not engine.Results.ContainsKey(oldId), "이전 InstanceId 결과가 남음")
            Expect(
                IndicatorIdentity.SourceTypeName(registered).Contains(
                    GetType(Fake_Indicator).FullName,
                    StringComparison.Ordinal),
                "상태 저장용 원본 지표 형식 복원 실패")
        End Sub

        Private Sub VerifySeriesIdentity()
            Dim indicator As New Fake_Indicator(
                "RSI_14",
                New Dictionary(Of String, Object) From {
                    {"Period", 14}, {"SignalPeriod", 9}
                })
            Dim instanceId As String = IndicatorIdentity.CreateInstanceId(indicator)
            Dim valueId As String = IndicatorIdentity.CreateSeriesId(instanceId, "Value")
            Dim signalId As String = IndicatorIdentity.CreateSeriesId(instanceId, "Signal")

            ExpectEqual(valueId, instanceId & "::Value", "Value SeriesId 형식 오류")
            Expect(
                Not String.Equals(valueId, signalId, StringComparison.Ordinal),
                "서로 다른 series key가 같은 SeriesId를 생성함")
        End Sub

        Private Function CreateCandles() As List(Of CandleItem)
            Return New List(Of CandleItem) From {
                New CandleItem With {
                    .Dt = New DateTime(2026, 7, 31, 9, 0, 0),
                    .Open = 100.0F, .High = 101.0F, .Low = 99.0F,
                    .Close = 100.5F, .Volume = 1000L
                },
                New CandleItem With {
                    .Dt = New DateTime(2026, 7, 31, 9, 1, 0),
                    .Open = 100.5F, .High = 102.0F, .Low = 100.0F,
                    .Close = 101.5F, .Volume = 1200L
                }
            }
        End Function

        Private Sub Expect(condition As Boolean,
                           message As String)
            If Not condition Then Throw New InvalidOperationException(message)
        End Sub

        Private Sub ExpectEqual(actual As String,
                                expected As String,
                                description As String)
            If Not String.Equals(actual, expected, StringComparison.Ordinal) Then
                Throw New InvalidOperationException(
                    description & $": expected={expected}, actual={actual}")
            End If
        End Sub

        Private NotInheritable Class Fake_Indicator
            Implements IIndicator

            Private ReadOnly _legacyName As String
            Private _parameters As Dictionary(Of String, Object)

            Public Sub New(legacyName As String,
                           parameters As Dictionary(Of String, Object))
                _legacyName = legacyName
                _parameters = New Dictionary(Of String, Object)(parameters)
            End Sub

            Public ReadOnly Property Name As String Implements IIndicator.Name
                Get
                    Return _legacyName
                End Get
            End Property

            Public ReadOnly Property DisplayName As String Implements IIndicator.DisplayName
                Get
                    Return _legacyName
                End Get
            End Property

            Public ReadOnly Property PanelIndex As Integer Implements IIndicator.PanelIndex
                Get
                    Return 0
                End Get
            End Property

            Public Property Parameters As Dictionary(Of String, Object) Implements IIndicator.Parameters
                Get
                    Return _parameters
                End Get
                Set(value As Dictionary(Of String, Object))
                    _parameters = If(
                        value Is Nothing,
                        New Dictionary(Of String, Object)(),
                        New Dictionary(Of String, Object)(value))
                End Set
            End Property

            Public Function Calculate(candles As IReadOnlyList(Of CandleItem)) As List(Of IndicatorResult) Implements IIndicator.Calculate
                Dim results As New List(Of IndicatorResult)(candles.Count)
                For index As Integer = 0 To candles.Count - 1
                    results.Add(CreateResult(index, candles(index).Close))
                Next
                Return results
            End Function

            Public Function UpdateLast(candles As IReadOnlyList(Of CandleItem),
                                       prevResults As IReadOnlyList(Of IndicatorResult)) As IndicatorResult Implements IIndicator.UpdateLast
                Dim index As Integer = candles.Count - 1
                Return CreateResult(index, candles(index).Close)
            End Function

            Private Function CreateResult(index As Integer,
                                          value As Single) As IndicatorResult
                Return New IndicatorResult With {
                    .Name = Name,
                    .Index = index,
                    .PanelIndex = PanelIndex,
                    .Values = New Dictionary(Of String, Single) From {
                        {"Value", value}
                    },
                    .SeriesKinds = New Dictionary(Of String, SeriesKind) From {
                        {"Value", SeriesKind.Line}
                    }
                }
            End Function
        End Class
    End Module
End Namespace
