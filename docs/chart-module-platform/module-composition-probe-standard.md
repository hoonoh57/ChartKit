# Module Composition 및 Platform Probe 표준

상태: **P1-C 구현 후보**  
기준 브랜치: `csharp/module-platform-p1-composition-probe`

## 목적

P1-C는 다음 수직 경로를 실제 코드로 연결한다.

```text
ChartModuleProfile
→ ChartModuleRegistry
→ ChartModuleHost
→ IChartVisualProvider.BuildContributions
→ ChartHostedContributionSet
→ ChartCompositionService
→ ModuleContributionSet
→ SceneCompiler
→ immutable ChartRenderPlan
```

이 단계는 Renderer 또는 App UI를 변경하지 않는다.

## 책임 경계

### ChartModuleHost

- Profile 적용
- Initialize / Activate / Deactivate / Reset
- 범용 On/Off
- 비활성 모듈 호출 0
- Contribution 소유권과 Primitive 선언 검사
- 모듈별 fault isolation

### ChartCompositionService

- 활성 모듈의 Contribution 집합 수집
- Host 계약을 Scene 계약으로 변환
- SceneCompiler 호출
- 기능 이름별 분기 금지
- Renderer, DataSources, WinForms, SkiaSharp 참조 금지

### SceneCompiler

- 결정적 정렬
- 중복 ChartObjectIdentity 거부
- immutable ChartRenderPlan 생성

## 첫 표준 모듈

`ChartKit.Modules.Platform/PlatformProbeModule.cs`는 플랫폼 수직 경로 검증만 담당한다.

```text
Module-Id: platform.probe
Registration: registry.Register<PlatformProbeModule>()
Contribution: Polyline 1개
Default enabled: false
Default panel: price.main
Parameters: level, amplitude
```

이 모듈은 시장 데이터, Renderer, App UI, SkiaSharp를 참조하지 않는다.

## 완료 기준

```text
[ ] verify_chart_module_headers.ps1 module_files=1 PASS
[ ] ChartKit.Modules.Platform Release 빌드
[ ] ChartKit.Composition Release 빌드
[ ] 비활성 모듈 RenderPlan primitive 0
[ ] 활성 모듈 Contribution 1개가 RenderPlan 1개로 변환
[ ] Profile panel/z-index/parameter 변경 반영
[ ] 동일 입력의 결정적 RenderPlan
[ ] PropertyDescriptor와 CommandDescriptor 노출
[ ] Modules.Platform는 Abstractions만 참조
[ ] Composition은 ModuleHost와 Scene만 참조
[ ] 기존 Renderer, 실시간, 틱 순서, App self-test 회귀 없음
```

## 다음 단계와의 경계

P1-C 이후에도 다음은 별도 단계다.

- Profile JSON 저장과 migration
- App context menu / quick toolbar / property inspector
- 기존 8개 지표의 모듈 이전
- ChartRenderPlan을 현재 Renderer 입력으로 변환하는 production adapter
- Renderer의 기존 ChartFrame 경로 교체
