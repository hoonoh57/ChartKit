# ChartKit 개별 모듈 파일 작성·연결 표준

상태: **Architecture Freeze Candidate 1 — Mandatory Standard**  
문서 버전: `1.0-rc1`  
작성일: 2026-08-01

이 문서는 ChartKit에 신규 기능 파일을 만들 때 반드시 지켜야 하는 생성·등록·렌더링 연결·UI 연결·저장·검증 절차를 정의한다.

핵심 원칙:

> 기능의 내부 구현은 독립 파일 또는 기능 폴더에 둔다. 플랫폼 연결 정보는 항상 `<Feature>Module.cs` 파일 상단에서 확인할 수 있어야 한다.

---

# 1. 적용 대상

다음 기능의 진입 파일은 모두 이 표준을 따른다.

```text
기술적 지표
시장지수·비교종목
복수 종목 서브차트
작도 도구
마우스 상호작용
전략·신호
호가·체결
시장분석
매매대상 후보
주문·포지션
DSL 기반 기능
외부 패널·상태·명령 제공 기능
```

적용 파일 이름:

```text
*Module.cs
```

예:

```text
RsiModule.cs
SuperTrendModule.cs
SymbolComparisonModule.cs
FibonacciModule.cs
StrategySignalModule.cs
OrderBookModule.cs
CandidateSymbolModule.cs
```

---

# 2. 기능 단위와 프로젝트 단위

모든 기능을 별도 `.csproj`로 만들지 않는다.

```text
개별 기능           → 개별 Module 클래스 또는 작은 기능 폴더
유사 기능군         → 하나의 Modules 프로젝트
특수 런타임·의존성 → 별도 프로젝트 또는 외부 플러그인
```

권장 예:

```text
ChartKit.Modules.Indicators/
  RsiModule.cs
  MacdModule.cs
  SuperTrendModule.cs

ChartKit.Modules.Drawing/
  HorizontalLineModule.cs
  TrendLineModule.cs
  Fibonacci/
    FibonacciModule.cs
    FibonacciSettings.cs
    FibonacciState.cs
    FibonacciCalculator.cs
    FibonacciVerification.cs
```

기능이 커져 여러 파일로 분리되더라도 플랫폼 연결 진입점은 반드시 `<Feature>Module.cs` 하나로 유지한다.

---

# 3. 파일 상단 연결 계약 — 필수

모든 `*Module.cs` 파일은 `using`, namespace, class 선언보다 앞에 다음 형식의 연결 계약을 둔다.

```csharp
// <chart-module>
// Module-Id: indicator.rsi
// Module-Class: RsiModule
// Module-Category: Indicators
// Registration: registry.Register<RsiModule>()
// Profile-Key: modules[].instanceId
// Data-Requirements: PrimarySymbol.OHLCV
// Capabilities: Computation, Visual, Properties, Commands
// Contributions: Polyline, ReferenceLine
// Default-Panel: indicator.rsi
// Renderer-Path: ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
// UI-Path: CommandDescriptor/PropertyDescriptor -> ContextMenu/QuickButton/PropertyInspector
// Persistence: ChartModuleProfile.Parameters, ChartModuleProfile.Style
// Verification: RsiModuleVerification
// </chart-module>
```

필수 키:

```text
Module-Id
Module-Class
Module-Category
Registration
Profile-Key
Data-Requirements
Capabilities
Contributions
Default-Panel
Renderer-Path
UI-Path
Persistence
Verification
```

값이 해당되지 않는 경우 키를 삭제하지 않고 `None`을 기록한다.

예:

```text
Data-Requirements: None
Default-Panel: None
Persistence: None
```

## 3.1 상단 계약의 목적

파일 하나만 열어도 다음 질문에 즉시 답할 수 있어야 한다.

```text
이 기능의 고유 ModuleId는 무엇인가?
어떤 Registry 코드로 생성되는가?
ChartProfile 어디에 On/Off와 설정이 저장되는가?
어떤 시장 데이터가 필요한가?
어떤 capability 인터페이스를 구현하는가?
어떤 표준 Contribution을 생성하는가?
어떤 패널과 축을 요구하는가?
Renderer에는 어떤 경로로 도달하는가?
컨텍스트 메뉴·퀵버튼·Property Inspector에는 어떻게 나타나는가?
어떤 자동검증이 이 기능을 보호하는가?
```

상단 계약은 설명용 주석이 아니라 Module Catalog와 CI가 읽는 정형 메타데이터다.

---

# 4. 클래스 선언 표준

모든 기능 진입 파일은 `IChartModule`을 구현한다.

기능이 제공하는 역할만 capability 인터페이스로 추가한다.

```csharp
public sealed class RsiModule :
    IChartModule,
    IDataRequirementProvider,
    IChartComputationModule,
    IChartVisualProvider,
    IChartPropertyProvider,
    IChartCommandProvider
{
}
```

역할별 의미:

```text
IChartModule                  등록, 수명주기, On/Off
IDataRequirementProvider     필요한 symbol/timeframe/market data 선언
IChartComputationModule      과거·실시간 데이터를 계산 상태로 변환
IChartVisualProvider         표준 Contribution 생성
IChartPropertyProvider       공용 Property Inspector 스키마 제공
IChartCommandProvider        컨텍스트 메뉴·퀵버튼 명령 제공
IChartInteractionProvider    마우스·키보드 이벤트 처리
IChartPersistentStateProvider 작도점 등 종목별 상태 저장
```

기능과 무관한 인터페이스를 형식적으로 구현하지 않는다.

---

# 5. ModuleDefinition — 필수

각 Module 클래스는 자신의 정적 정의를 같은 파일에서 제공한다.

```csharp
public static ChartModuleDefinition Definition { get; } =
    new(
        moduleId: "indicator.rsi",
        displayName: "RSI",
        category: "Indicators",
        description: "상대강도지수와 기준선을 표시합니다.",
        defaultPanelId: "indicator.rsi",
        defaultEnabled: true,
        capabilities:
            ChartModuleCapabilities.Computation |
            ChartModuleCapabilities.Visual |
            ChartModuleCapabilities.Properties |
            ChartModuleCapabilities.Commands,
        supportedPrimitiveKinds:
        [
            ChartPrimitiveKind.Polyline,
            ChartPrimitiveKind.ReferenceLine
        ]);

public ChartModuleDefinition ModuleDefinition => Definition;
```

상단 계약과 `Definition`의 다음 값은 일치해야 한다.

```text
Module-Id       ↔ Definition.ModuleId
Module-Class    ↔ 실제 클래스명
Module-Category ↔ Definition.Category
Capabilities    ↔ 구현 capability와 Definition
Contributions   ↔ supportedPrimitiveKinds와 실제 출력
Default-Panel   ↔ 기본 ChartModuleProfile
```

---

# 6. Registry 연결 표준

Registry는 기능별 계산·렌더링 분기를 갖지 않는다.

허용:

```csharp
registry.Register<RsiModule>();
registry.Register<MacdModule>();
registry.Register<FibonacciModule>();
```

금지:

```csharp
switch (moduleId)
{
    case "indicator.rsi":
        DrawRsi();
        break;
}
```

Registry의 역할:

```text
ModuleId 유일성 검증
Definition 보관
Factory 보관
Module 인스턴스 생성
Module Catalog 제공
```

Registry 이후 모든 호출은 공통 인터페이스를 통해 수행한다.

---

# 7. Renderer 연결 표준

개별 Module은 Renderer나 SkiaSharp를 직접 호출하지 않는다.

금지:

```text
SKCanvas
SKPaint
SKPath
SkiaChartRenderer
WinForms Control
픽셀 좌표 직접 계산
```

허용되는 연결 경로는 하나다.

```text
<Feature>Module.BuildContributions()
→ ContributionSet
→ SceneCompiler
→ ChartRenderPlan
→ SkiaChartRenderer
```

예:

```csharp
public void BuildContributions(
    ChartVisualContext context,
    IChartContributionWriter writer)
{
    writer.RequestPanel(...);
    writer.AddPolyline(...);
    writer.AddReferenceLine(...);
}
```

Renderer는 `ModuleId`별 분기를 하지 않고 다음 범용 batch만 처리한다.

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

새 기능이 기존 Primitive 조합으로 표현되지 않을 경우, 기능 전용 Draw API를 추가하지 않는다. 두 개 이상의 독립 기능에서 재사용 가능한 범용 Primitive인지 먼저 검증한다.

---

# 8. UI 연결 표준

개별 Module은 메뉴, 퀵버튼, PropertyGrid Control을 직접 만들지 않는다.

```text
Module
→ ChartCommandDescriptor
→ Context Menu / Quick Toolbar

Module
→ ChartPropertyDescriptor
→ Common Property Inspector
```

모든 UI 조작은 동일한 상태를 변경한다.

```text
컨텍스트 메뉴 On
= 퀵버튼 On
= Property Inspector Enabled=True
= ChartModuleProfile.IsEnabled=True
```

표준 명령:

```csharp
moduleHost.SetEnabled(instanceId, enabled);
propertyService.Change(instanceId, propertyId, value);
commandBus.Execute(commandId, targetId);
```

기능별 `ToggleRsi`, `OpenMacdProperties` 같은 App 메서드를 만들지 않는다.

---

# 9. 저장 연결 표준

설정과 상태를 분리한다.

Chart Template/Profile:

```text
타임프레임
On/Off
파라미터
스타일
패널 배치
축 설정
퀵버튼 고정
```

종목별 Instance State:

```text
추세선 기준점
피보나치 기준점
종목 메모
특정 종목의 숨김 상태
마지막 viewport
```

저장하지 않는 것:

```text
SKPaint/SKPath/SKCanvas
픽셀 좌표
ChartRenderPlan
GPU 리소스
렌더러 캐시
```

---

# 10. 파일 생성 표준 절차

신규 기능은 다음 순서로만 생성한다.

```text
1. Feature Capability Matrix의 ID와 범주 확인
2. 기능군 프로젝트 또는 폴더 결정
3. templates/ChartModule.template.cs 복사
4. 파일명을 <Feature>Module.cs로 변경
5. 파일 상단 <chart-module> 계약 작성
6. ModuleDefinition 작성
7. 필요한 capability 인터페이스만 선택
8. 데이터 요구사항 선언
9. 계산 로직과 런타임 상태 작성
10. Property Schema와 Command Descriptor 작성
11. BuildContributions로 표준 출력 생성
12. Registry에 등록
13. 기본 ChartModuleProfile 추가
14. On/Off, 저장·복원, 오류 격리 테스트
15. 모듈별 Verification 작성
16. 헤더 및 참조 방향 CI 통과
17. 성능 게이트 통과
18. Feature Capability Matrix 상태 갱신
```

이 절차를 거치지 않은 파일은 기능 완료로 처리하지 않는다.

---

# 11. 파일 구조 표준

단순 기능:

```text
RsiModule.cs
```

복잡한 기능:

```text
Fibonacci/
  FibonacciModule.cs       플랫폼 연결 진입점
  FibonacciSettings.cs     저장 파라미터
  FibonacciState.cs        런타임·종목별 상태
  FibonacciCalculator.cs   순수 계산
  FibonacciVerification.cs 자동검증
```

플랫폼 연결 코드는 항상 `<Feature>Module.cs`에 둔다. Calculator나 Settings 파일에 Registry·UI·Renderer 연결 코드를 분산하지 않는다.

---

# 12. Module Catalog와 진단

상단 계약과 ModuleDefinition에서 다음 목록을 자동 생성한다.

```text
ModuleId
클래스와 파일
기능군
데이터 요구
capability
Contribution/Primitive
기본 패널
On/Off
Profile 위치
Verification
최근 계산 시간
최근 composition 시간
primitive 수
오류 상태
```

실행 중 연결 경로:

```text
Data Requirement
→ Computation
→ Contribution
→ Scene Primitive
→ Render Batch
```

각 단계의 개수와 마지막 처리 시간을 Module Diagnostics에서 확인할 수 있게 한다.

---

# 13. CI 강제 규칙

`*Module.cs` 파일은 다음 조건을 만족하지 않으면 CI가 실패한다.

```text
파일 첫 80줄 안에 <chart-module> ... </chart-module> 존재
필수 13개 키 존재
Module-Class와 실제 파일/클래스 이름 일치
IChartModule 구현
ChartModuleDefinition Definition 존재
ModuleDefinition 공개
상단 Renderer-Path가 표준 경로와 일치
SKCanvas/SKPaint/SKPath 직접 참조 없음
WinForms Control 직접 참조 없음
```

플랫폼 구현 전에는 대상 파일이 없으면 검사가 PASS한다. 첫 Modules 프로젝트가 추가되는 순간부터 모든 `*Module.cs`에 자동 적용한다.

검사 스크립트:

```text
scripts/verify_chart_module_headers.ps1
```

---

# 14. Definition of Done

신규 기능은 다음을 모두 충족해야 완료다.

```text
[ ] Feature Matrix ID 존재
[ ] <Feature>Module.cs 존재
[ ] 파일 상단 연결 계약 존재
[ ] ModuleDefinition과 상단 계약 일치
[ ] Registry 등록
[ ] Profile On/Off
[ ] 표준 Contribution 생성
[ ] Renderer 기능별 수정 0
[ ] Context Menu 자동 생성
[ ] Quick Button 고정 가능
[ ] Property Inspector 자동 표시
[ ] Profile 저장·복원
[ ] 독립 Verification
[ ] 비활성 계산·구독·Contribution 0
[ ] 오류 격리
[ ] 성능 기준 통과
[ ] CI 통과
```

이 표준은 모든 신규 기능 파일의 생성 기준이며, 예외는 아키텍처 검토와 문서 개정 없이 허용하지 않는다.
