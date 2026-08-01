# ChartKit UI Metadata 및 Property 변경 표준

상태: P1-E 구현 표준  
기준 아키텍처: Architecture Baseline 1.0

## 1. 목적

기능 모듈이 추가될 때 App의 메뉴, 툴바, Property Form에 기능 이름별 코드를 추가하지 않는다.

모든 UI 표시는 다음 경로를 사용한다.

```text
IChartCommandProvider / IChartPropertyProvider
→ ChartModuleHost 소유권 검증
→ ChartKit.UiModel 공용 투영
→ Context Menu / Quick Toolbar / Property Inspector
```

실제 WinForms 컨트롤은 이 공용 모델을 표시하는 어댑터일 뿐이다.

## 2. 프로젝트 경계

```text
ChartKit.UiModel
├─ ChartKit.Modules.Abstractions 참조
└─ ChartKit.ModuleHost 참조
```

금지 참조:

```text
ChartKit.App
ChartKit.Rendering
ChartKit.Scene
ChartKit.Composition
ChartKit.Persistence
ChartKit.DataSources
SkiaSharp
System.Windows.Forms
```

UI 모델은 Renderer, 데이터 공급자, 파일 저장소를 직접 호출하지 않는다.

## 3. Host 메타데이터 수집

`ChartModuleHost`는 다음 소유권이 포함된 메타데이터를 제공한다.

```text
ChartHostedCommandDescriptor
ChartHostedPropertyDescriptor
```

각 항목에는 다음 정보가 포함된다.

```text
ModuleId
InstanceId
Module DisplayName
Module Category
Enabled / Active / Fault 상태
원본 CommandDescriptor 또는 PropertyDescriptor
```

검증 규칙:

```text
중복 CommandId 거부
중복 PropertyId 거부
빈 ID·표시명·Category 거부
Placement=None 명령 거부
Provider 구현과 Capability 선언 불일치 거부
모듈별 오류 격리
```

## 4. 공용 UI 투영

`ChartModuleUiCatalog`는 같은 메타데이터에서 다음을 생성한다.

```text
ContextMenuItems
QuickToolbarItems
InspectorProperties
```

모든 모듈 인스턴스에는 공용 On/Off 명령이 자동 생성된다.

```text
CommandId: chart.module.toggle
Kind: ModuleToggle
Check state: ChartModuleProfile.IsEnabled
Placement: ContextMenu | QuickToolbar
```

모듈별 명령은 `ChartCommandDescriptor.Placement`에 따라 필요한 표면에만 투영한다.

정렬은 다음 순서로 결정적이어야 한다.

```text
Category
DisplayName
InstanceId
CommandId 또는 PropertyId
```

## 5. Selection

`ChartSelectionService`는 다음 선택 경로를 하나로 통합한다.

```text
모듈 목록 선택
범례 선택
차트 Primitive 선택
컨텍스트 메뉴의 속성 선택
```

선택 키는 `ChartObjectIdentity`이다.

```text
ModuleId
InstanceId
ObjectId
```

Inspector는 선택 Object의 `InstanceId`를 통해 소유 모듈 Property를 표시한다.

## 6. Property 메타데이터

`ChartPropertyDescriptor`는 다음을 선언한다.

```text
PropertyId
DisplayName
Category
ValueKind
Value
ChangeImpact
Storage
IsReadOnly
Minimum / Maximum
AllowedValues
```

지원 ValueKind:

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
DateRange
Collection
```

지원 Storage:

```text
Parameters
Style
PersistentState
Placement
ZIndex
```

## 7. Property 변경 명령

모든 변경은 다음 단일 경로를 사용한다.

```text
ChangeChartPropertyCommand
→ PropertyDescriptor 조회
→ ValueKind·범위·허용값 검증
→ ChartModuleProfile 방어 복사
→ 지정 Storage만 변경
→ ChartModuleHost.UpsertProfile
→ ChartChangeImpact 반환
```

기능별 `switch`는 금지한다. 허용되는 분기는 공용 `ValueKind`와 `Storage`에 대한 분기뿐이다.

동일 값은 다음 결과를 반환한다.

```text
Succeeded = true
Changed = false
ChangeImpact = None
```

## 8. 최소 invalidation

Property가 선언한 `ChartChangeImpact`를 그대로 반환한다.

```text
None
RedrawOnly
RebuildVisuals
RecalculateModule
RebuildLayout
ReloadData
RestartSubscription
RebuildWorkspace
```

하위 영향의 변경을 상위 영향으로 임의 승격하지 않는다.

예:

```text
색상 변경      → RedrawOnly
수치 시각 변경 → RebuildVisuals
기간 변경      → RecalculateModule
패널 이동      → RebuildLayout
```

## 9. PlatformProbeModule 기준

```text
level      Decimal / Parameters / RebuildVisuals
amplitude  Decimal(min=0) / Parameters / RebuildVisuals
stroke     Color / Style / RedrawOnly
```

명령:

```text
platform.probe.inspect
→ ContextMenu / QuickToolbar / PropertyInspector
```

## 10. 완료 기준

```text
[ ] 같은 Descriptor에서 Context Menu와 Quick Toolbar 생성
[ ] Module toggle check 상태가 Profile과 일치
[ ] 차트 Object 선택이 소유 모듈 Inspector로 연결
[ ] Property 변경이 Profile과 모듈 런타임에 반영
[ ] 변경 결과가 RenderPlan까지 도달
[ ] 범위 밖 값 거부
[ ] 동일 값 no-op
[ ] 정확한 ChartChangeImpact 반환
[ ] 비활성 모듈 Property 열람 가능
[ ] 결정적 메타데이터 정렬
[ ] WinForms/Renderer 참조 0
[ ] 기존 실시간·틱 순서 검증 유지
```

## 11. 이후 단계

P1-E 통과 후 App 어댑터는 다음만 담당한다.

```text
ChartUiCommandItem → ToolStrip/MenuItem
ChartUiPropertyItem → 공용 Editor
Selection event → ChartSelectionService
Editor value → ChangeChartPropertyCommand
ChartChangeImpact → 최소 갱신 스케줄러
```

App 어댑터에서도 `PlatformProbeModule`, RSI, MACD 등의 기능 이름별 분기를 추가하지 않는다.
