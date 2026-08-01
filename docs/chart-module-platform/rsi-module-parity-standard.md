# RSI Module Legacy Parity Standard

상태: P2-2 구현 표준  
대상 모듈: `indicator.rsi`  
Legacy 기준: `ChartKit.Engine.RsiIndicator`

## 목적

기존 중앙 고정 RSI 계산·서브패널 출력은 유지한 채 독립 `RsiModule`을 모듈 플랫폼에 병렬 연결한다. 신규 경로가 전체 계산, 동일 봉 갱신, 새 봉 추가, 고정 길이 이동 재계산에서 Legacy 결과와 일치한 뒤에만 이후 제거 단계를 검토한다.

## Legacy 기준값

```text
RSI period       14
Signal period     9
Upper             70
Lower             30
Panel index        1
```

RSI는 Wilder 평균 상승폭·하락폭을 사용하며 Signal은 유효 RSI 값의 단순 이동평균이다.

## 표준 연결

```text
Primary OHLCV
→ active IChartDataModule
→ RsiSeriesRuntime
→ immutable RsiValuePoint snapshot
→ RsiModule.BuildContributions
→ SceneCompiler
→ ChartRenderPlan
→ generic Skia renderer
```

계산은 렌더 스레드에서 수행하지 않는다. `BuildContributions`는 이미 계산된 불변 값만 읽는다.

## 출력 계약

```text
rsi.value   Polyline
rsi.signal  Polyline
rsi.upper   Polyline
rsi.lower   Polyline
panel       indicator.1
```

각 점의 X는 Snapshot 봉 배열의 절대 인덱스다. NaN 구간은 Renderer에서 선분 단절로 처리한다.

## Style 우선순위

다중 시리즈 모듈은 다음 범용 우선순위를 사용한다.

```text
ChartContribution 기본 Style
→ Module 공통 Profile Style
→ <ObjectId>.<style-key> Profile override
```

예:

```json
{
  "stroke": "#FFFFFF",
  "strokeWidth": 1.5,
  "rsi.value.stroke": "#FF9800",
  "rsi.signal.stroke": "#E91E63",
  "rsi.upper.stroke": "#00BCD4",
  "rsi.lower.stroke": "#CDDC39"
}
```

Renderer는 RSI나 ObjectId 의미를 알지 않는다. SceneCompiler가 최종 `RenderPrimitiveStyle`만 생성한다.

## Profile Property

```text
period                    Parameters / RecalculateModule
signalPeriod              Parameters / RecalculateModule
upper                     Parameters / RebuildVisuals
lower                     Parameters / RebuildVisuals
rsi.value.stroke          Style / RedrawOnly
rsi.signal.stroke         Style / RedrawOnly
rsi.upper.stroke          Style / RedrawOnly
rsi.lower.stroke          Style / RedrawOnly
```

`upper > lower`, period·signalPeriod `1..10000`, threshold `0..100`을 강제한다.

## 증분 경로

```text
첫 Snapshot                         Full calculation
동일 배열·동일 마지막 Sequence 변경  UpdateLast
기존 배열 + Sequence 1개 증가         Append
고정 길이 이동·중간 변경              Full rebuild
동일 데이터                          Unchanged no-op
```

입력 봉 순서를 그대로 사용하며 정렬·중복 제거를 수행하지 않는다.

## 비활성 계약

비활성 RSI 모듈은 다음이 모두 0이어야 한다.

```text
데이터 전달 대상
계산 호출
Contribution
RenderPlan primitive
```

## 자동검증

`RsiModuleParityVerification`은 다음을 강제한다.

```text
ModuleDefinition·OHLCV requirement·Property metadata
Legacy 전체 RSI·Signal parity
동일 봉 Update parity
새 봉 Append parity
고정 길이 Rebuild parity
비활성 계산 0
4개 Contribution identity·panel·point count
Upper 70 / Lower 30
Contribution 기본 Style
Module 공통 Style override
ObjectId별 Style override
Period 5 / Signal 3 / Upper 80 / Lower 20 변경
Modules.Indicators 참조 경계
Release configuration
```

App self-test는 Controller 등록, On/Off, 데이터 계산, Property 변경, 네 RenderPlan 시리즈, Style, Profile 저장·재실행 복원을 검증한다.

## 이번 단계에서 금지

```text
Legacy RsiIndicator 제거
DefaultIndicatorFactory 변경
SymbolRuntime의 Legacy RSI 제거
Renderer의 RSI 전용 분기
MainForm의 RSI 전용 메뉴·설정 Form
시장 데이터·실시간 처리 변경
동적 PanelGraph 구현으로 범위 확대
```

## 완료 기준

```text
chart_module_header_contract=PASS module_files=3
Release build 오류 0 / 신규 C# 경고 0
csharp_rsi_module_contracts=PASS
csharp_engine_verification=PASS
csharp_app_rsi_module_roundtrip=PASS
csharp_app_self_test=PASS
화면에서 Legacy RSI와 신규 RSI(14,9) 겹침 확인
Upper·Lower·Signal 색상과 On/Off 확인
재실행 후 On·Property·Style 복원 확인
```
