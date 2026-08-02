# ChartKit 범용 모듈 플랫폼 아키텍처 헌장

상태: **Architecture Freeze Candidate 1**  
문서 버전: `1.0-rc1`  
작성일: 2026-08-01

---

# 1. 목적

ChartKit은 단순 차트 프로그램이 아니라 다음 기능을 지속적으로 추가할 수 있는 범용 차트 플랫폼을 목표로 한다.

```text
기술적 지표
시장지수
비교종목
복수 종목 서브차트
작도 도구
마우스 동작
전략
매수·매도 신호
강세·약세 구간
호가·체결 정보
시장분석
매매대상 후보
주문·포지션
DSL 수식
외부 패널과 명령
```

기능 수가 늘어나도 다음 파일에 기능별 조건문이 누적되지 않아야 한다.

```text
SkiaChartRenderer
ChartFrameBuilder
SymbolRuntime
MainForm
```

핵심 성공 조건:

> 기능의 내부 구현 복잡도는 제한하지 않되, 등록·표시·설정·저장·삭제의 연결 절차는 모든 기능에서 동일하게 유지한다.

---

# 2. 절대 원칙

## 2.1 Renderer는 업무 기능을 모른다

Renderer에 다음 이름이 등장하지 않아야 한다.

```text
RSI
MACD
SuperTrend
Strategy
OrderBook
Fibonacci
CandidateSymbol
KospiComparison
```

Renderer는 다음과 같은 제한된 범용 Primitive만 처리한다.

```text
CandleBatch
PolylineBatch
HistogramBatch
HorizontalHistogramBatch
LineBatch
MarkerBatch
RectangleBatch
FillAreaBatch
TextBatch
HeatCellBatch
ImageBatch
```

## 2.2 모듈은 Renderer 기술을 모른다

개별 모듈은 다음을 직접 참조하지 않는다.

```text
SKCanvas
SKPaint
SKPath
WinForms Control
SkiaChartRenderer
```

모듈은 데이터 중심 Contribution을 생성한다.

## 2.3 UI는 개별 기능을 하드코딩하지 않는다

컨텍스트 메뉴, 퀵버튼, Property Inspector는 모듈이 제공한 메타데이터에서 자동 생성한다.

잘못된 예:

```text
MainForm.ToggleRsi()
MainForm.OpenMacdProperties()
MainForm.DrawFibonacci()
```

올바른 예:

```text
moduleHost.SetEnabled(instanceId, true)
commandBus.Execute(commandId, targetId)
propertyService.Change(instanceId, propertyId, value)
```

## 2.4 저장 대상은 프로필과 도구 상태다

저장:

```text
타임프레임
활성 모듈
모듈 파라미터
색상·스타일
패널 높이·순서
축 설정
퀵버튼 고정
작도 기준점
종목별 메모
마지막 viewport
```

저장 금지:

```text
SKPaint
SKPath
SKCanvas
픽셀 좌표 기반 RenderPlan
GPU 리소스
렌더러 캐시 객체
```

## 2.5 비활성 모듈 비용은 0에 가까워야 한다

비활성 모듈은 다음을 수행하지 않는다.

```text
데이터 구독
지표 계산
Contribution 생성
HitTest 등록
프레임 invalidation
```

## 2.6 새 기능 추가 시 Renderer 수정은 원칙적으로 금지한다

예외는 새로운 범용 Primitive가 필요하고, 두 개 이상의 독립 기능에서 재사용 가능함이 증명된 경우다.

---

# 3. 목표 구조

```text
DataSources
    ↓ 표준 Market Data
Engine / Module Data Runtime
    ↓ 모듈별 계산 상태
Chart Modules
    ↓ Contributions
Composition / Scene Compiler
    ↓ immutable RenderPlan
Rendering
    ↓ pixels
App / UI Adapters
    ↕ Command, Property, Selection, Profile
```

프로젝트 후보:

```text
ChartKit.Contracts
ChartKit.Engine
ChartKit.Charting
ChartKit.Modules.Abstractions
ChartKit.ModuleHost
ChartKit.Scene
ChartKit.Composition
ChartKit.Persistence
ChartKit.Rendering
ChartKit.Interactions
ChartKit.DataSources
ChartKit.App
```

모듈 구현 후보:

```text
ChartKit.Modules.Indicators
ChartKit.Modules.Comparison
ChartKit.Modules.Drawing
ChartKit.Modules.Strategy
ChartKit.Modules.OrderBook
ChartKit.Modules.MarketAnalysis
ChartKit.Modules.Trading
ChartKit.Modules.Dsl
```

기능마다 별도 DLL을 강제하지 않는다. 독립 클래스·상태·설정·검증·Contribution 경계가 유지되면 같은 프로젝트에 여러 모듈을 둘 수 있다.

---

# 4. 핵심 개념

## 4.1 Module Definition

설치 가능한 기능 종류다.

예:

```text
indicator.rsi
indicator.supertrend
comparison.symbol
market.orderbook
drawing.fibonacci
strategy.dsl
candidate.ranking
```

## 4.2 Module Instance

한 차트에 실제 배치된 기능 인스턴스다.

같은 모듈을 여러 번 사용할 수 있다.

```text
indicator.rsi / instance=rsi-14
indicator.rsi / instance=rsi-7
comparison.symbol / instance=compare-005930
```

## 4.3 ChartProfile

차트 구성의 선언형 설계도다.

```text
타임프레임
데이터 범위
활성 Module Instance
파라미터
스타일
패널 배치
축
퀵버튼
Interaction 설정
```

## 4.4 Contribution

모듈이 Composition 계층에 제공하는 표준 결과다.

```text
데이터 요구
패널 요청
축 요청
Series
Primitive
Legend
HitRegion
Command
Property Schema
외부 Panel Model
Status
```

## 4.5 Scene

활성 모듈의 Contribution을 논리적으로 합성한 차트 장면이다.

## 4.6 RenderPlan

현재 viewport, DPI, 패널 배치, 축 범위까지 반영된 불변 렌더러 입력이다.

Renderer는 RenderPlan만 처리한다.

---

# 5. 범용 Registry와 On/Off 스위치

Registry는 기능 ID를 생성자와 메타데이터에 연결한다.

```csharp
public interface IChartModuleRegistry
{
    void Register(ChartModuleDefinition definition);
    bool TryGet(string moduleId, out ChartModuleDefinition definition);
    IChartModule Create(string moduleId, string instanceId);
    IReadOnlyList<ChartModuleDefinition> Search(string query);
}
```

정의:

```csharp
public sealed record ChartModuleDefinition(
    string ModuleId,
    string DisplayName,
    string Category,
    int SchemaVersion,
    string[] Tags,
    Func<string, IChartModule> Factory);
```

범용 활성화 명령:

```csharp
public sealed record SetModuleEnabledCommand(
    string InstanceId,
    bool IsEnabled);
```

처리 절차:

```text
ChartProfile IsEnabled 변경
→ Activate/Deactivate
→ 데이터 구독 재계산
→ Contribution invalidation
→ Scene 재합성
→ RenderPlan 교체
→ 프로필 저장
```

기능별 Toggle 메서드를 만들지 않는다.

---

# 6. 모듈 역할 인터페이스

하나의 거대한 인터페이스 대신 공통 수명주기와 역할별 capability를 사용한다.

## 6.1 공통 모듈

```csharp
public interface IChartModule
{
    string ModuleId { get; }
    string InstanceId { get; }

    void Initialize(IChartModuleContext context);
    void ApplyProfile(ChartModuleProfile profile);
    void Activate();
    void Deactivate();
    void Reset();
}
```

## 6.2 데이터 요구

```csharp
public interface IDataRequirementProvider
{
    void DescribeRequirements(IDataRequirementWriter writer);
}
```

예:

```text
현재 종목 120틱
KOSPI 일봉
005930 5분봉
호가 10단계
체결 이벤트
전략 포지션 상태
```

## 6.3 계산

```csharp
public interface IChartComputationModule
{
    void OnHistory(ChartDataBatch batch);
    void OnMarketEvent(ChartMarketEvent value);
    long DataVersion { get; }
}
```

## 6.4 시각 결과

```csharp
public interface IChartVisualProvider
{
    void BuildContributions(
        ChartVisualContext context,
        IChartContributionWriter writer);
}
```

## 6.5 Interaction

```csharp
public interface IChartInteractionProvider
{
    void RegisterInteractions(IChartInteractionWriter writer);
    void HandleInteraction(ChartInteractionEvent value);
}
```

## 6.6 명령

```csharp
public interface IChartCommandProvider
{
    void RegisterCommands(IChartCommandWriter writer);
    ValueTask ExecuteAsync(
        string commandId,
        ChartCommandContext context,
        CancellationToken cancellationToken);
}
```

## 6.7 프로퍼티

```csharp
public interface IChartPropertyProvider
{
    void DescribeProperties(
        ChartPropertyContext context,
        IChartPropertyWriter writer);

    ChartPropertyChangeResult ApplyProperty(
        string propertyId,
        object? value);
}
```

## 6.8 상태 저장

```csharp
public interface IChartPersistentStateProvider
{
    JsonObject SaveState();
    void RestoreState(JsonObject state);
}
```

## 6.9 외부 UI 출력

차트 밖 후보목록·시장분석표 등이 필요한 모듈은 별도 표준 모델을 제공한다.

```csharp
public interface IChartSidePanelProvider
{
    void BuildSidePanels(IChartSidePanelWriter writer);
}
```

모듈이 WinForms Control을 직접 생성하지 않는다.

---

# 7. UI 메타데이터 표준

## 7.1 Command Descriptor

동일 메타데이터로 컨텍스트 메뉴와 퀵버튼을 만든다.

```csharp
public sealed record ChartCommandDescriptor(
    string CommandId,
    string OwnerInstanceId,
    string DisplayName,
    string Category,
    string? IconKey,
    string? Shortcut,
    bool IsCheckable,
    bool IsChecked,
    bool IsEnabled,
    ChartCommandPlacement Placement);
```

Placement:

```text
ContextMenu
QuickToolbar
MainMenu
PropertyInspector
KeyboardOnly
```

## 7.2 Property Descriptor

```csharp
public sealed record ChartPropertyDescriptor(
    string PropertyId,
    string DisplayName,
    string Category,
    string Description,
    ChartPropertyType PropertyType,
    object? Value,
    object? DefaultValue,
    object? Minimum,
    object? Maximum,
    object? Step,
    object[] AllowedValues,
    bool IsReadOnly,
    ChartChangeImpact ChangeImpact);
```

Property type:

```text
Boolean
Integer
Decimal
String
Enum
Color
LineStyle
Symbol
Timeframe
PanelId
Formula
File
DateRange
Collection
```

## 7.3 Property Inspector 규칙

- 모듈 객체 자체를 WinForms `PropertyGrid.SelectedObject`로 노출하지 않는다.
- Property Schema를 동적 편집 객체로 변환하는 Adapter를 둔다.
- 내부 필드명과 저장 스키마를 분리한다.
- 검증과 change impact는 모듈이 선언한다.
- UI는 타입별 공통 editor만 제공한다.

## 7.4 선택 동기화

다음 선택 경로는 모두 같은 Selection Service를 사용한다.

```text
차트 객체 클릭
범례 클릭
컨텍스트 메뉴 속성
모듈 관리 목록 선택
패널 선택
```

```csharp
public sealed record ChartSelection(
    string OwnerInstanceId,
    string? VisualObjectId,
    string? PanelId);
```

---

# 8. 프로필과 저장

## 8.1 설정 병합 우선순위

```text
System Default
→ User Default
→ Workspace Profile
→ Chart Template
→ Symbol Override
→ Session Temporary Override
```

## 8.2 ChartProfile

```csharp
public sealed record ChartProfile
{
    public int SchemaVersion { get; init; }
    public string ProfileId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public CandleTimeframe Timeframe { get; init; }
    public ChartLayoutProfile Layout { get; init; } = new();
    public ChartInteractionProfile Interaction { get; init; } = new();
    public ChartThemeProfile Theme { get; init; } = new();
    public ChartModuleProfile[] Modules { get; init; } = [];
}
```

## 8.3 Module Profile

```csharp
public sealed record ChartModuleProfile
{
    public string InstanceId { get; init; } = "";
    public string ModuleId { get; init; } = "";
    public int ModuleSchemaVersion { get; init; }
    public bool IsEnabled { get; init; }
    public int ZIndex { get; init; }
    public string Placement { get; init; } = "";
    public JsonObject Parameters { get; init; } = [];
    public JsonObject Style { get; init; } = [];
    public JsonObject PersistentState { get; init; } = [];
}
```

## 8.4 Template와 Symbol State 분리

Template:

```text
타임프레임
지표와 파라미터
패널 배치
색상
전략 모듈
축
퀵버튼
```

Symbol state:

```text
추세선
피보나치
수평선
종목 메모
특정 신호 숨김
마지막 viewport
```

## 8.5 저장 정책

- 메모리 변경은 즉시 반영한다.
- 디스크 저장은 debounce한다.
- 임시 마우스 이동은 저장하지 않는다.
- 확정된 작도와 파라미터 변경은 Undo 기록 후 저장한다.
- 스키마 버전을 반드시 기록한다.
- 모듈별 migration을 제공한다.

---

# 9. Contribution과 Scene

## 9.1 데이터 Contribution

```text
CandleSeriesContribution
NumericSeriesContribution
BooleanSeriesContribution
CategorySeriesContribution
Level2SeriesContribution
```

## 9.2 구조 Contribution

```text
PanelRequest
AxisRequest
ScaleRequest
LegendContribution
ReferenceLineContribution
```

## 9.3 시각 Contribution

```text
CandleContribution
PolylineContribution
HistogramContribution
HorizontalHistogramContribution
LineContribution
MarkerContribution
RectangleContribution
FillAreaContribution
TextContribution
HeatCellContribution
ImageContribution
```

## 9.4 Interaction Contribution

```text
HitRegion
DragHandle
PointerBinding
KeyboardBinding
ContextCommand
SelectionDescriptor
```

## 9.5 외부 UI Contribution

```text
ToolbarCommand
PropertyDescriptor
SidePanelDescriptor
StatusContribution
NotificationContribution
```

---

# 10. PanelGraph와 축

현재 고정 `PanelIndex`는 동적 `PanelId`로 점진 이전한다.

예:

```text
price.main
volume.main
indicator.rsi
indicator.macd
symbol.005930
symbol.000660
market.orderbook
```

PanelRequest:

```csharp
public sealed record ChartPanelRequest(
    string PanelId,
    PanelPlacement Placement,
    float PreferredHeightRatio,
    float MinimumHeight,
    AxisMode AxisMode,
    bool IsCollapsible,
    bool IsVisible);
```

필수 지원:

```text
높이 조절
순서 이동
접기·펼치기
서브패널 위 오버레이
서브패널 안 추가 서브패널
공유 축
독립 축
좌측·우측 축
복수 축
저장·복원
```

패널 경계 드래그와 Property Inspector의 높이 변경은 같은 Profile 값을 변경한다.

---

# 11. Scene Compiler와 RenderPlan

Scene Compiler 책임:

```text
활성 모듈 Contribution 수집
PanelGraph 해결
축·범위 해결
시간 정렬
Z-order 해결
클리핑 해결
viewport 필터링
Primitive batch 구성
HitTest index 구성
RenderPlan 생성
```

RenderPlan 후보:

```csharp
public sealed class ChartRenderPlan
{
    public ChartFrame Frame { get; init; } = default!;
    public CandleBatch[] Candles { get; init; } = [];
    public PolylineBatch[] Polylines { get; init; } = [];
    public HistogramBatch[] Histograms { get; init; } = [];
    public MarkerBatch[] Markers { get; init; } = [];
    public LineBatch[] Lines { get; init; } = [];
    public RectangleBatch[] Rectangles { get; init; } = [];
    public FillAreaBatch[] Areas { get; init; } = [];
    public TextBatch[] Texts { get; init; } = [];
    public HeatCellBatch[] HeatCells { get; init; } = [];
    public ChartHitTestIndex HitTestIndex { get; init; } = default!;
}
```

Renderer는 모듈 Registry, ChartProfile, Property Schema를 알지 않는다.

---

# 12. Interaction 구조

```text
Pointer/Keyboard Event
→ Interaction Router
→ HitTestIndex
→ Owner Instance 확인
→ 해당 Interaction Module에 전달
→ 모듈 상태 변경
→ 필요한 계층만 invalidate
→ 새 Contribution/RenderPlan
```

지원 대상:

```text
십자선
패닝
줌
가격축 이동
패널 splitter
도형 선택
도형 이동
핸들 조절
복사·삭제
Undo/Redo
스냅
컨텍스트 메뉴
단축키
```

Renderer는 상호작용 상태를 소유하지 않는다.

---

# 13. 변경 영향과 invalidation

```csharp
public enum ChartChangeImpact
{
    None = 0,
    RedrawOnly = 1,
    RebuildVisuals = 2,
    RecalculateModule = 3,
    RebuildLayout = 4,
    ReloadData = 5,
    RestartSubscription = 6,
    RebuildWorkspace = 7
}
```

예:

```text
색상 변경 → RedrawOnly
선 굵기 → RedrawOnly
범례 표시 → RebuildVisuals
RSI 기간 → RecalculateModule
패널 이동 → RebuildLayout
비교종목 변경 → ReloadData
타임프레임 변경 → ReloadData + RestartSubscription
```

모든 Property 변경에 전체 차트 재계산을 사용하지 않는다.

---

# 14. 성능 모델

비용은 등록된 총 모듈 수가 아니라 다음에 비례해야 한다.

```text
활성 모듈 수
× 현재 표시 구간
× 변경된 Contribution
```

필수 정책:

```text
비활성 모듈 계산 0
화면 밖 Primitive 최소화
증분 계산
모듈별 결과 캐시
viewport 변경 시 계산 결과 재사용
마우스 이동 시 interaction 계층만 갱신
호가 변경 시 호가 계층만 갱신
RenderPlan double buffering
렌더링 스레드에서 모듈 계산 금지
Paint/Path 재사용
```

버전 키 후보:

```text
DataVersion
ConfigurationVersion
VisualVersion
ViewportVersion
ThemeVersion
```

캐시 키:

```text
InstanceId
+ DataVersion
+ ConfigurationVersion
+ ViewportVersion
```

---

# 15. 오류 격리

한 모듈의 실패가 차트 전체를 중단시키지 않아야 한다.

```text
모듈 계산 오류
→ 해당 모듈 Faulted
→ 이전 정상 Contribution 유지 또는 숨김
→ 상태창에 오류 표시
→ 다른 모듈과 Renderer 계속 동작
```

각 모듈은 다음 진단을 제공한다.

```text
상태
마지막 성공 시각
마지막 오류
계산 시간
Contribution 개수
캐시 hit/miss
데이터 지연
```

---

# 16. 버전 호환과 migration

각 Module Profile에는 다음을 저장한다.

```text
ModuleId
ModuleSchemaVersion
```

모듈 버전 변경 시:

```csharp
public interface IChartModuleProfileMigrator
{
    JsonObject Migrate(
        int fromVersion,
        int toVersion,
        JsonObject source);
}
```

마이그레이션 실패 시 기본값으로 조용히 덮지 않는다. 오류를 표시하고 원본 설정을 보존한다.

---

# 17. CI 보호 규칙

필수 정적 검사:

```text
Rendering → Modules 참조 금지
Rendering → DataSources 참조 금지
Module → Rendering/SkiaSharp 참조 금지
Engine → WinForms/SkiaSharp 참조 금지
App 외부 → WinForms 타입 노출 금지
Renderer source에 업무 기능명 금지
MainForm에 module ID별 switch 금지
```

필수 동작 검사:

```text
Module On/Off
Profile 저장·복원
파라미터 변경 영향 범위
Panel 이동·높이 복원
Renderer 무변경 신규 모듈 추가
Fault isolation
비활성 모듈 계산 0
steady-state 할당
```

---

# 18. 신규 기능 구현 표준 절차

```text
1. Capability Matrix에서 기능 선택
2. 데이터·계산·출력·상호작용·저장 요구 명시
3. 기존 Primitive 조합 가능 여부 확인
4. Module 클래스 작성
5. 설정 모델과 Profile schema 작성
6. Property Descriptor 작성
7. Command Descriptor 작성
8. 데이터 요구사항 작성
9. 계산과 증분 상태 작성
10. Contribution 작성
11. Registry 등록
12. On/Off 검증
13. 저장·복원 검증
14. 독립 기능 검증
15. 통합 Renderer 결과 검증
16. 성능 검증
17. 복원 체크포인트 생성
18. Capability Matrix 상태 갱신
```

신규 기능 추가 시 기본 변경 파일:

```text
새 Module 파일
새 설정 파일
새 Module 테스트
Registry 등록 파일
기본 Profile 파일
```

원칙적으로 변경 금지:

```text
SkiaChartRenderer
MainForm
SymbolRuntime
ChartFrameBuilder
```

---

# 19. 완료 정의

모듈 완료 조건:

```text
독립 구현
독립 자동검증
범용 Registry 등록
범용 On/Off
공용 Property Inspector
프로필 저장·복원
Renderer 기능명 참조 0
비활성 계산 0
부분 invalidation
오류 격리
성능 기준 통과
```

플랫폼 완료 조건:

```text
기존 8개 지표를 Renderer 변경 없이 모듈화
비교종목을 같은 구조로 연결
독립 서브차트를 같은 구조로 연결
작도 도구를 같은 구조로 연결
DSL 신호를 같은 구조로 연결
고빈도 호가를 같은 구조로 연결
시장분석 외부 패널을 같은 구조로 연결
```

이 서로 다른 대표 기능들이 모두 동일 경로로 연결되면 플랫폼 구조가 검증된 것으로 본다.
