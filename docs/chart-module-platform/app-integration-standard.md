# ChartKit Module Platform App Integration Standard

상태: P1-F 구현 표준  
기준: Architecture Baseline 1.0

## 목적

P1-F는 P1-A부터 P1-E까지 구축한 계약을 실제 `ChartKit.App` WinForms 셸에 연결한다.

```text
ChartProfile
→ Registry
→ ModuleHost
→ UiModel
→ Context Menu / Quick Toolbar / Property Inspector
→ Property mutation
→ Composition
→ RenderPlan
→ Profile atomic save
```

App은 모듈의 기능 이름을 해석하지 않는다. App은 `ChartUiCommandItem`, `ChartUiPropertyItem`, `ChartChangeImpact`, `ChartRenderPlan`만 소비한다.

## Composition Root

`ChartModulePlatformController`가 App의 단일 플랫폼 조립 지점이다.

담당 범위:

- 생산 모듈 Registry 등록
- ChartProfile 로드와 기본 모듈 인스턴스 보강
- 등록된 Profile을 ModuleHost에 적용
- 미등록 moduleId Profile 보존
- Context Menu·Quick Toolbar·Inspector 모델 생성
- 범용 모듈 On/Off 실행
- Property 변경 검증과 Profile 반영
- Contribution 재수집과 RenderPlan 재합성
- UTF-8/BOM 없는 원자적 Profile 저장

`MainForm`은 Registry 또는 모듈 인스턴스를 직접 다루지 않는다.

## Profile 경로

기본 경로:

```text
%LOCALAPPDATA%\ChartKit\chart-profile.json
```

실행 옵션:

```text
--profile <path>
```

Self-test는 사용자 Profile과 분리된 임시 디렉터리를 사용한다.

## 시작 순서

기존 시장 데이터 초기 로드와 Profile 초기화를 동시에 실행하지 않는다.

```text
기존 MainForm 초기 Reload 완료
→ reload gate 획득·해제
→ ChartProfile 로드
→ 모듈 Profile 적용
→ layout/interaction 상태 적용
→ Profile timeframe이 다르면 1회 Reload
→ UiModel 투영
```

이를 통해 초기 히스토리 조회와 Profile 기반 주기 변경이 서로 경합하지 않는다.

## 공용 UI 연결

### Context Menu

`ChartUiCatalogSnapshot.ContextMenuItems`를 category 기준으로 그룹화한다.

- 공용 `chart.module.toggle`
- 모듈이 제공한 `ChartCommandDescriptor`
- check 상태는 `ChartModuleProfile.IsEnabled`
- fault 모듈 명령은 disabled

### Quick Toolbar

`QuickToolbarItems`를 결정적 순서로 ToolStripButton에 투영한다.

App에 특정 지표 또는 전략의 버튼 생성 코드를 추가하지 않는다.

### Property Inspector

WinForms `PropertyGrid`를 모듈 객체에 직접 바인딩하지 않는다.

```text
ChartPropertyValueKind
→ 공용 editor 선택
→ JsonNode 생성
→ ChangeChartPropertyCommand
→ ChartPropertyMutationService
```

현재 공용 editor:

- Boolean: CheckBox
- Integer/Decimal: NumericUpDown
- Enum: ComboBox
- String/Color/LineStyle/Symbol/Timeframe/PanelId/Formula: TextBox
- DateRange/Collection: 읽기 전용 JSON 표시

## Profile에 저장하는 App 상태

```text
layout.visibleBars
layout.infoPanelVisible
interaction.datesVisible
interaction.axesVisible
interaction.legendVisible
interaction.crosshairVisible
timeframe
modules[]
```

변경은 350ms debounce 후 저장하고, FormClosing에서 최종 동기 flush를 수행한다.

## RenderPlan

모듈 On/Off 또는 Property 변경 후 즉시 재합성한다. 선택 종목 Snapshot version이 바뀔 때도 `ChartVisualContext.DataVersion`을 갱신한다.

P1-F에서 App 상태 표시에는 다음 진단을 노출한다.

```text
활성 모듈 수 / 전체 모듈 수
RenderPlan primitive 수
fault 모듈 수
```

기존 Skia 가격 차트 Renderer의 생산 모듈 primitive 출력 이전은 별도 단계다. P1-F는 App에서 `Module Registry → UI → Profile → Composition → RenderPlan` 수직 경로를 완성한다.

## 금지 규칙

- `MainForm`의 moduleId 문자열 switch
- 특정 모듈 전용 Context Menu 생성
- 특정 모듈 전용 설정 Form
- 모듈 객체를 WinForms PropertyGrid에 직접 바인딩
- App에서 Parameters/Style JSON을 임의로 수정
- Renderer에 `platform.probe`, RSI, 전략 등의 기능 이름 분기 추가
- 미등록 모듈 Profile 삭제
- Self-test에서 사용자 Profile 사용

## 검증 기준

App self-test는 다음을 독립 검증한다.

```text
csharp_app_module_profile_load=PASS
csharp_app_module_context_menu=PASS
csharp_app_module_quick_toolbar=PASS
csharp_app_module_property_inspector=PASS
csharp_app_module_property_roundtrip=PASS
csharp_app_module_render_plan=PASS
csharp_app_self_test=PASS
```

수직 검증 절차:

1. 임시 경로에서 기본 Profile 생성
2. Context Menu와 Quick Toolbar 명령 확인
3. 공용 toggle로 모듈 활성화
4. 공용 inspect 선택으로 Property Inspector 생성
5. `level` Property 변경
6. RenderPlan primitive 값 확인
7. layout·interaction과 modules 원자적 저장
8. 새 Controller로 Profile 재로드
9. enabled 상태, Property, layout, RenderPlan 복원 확인

## P1 완료 경계

P1-F가 통과하면 최소 수직 플랫폼은 다음을 모두 실제 App 경로에서 보유한다.

```text
Module Definition
Registry
Host lifecycle
Profile persistence
Contribution
Scene Compiler
RenderPlan
Context Menu
Quick Toolbar
Property Inspector
On/Off
Property mutation
App startup restore
App close save
```

기존 8개 지표의 생산 모듈 이전과 범용 RenderPlan Renderer 연결은 이후 단계에서 진행한다.
