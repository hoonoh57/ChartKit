Imports System.IO
Imports System.Text.Json

Namespace Core
    '' 영속화 대상 상태 DTO. System.Text.Json 직렬화.
    Public Class IndicatorState
        Public Property TypeName As String        '' AssemblyQualifiedName
        Public Property Params As Dictionary(Of String, String)  '' 값은 문자열로 보관(형 안전)
    End Class

    Public Class OverlayShadeRule
        Public Property IndicatorA As String   '' 지표A 이름 (Engine.Name)
        Public Property IndicatorB As String   '' 지표B 이름
        '' A >= B 인 구간을 이 색으로 배경 음영 (기본 옅은 분홍)
        Public Property ColorR As Integer = 233
        Public Property ColorG As Integer = 30
        Public Property ColorB As Integer = 99
        Public Property ColorA As Integer = 35
        '' True 면 B(장기)가 직전봉 대비 상승중인 구간만 음영
        Public Property RequireBRising As Boolean = False

        '' ── 표시 속성 (키움 신호검색 스타일) ──
        Public Property Side As Integer = -1        '' -1=CrossUp 따름, 0=매수, 1=매도
        Public Property MarkerShape As Integer = 0  '' 0=화살표 1=위삼각 2=아래삼각 3=다이아 4=원 5=별 6=네모 7=십자
        Public Property ColorArgb As Integer = 0    '' 0이면 매수/매도 기본색
        Public Property Name As String = ""         '' 검색식명
    End Class

    '' 신호 규칙: A 가 B 를 crossUp(상향돌파) 또는 crossDown(하향돌파) 하는 봉을 표시.
    Public Enum SignalSide
        매수 = 0
        매도 = 1
    End Enum

    Public Enum SignalMarker
        화살표 = 0
        위삼각 = 1
        아래삼각 = 2
        다이아몬드 = 3
        원 = 4
        별 = 5
        네모 = 6
        십자 = 7
    End Enum

    Public Class SignalRule
        <System.ComponentModel.Category("신호"), System.ComponentModel.DisplayName("검색식명")>
        Public Property Name As String = ""

        <System.ComponentModel.Category("조건"), System.ComponentModel.DisplayName("지표A")>
        Public Property IndicatorA As String

        <System.ComponentModel.Category("조건"), System.ComponentModel.DisplayName("지표B")>
        Public Property IndicatorB As String

        <System.ComponentModel.Category("조건"), System.ComponentModel.DisplayName("상향돌파(매수)"), System.ComponentModel.Description("True=상향돌파(A가 B를 위로), False=하향돌파")>
        Public Property CrossUp As Boolean = True

        <System.ComponentModel.Category("조건"), System.ComponentModel.DisplayName("B상승중만"), System.ComponentModel.Description("돌파 후 B(장기)가 상승 전환하는 첫 봉에만 신호")>
        Public Property RequireBRising As Boolean = False

        '' ── 내부 저장용 (Integer). 그리드에는 숨김 ──
        <System.ComponentModel.Browsable(False)>
        Public Property Side As Integer = -1
        <System.ComponentModel.Browsable(False)>
        Public Property MarkerShape As Integer = 0
        <System.ComponentModel.Browsable(False)>
        Public Property ColorArgb As Integer = 0

        '' ── 그리드 편집용 래퍼 (Enum/Color) ──
        <System.ComponentModel.Category("표시"), System.ComponentModel.DisplayName("표시(매수/매도)")>
        Public Property SideEnum As SignalSide
            Get
                If Side < 0 Then Return If(CrossUp, SignalSide.매수, SignalSide.매도)
                Return CType(Side, SignalSide)
            End Get
            Set(value As SignalSide)
                Side = CInt(value)
            End Set
        End Property

        <System.ComponentModel.Category("표시"), System.ComponentModel.DisplayName("모양")>
        Public Property MarkerEnum As SignalMarker
            Get
                Return CType(MarkerShape, SignalMarker)
            End Get
            Set(value As SignalMarker)
                MarkerShape = CInt(value)
            End Set
        End Property

        <System.ComponentModel.Category("표시"), System.ComponentModel.DisplayName("색상")>
        Public Property MarkerColor As System.Drawing.Color
            Get
                If ColorArgb = 0 Then Return System.Drawing.Color.Empty
                Return System.Drawing.Color.FromArgb(ColorArgb)
            End Get
            Set(value As System.Drawing.Color)
                If value.IsEmpty Then
                    ColorArgb = 0
                Else
                    ColorArgb = value.ToArgb()
                End If
            End Set
        End Property

        Public Overrides Function ToString() As String
            Dim nm = If(String.IsNullOrEmpty(Name), $"{IndicatorA} {If(CrossUp, "▲", "▼")} {IndicatorB}", Name)
            Return nm
        End Function
    End Class

    Public Class PanelZoneState
        Public Property OverValue As Single?   '' 과열 기준값(이상 음영). Nothing=없음
        Public Property UnderValue As Single?  '' 침체 기준값(이하 음영). Nothing=없음
    End Class

    Public Class LayerToggleState
        Public Property Id As String
        Public Property Visible As Boolean
    End Class

    Public Class ChartState
        '' 뷰포트
        Public Property CandleCount As Integer = 0
        Public Property StartIndex As Integer = 0
        Public Property CandleWidth As Single = 8
        Public Property Gap As Single = 2
        '' Y 스케일
        Public Property IsAutoScaleY As Boolean = True
        Public Property ManualMaxP As Single = 0
        Public Property ManualMinP As Single = 0
        '' 패널 높이 비율
        Public Property PanelRatios As New List(Of Single)()
        '' 지표
        Public Property Indicators As New List(Of IndicatorState)()
        '' 레이어 토글
        Public Property Layers As New List(Of LayerToggleState)()
        '' 사용자 편집 기준선: key=서브패널 인덱스(0=첫 서브패널)
        Public Property PanelBaselines As New Dictionary(Of Integer, List(Of Single))()
        '' 과열/침체 음영 (키=서브패널 인덱스)
        Public Property PanelZones As New Dictionary(Of Integer, PanelZoneState)()
        '' 오버레이 배경 음영 규칙 (A>=B 구간)
        Public Property PctAxisMode As Integer = 0
        Public Property ShadeRules As New List(Of OverlayShadeRule)()
        Public Property SignalRules As New List(Of SignalRule)()

        Private Shared ReadOnly _opts As New JsonSerializerOptions With {.WriteIndented = True}

        Public Shared Function GetPath(profile As String) As String
            Dim dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ChartKit")
            If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
            Dim safe = If(String.IsNullOrWhiteSpace(profile), "default", profile)
            For Each ch In Path.GetInvalidFileNameChars()
                safe = safe.Replace(ch, "_"c)
            Next
            Return Path.Combine(dir, $"chart_state_{safe}.json")
        End Function

        Public Sub Save(profile As String)
            Try
                Dim json = JsonSerializer.Serialize(Me, _opts)
                File.WriteAllText(GetPath(profile), json)
            Catch
                '' 저장 실패는 무시(치명적 아님)
            End Try
        End Sub

        Public Shared Function Load(profile As String) As ChartState
            Try
                Dim p = GetPath(profile)
                If Not File.Exists(p) Then Return Nothing
                Dim json = File.ReadAllText(p)
                Return JsonSerializer.Deserialize(Of ChartState)(json)
            Catch
                Return Nothing
            End Try
        End Function
    End Class
End Namespace