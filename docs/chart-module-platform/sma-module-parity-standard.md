# SMA 모듈 Legacy Parity 표준

상태: **P2-1 구현 후보**  
기준 브랜치: `csharp/module-platform-p2-sma-parity`

## 목적

P2의 첫 지표인 SMA를 기존 `MaIndicator(20, "SMA")`와 병렬 실행해 새 모듈 플랫폼이 실제 지표 계산·설정·렌더링을 수용할 수 있는지 검증한다.

```text
Legacy MaIndicator
New SmaModule
→ 전체 계산 비교
→ 마지막 봉 Update 비교
→ 새 봉 Append 비교
→ 고정 길이 Snapshot 이동 시 재계산 비교
```

이번 단계에서는 Legacy SMA를 제거하지 않는다.

```text
DefaultIndicatorFactory 유지
SymbolRuntime의 기존 MaIndicator 유지
기존 가격 차트 SMA 유지
신규 SmaModule은 기본 비활성
```

8개 지표의 parity가 모두 끝난 뒤에만 중앙 고정 생성 경로 제거를 검토한다.

## 프로젝트 경계

```text
ChartKit.Modules.Indicators
→ ChartKit.Modules.Abstractions
```

금지 참조:

```text
ChartKit.Engine
ChartKit.Rendering
ChartKit.DataSources
ChartKit.App
SkiaSharp
System.Windows.Forms
```

`SmaModule`은 기존 `MaIndicator`를 호출하거나 감싸지 않는다. 독립 계산기로 같은 결과를 재현한다.

## 공용 데이터 계약

`ChartPrimarySeriesSnapshot`은 현재 Snapshot의 OHLCV 봉을 입력 순서 그대로 전달한다.

```text
ChartPrimaryBar
├─ Sequence
├─ Open / High / Low / Close
├─ Volume
└─ IsFinal
```

금지:

```text
시간·가격·Sequence 기준 재정렬
중복 제거
같은 시각 데이터 병합
```

`ChartModuleHost.ApplyPrimarySeries`는 활성·정상 상태의 `IChartDataModule`만 호출한다.

```text
비활성 모듈 계산 0
Fault 모듈 계산 0
모듈별 오류 격리
```

## 계산 상태

`SmaSeriesRuntime`은 Legacy `IncrementalIndicatorBase`와 같은 상태 전이를 사용한다.

```text
첫 Snapshot 또는 비호환 Snapshot
→ 전체 Calculate

동일 Sequence의 마지막 봉 변경
→ 저장 상태 Restore
→ 마지막 봉 재계산

Sequence + 1 새 봉
→ 현재 상태 Save
→ 새 봉 Append

고정 길이 Snapshot 이동 또는 중간 데이터 변경
→ 전체 재계산
```

Period 변경 시 기존 계산 상태를 폐기하고 최신 Primary Snapshot으로 다시 계산한다.

## App 연결

```text
SymbolSnapshot 변경
→ ChartPrimarySeriesSnapshot 생성
→ 백그라운드 Host data update
→ 불변 SMA point 배열 교체
→ Contribution
→ SceneCompiler
→ ChartRenderPlan
→ Skia renderer
```

`BuildContributions`와 `OnPaintSurface`에서는 SMA를 계산하지 않는다. 이미 계산된 Point만 읽는다.

Viewport만 변경된 경우:

```text
계산 없음
RenderPlan 재합성만 수행
```

## Profile과 UI

기본 Profile:

```text
moduleId   indicator.sma
instanceId indicator.sma.default
enabled    false
placement  price.main
period     20
stroke     #FFC107
```

Property:

```text
Period  Integer 1..10000  RecalculateModule
Stroke  Color             RedrawOnly
```

동일 메타데이터로 Context Menu, Quick Toolbar, Property Inspector를 생성한다.

## 자동검증

필수 마커:

```text
csharp_sma_module_release_configuration=PASS
csharp_sma_module_definition=PASS
csharp_sma_full_parity=PASS
csharp_sma_update_parity=PASS
csharp_sma_append_parity=PASS
csharp_sma_rebuild_parity=PASS
csharp_sma_disabled_zero=PASS
csharp_sma_contribution=PASS
csharp_sma_period_change=PASS
csharp_sma_reference_boundary=PASS
csharp_sma_module_contracts=PASS
```

App self-test 추가 마커:

```text
csharp_app_sma_module_data=PASS
csharp_app_sma_module_period=PASS
csharp_app_sma_module_roundtrip=PASS
```

## 화면 검증

Legacy SMA와 신규 SMA를 동시에 켜고 같은 Period와 색상으로 설정한다.

```text
두 선의 봉별 위치 일치
마지막 봉 Update 중 일치
새 봉 Append 후 일치
Period 변경 즉시 신규선 재계산
Off 시 신규선만 제거
재실행 후 Period·색상·On 상태 복원
```

시각 비교를 위해 신규 SMA는 Legacy와 다른 색으로 설정할 수 있지만, 수치 parity 검증은 자동검증 결과를 기준으로 한다.

## 완료 기준

```text
[ ] Release 빌드 오류 0
[ ] 신규 C# 경고 0
[ ] module header contract module_files=2
[ ] 전체 계산 parity
[ ] Update parity
[ ] Append parity
[ ] rolling rebuild parity
[ ] 비활성 계산 0
[ ] Profile Period 변경 재계산
[ ] Profile 저장·복원
[ ] 실제 가격 패널 출력
[ ] 기존 EngineVerification 전체 PASS
[ ] App self-test PASS
[ ] Legacy SMA와 기존 Renderer 회귀 0
```
