# JMA Module Parity Standard

상태: P2-5 검증 후보  
기준 브랜치: `csharp/module-platform-p2-jma-parity`

## 1. 목적

기존 `JmaIndicator`를 제거하지 않고 신규 `JmaModule`을 병렬 실행하여 계산·표시·설정 저장의 동일성을 검증한다.

## 2. Legacy 계산 계약

```text
Period  14
Phase   50, 허용 범위 -100..100
Power    2
Source   Close
Panel    price.main
Values   Value / Up / Down / Slope
```

초기 상태:

```text
e0       first close
e1       0
e2       0
lastJma  first close
```

평활식:

```text
beta  = 0.45 * (period - 1) / (0.45 * (period - 1) + 2)
alpha = beta ^ power
```

초기 `period`개 봉은 누적 종가 평균을 소수점 4자리로 반올림한다. 이후 값은 `e2 + lastJma`를 소수점 4자리로 반올림한다. Slope는 직전 JMA 대비 변화율을 백분율 소수점 1자리로 반올림한다. 이 반올림 시점은 Legacy와 동일해야 한다.

## 3. 모듈 계약

```text
ModuleId       indicator.jma
DefaultPanel   price.main
Data           PrimarySymbol.OHLCV
Computation    active module only
Contributions  jma.up / jma.down
Primitive      Polyline
```

`Value`는 매 봉 `Up` 또는 `Down` 중 하나와 동일하므로 계산 parity에는 포함하지만 별도 중복선을 출력하지 않는다.

## 4. Profile 계약

Parameters:

```text
period   integer 1..10000
phase    integer -100..100
power    integer 1..10000
```

Style:

```text
jma.up.stroke
jma.down.stroke
```

Property 변경 영향:

```text
period / phase / power  RecalculateModule
색상                     RedrawOnly
```

## 5. 증분 처리

```text
첫 Snapshot                  전체 계산
동일 Sequence 마지막 봉 변경  RestoreState + UpdateLast
Sequence + 1                 SaveState + Append
중간 데이터 변경·rolling 이동 전체 재계산
동일 Snapshot                계산 생략
```

입력 배열의 순서를 그대로 사용하며 정렬·중복 제거를 수행하지 않는다.

## 6. 자동검증

필수 표식:

```text
csharp_jma_full_parity=PASS
csharp_jma_update_parity=PASS
csharp_jma_append_parity=PASS
csharp_jma_rebuild_parity=PASS
csharp_jma_disabled_zero=PASS
csharp_jma_contributions=PASS
csharp_jma_panel_contract=PASS
csharp_jma_style_override=PASS
csharp_jma_parameter_change=PASS
csharp_jma_reference_boundary=PASS
csharp_jma_module_contracts=PASS
```

App 필수 표식:

```text
csharp_app_jma_module_data=PASS
csharp_app_jma_module_parameters=PASS
csharp_app_jma_module_style=PASS
csharp_app_jma_panel_contract=PASS
csharp_app_jma_module_roundtrip=PASS
```

## 7. 화면 검증

기본 `14/50/2`에서 신규 Up/Down 선은 Legacy JMA 위치와 겹쳐야 한다. `7/-25/3`으로 변경하면 신규 선이 분리되어 즉시 재계산되어야 하며, 기본값으로 복귀하면 다시 겹쳐야 한다.

## 8. 비목표

이번 단계에서는 다음을 수행하지 않는다.

```text
Legacy JmaIndicator 제거
DefaultIndicatorFactory 제거
SymbolRuntime 변경
Renderer 기능별 분기 추가
시장 데이터·실시간 경로 변경
다른 지표 모듈 이전
```
