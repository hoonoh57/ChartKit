# ChartKit Architecture Baseline 1.0

확정일: 2026-08-01  
승인 상태: **Approved**  
기준 기능 커밋: `bd21cd849d44c5e4d892701326377b825f3af67d`  
기준 문서 커밋: `7d4fa1c8268ebe11a59875a9d4a1750586b4a31c`  
확정 브랜치: `csharp/module-platform-baseline`

---

# 1. 확정 목적

ChartKit의 기능 수가 증가해도 Renderer, MainForm, SymbolRuntime, ChartFrameBuilder에 기능별 분기가 누적되지 않도록 범용 모듈 플랫폼을 공식 기준으로 확정한다.

핵심 불변식:

```text
기능의 내부 복잡도는 깊어질 수 있다.
차트 연결 복잡도는 항상 1이어야 한다.
```

표준 연결 경로:

```text
<Feature>Module.cs
→ Module Registry
→ ChartProfile On/Off
→ 표준 Contribution
→ SceneCompiler
→ immutable ChartRenderPlan
→ 범용 SkiaChartRenderer
```

---

# 2. 확정된 설계 문서

다음 문서를 Baseline 1.0의 구성 문서로 확정한다.

```text
docs/chart-module-platform/architecture-constitution.md
docs/chart-module-platform/feature-capability-matrix.md
docs/chart-module-platform/implementation-roadmap.md
docs/chart-module-platform/module-file-standard.md
docs/chart-module-platform/templates/ChartModule.template.cs
scripts/verify_chart_module_headers.ps1
```

위 문서 내부의 `Architecture Freeze Candidate 1` 표기는 작성 당시의 후보 버전 표식이며, 이 승인 기록에 의해 해당 내용 전체가 Baseline 1.0으로 비준된다.

---

# 3. 기능 단위 확정

모든 기능을 개별 `.csproj`로 분리하지 않는다.

```text
개별 기능
→ 개별 <Feature>Module.cs 또는 작은 기능 폴더

유사 기능군
→ ChartKit.Modules.Indicators 등 하나의 기능군 프로젝트

특수 런타임·독립 배포·대형 의존성
→ 별도 프로젝트 또는 플러그인
```

플랫폼 연결 진입점은 항상 `<Feature>Module.cs`다.

---

# 4. 파일 상단 연결 계약

모든 신규 `*Module.cs` 파일은 `using`, namespace, class 선언보다 앞에 `<chart-module>` 계약을 기록한다.

필수 항목:

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

표준 Renderer 경로:

```text
ContributionSet -> SceneCompiler -> ChartRenderPlan -> SkiaChartRenderer
```

표준 UI 경로:

```text
CommandDescriptor/PropertyDescriptor
-> ContextMenu/QuickButton/PropertyInspector
```

`verify_chart_module_headers.ps1`가 이를 CI에서 강제한다.

---

# 5. UI 확정

개별 기능은 WinForms 메뉴나 PropertyGrid를 직접 생성하지 않는다.

모듈이 제공하는 동일한 메타데이터를 사용해 다음 UI를 생성한다.

```text
Context Menu
Quick Button
공용 Property Inspector
Module Catalog
Module Diagnostics
```

모든 UI 조작은 동일한 ModuleHost·CommandBus·PropertyService 경로를 사용한다.

---

# 6. Renderer 확정

Renderer는 기능명을 알지 않는다.

금지 예:

```text
DrawRsi
DrawMacd
DrawOrderBook
DrawFibonacci
DrawStrategySignal
```

허용 입력은 범용 Render Primitive Batch다.

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

새 Primitive는 최소 두 개 이상의 독립 기능에서 재사용 가능함을 증명해야 한다.

---

# 7. 상태·저장 확정

저장 대상:

```text
ChartProfile
ChartModuleProfile
패널·축·레이아웃
모듈 파라미터·스타일
작도 기준점
종목별 상태
퀵버튼 고정
마지막 viewport
```

저장 금지:

```text
SKCanvas
SKPaint
SKPath
픽셀 좌표 RenderPlan
GPU 리소스
렌더러 내부 캐시
```

---

# 8. 성능 확정

```text
비활성 모듈 데이터 구독 0
비활성 모듈 계산 0
비활성 모듈 Contribution 0
렌더 스레드에서 모듈 계산 금지
변경된 모듈만 invalidation
타입별 batch 렌더링
RenderPlan 불변·교체 방식
```

등록된 전체 모듈 수가 아니라 활성 모듈과 변경된 Contribution에 비례해 비용이 증가해야 한다.

---

# 9. 변경 통제

Baseline 1.0 이후 다음 변경은 아키텍처 변경 검토 없이 허용하지 않는다.

```text
Renderer가 개별 모듈 참조
모듈이 SkiaSharp/WinForms 직접 참조
MainForm 기능별 On/Off 분기
기능별 전용 Property Form 남용
파일 상단 연결 계약 생략
기능 이름별 ChartFrameBuilder 분기
특정 기능 하나만을 위한 Renderer API
```

변경 제안에는 다음 근거가 필요하다.

```text
기존 계약으로 구현 불가능한 이유
대안 검토
범용 재사용 근거
성능·메모리 영향
Profile migration 계획
CI 보호 규칙
```

---

# 10. 첫 구현 단계

Baseline 1.0 승인 후 첫 구현은 실제 지표가 아닌 `PlatformProbeModule.cs` 수직 검증이다.

검증 범위:

```text
파일 상단 연결 계약
Registry 등록
Profile On/Off
숫자·색상 Property
Context Menu
Quick Button
Property Inspector
Polyline Contribution
SceneCompiler
RenderPlan
Renderer 표시·삭제
Profile 저장·복원
비활성 계산 0
Fault isolation
```

이 수직 검증을 통과하기 전에는 대규모 지표·작도·DSL 기능을 새 플랫폼에 추가하지 않는다.
