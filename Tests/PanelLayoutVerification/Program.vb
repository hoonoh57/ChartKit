Option Strict On
Option Explicit On
Option Infer Off

Imports System.Collections.Generic
Imports ChartKit.Core

Namespace Verification
    Public Module Program
        Public Function Main() As Integer
            Try
                VerifyConstrainedLayoutDoesNotMutateRatios()
                VerifyRepeatedLayoutIsStable()
                VerifyUnconstrainedLayoutUsesSavedRatios()
                VerifyHiddenPanelsDoNotReserveHeight()

                Console.WriteLine("panel_layout_verification=PASS")
                Return 0
            Catch ex As Exception
                Console.Error.WriteLine("panel_layout_verification=FAIL")
                Console.Error.WriteLine(ex.ToString())
                Return 1
            End Try
        End Function

        Private Sub VerifyConstrainedLayoutDoesNotMutateRatios()
            Dim ratios As New List(Of Single) From {0.4F, 0.3F}
            Dim plan As PanelLayoutPlan = PanelLayoutCalculator.Calculate(
                1000.0F,
                0.2F,
                ratios,
                True,
                True)

            ExpectClose(ratios(0), 0.4F, "첫 번째 저장 비율 변경")
            ExpectClose(ratios(1), 0.3F, "두 번째 저장 비율 변경")
            ExpectClose(plan.MainHeight, 250.0F, "최소 메인 패널 높이")
            Expect(
                plan.PanelHeights.Count = 2,
                "서브패널 높이 개수 불일치")
            ExpectClose(
                plan.VolumeHeight +
                plan.PanelHeights(0) +
                plan.PanelHeights(1),
                750.0F,
                "축소 대상 전체 높이")
        End Sub

        Private Sub VerifyRepeatedLayoutIsStable()
            Dim ratios As New List(Of Single) From {0.4F, 0.3F}
            Dim firstPlan As PanelLayoutPlan = PanelLayoutCalculator.Calculate(
                600.0F,
                0.2F,
                ratios,
                True,
                True)
            Dim secondPlan As PanelLayoutPlan = PanelLayoutCalculator.Calculate(
                600.0F,
                0.2F,
                ratios,
                True,
                True)

            ExpectClose(firstPlan.MainHeight, secondPlan.MainHeight, "반복 메인 높이")
            ExpectClose(firstPlan.VolumeHeight, secondPlan.VolumeHeight, "반복 거래량 높이")
            ExpectClose(
                firstPlan.PanelHeights(0),
                secondPlan.PanelHeights(0),
                "반복 첫 번째 패널 높이")
            ExpectClose(
                firstPlan.PanelHeights(1),
                secondPlan.PanelHeights(1),
                "반복 두 번째 패널 높이")
            ExpectClose(ratios(0), 0.4F, "반복 계산 후 첫 번째 저장 비율")
            ExpectClose(ratios(1), 0.3F, "반복 계산 후 두 번째 저장 비율")
        End Sub

        Private Sub VerifyUnconstrainedLayoutUsesSavedRatios()
            Dim ratios As New List(Of Single) From {0.1F, 0.1F}
            Dim plan As PanelLayoutPlan = PanelLayoutCalculator.Calculate(
                1000.0F,
                0.1F,
                ratios,
                True,
                True)

            ExpectClose(plan.MainHeight, 700.0F, "비제약 메인 높이")
            ExpectClose(plan.VolumeHeight, 100.0F, "비제약 거래량 높이")
            ExpectClose(plan.PanelHeights(0), 100.0F, "비제약 첫 패널 높이")
            ExpectClose(plan.PanelHeights(1), 100.0F, "비제약 두 번째 패널 높이")
            ExpectClose(ratios(0), 0.1F, "비제약 첫 번째 저장 비율")
            ExpectClose(ratios(1), 0.1F, "비제약 두 번째 저장 비율")
        End Sub

        Private Sub VerifyHiddenPanelsDoNotReserveHeight()
            Dim ratios As New List(Of Single) From {0.4F, 0.3F}
            Dim plan As PanelLayoutPlan = PanelLayoutCalculator.Calculate(
                1000.0F,
                0.2F,
                ratios,
                True,
                False)

            ExpectClose(plan.MainHeight, 800.0F, "숨김 패널 메인 높이")
            ExpectClose(plan.VolumeHeight, 200.0F, "숨김 패널 거래량 높이")
            Expect(
                plan.PanelHeights.Count = 0,
                "숨김 패널이 높이를 예약함")
            ExpectClose(ratios(0), 0.4F, "숨김 후 첫 번째 저장 비율")
            ExpectClose(ratios(1), 0.3F, "숨김 후 두 번째 저장 비율")
        End Sub

        Private Sub ExpectClose(actual As Single,
                                expected As Single,
                                description As String)
            If Math.Abs(actual - expected) > 0.001F Then
                Throw New InvalidOperationException(
                    description &
                    $": expected={expected}, actual={actual}")
            End If
        End Sub

        Private Sub Expect(condition As Boolean,
                           message As String)
            If Not condition Then
                Throw New InvalidOperationException(message)
            End If
        End Sub
    End Module
End Namespace
