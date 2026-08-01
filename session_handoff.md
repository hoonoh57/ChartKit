# ChartKit C# 세션 인수인계

작성 시각: 2026-08-02 08:24 KST  
저장소: `hoonoh57/ChartKit`  
로컬 경로: `E:\2026\gpt\vb\sciaChart\ChartKit`  
현재 작업 브랜치: `csharp/module-platform-p2-vwap-parity`  
현재 기능 코드 기준: `22886133c1358d8c3b4c33a1f46fa558f4ceb6e9`  
기준 브랜치: `csharp/standalone-engine`  
장기 Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
PR #3 base: `improve/chart-engine-hardening`  
기존 VB 안정 기준: `316f0decfa5981d081f88585d4db5d4d18830c0f`

> 이 문서를 갱신한 커밋은 위 기능 코드 기준 위에 존재한다.  
> 다음 세션에서는 `git log -2 --oneline`으로 문서 커밋과 기능 기준 커밋을 함께 확인한다.

---

# 0. 다음 세션이 반드시 먼저 읽을 사항

## 0-1. 현재 실제 상태

```text
P1 모듈 플랫폼 기반             완료
P1 App/Renderer 수직 연결        완료
P2 SMA parity                    완료·병합
P2 RSI parity                    완료·병합
P2 MACD parity                   완료·병합
P2 SuperTrend parity             완료·병합
P2 JMA parity                    완료·병합
P2 OBV parity                    완료·병합
P2 Disparity parity              완료·병합
P2 VWAP parity                   시작점만 생성, 구현 전
```

현재 브랜치는 Disparity 병합 커밋에서 시작했다.

```text
branch  csharp/module-platform-p2-vwap-parity
base    22886133c1358d8c3b4c33a1f46fa558f4ceb6e9
```

**VWAP 구현 코드나 PR은 아직 없다.**  
이번 세션에서 수행한 VWAP 관련 작업은 Legacy 계약과 현재 데이터 계약의 충돌을 확인한 것까지다.

## 0-2. VWAP 구현 전에 해결해야 하는 선행 계약

Legacy `VwapIndicator`는 거래일이 바뀔 때 누적값을 초기화한다.

```csharp
if (_lastDate != DateTime.MinValue &&
    candle.TradingDate != _lastDate.Date)
{
    _priceVolume = 0;
    _volume = 0;
    _priceSquaredVolume = 0;
}
```

그러나 현재 모듈 입력 `ChartPrimaryBar`에는 다음 정보만 있다.

```text
Sequence
Open
High
Low
Close
Volume
IsFinal
```

즉, **TradingDate/OpenTime/SessionKey가 없다.**

이 상태에서 VWAP을 구현하면 다음 오류가 발생한다.

```text
단일 거래일 fixture에서는 PASS
다일 데이터에서는 전일 누적이 다음 날로 이어짐
Legacy와 session reset parity 실패
실제 intraday VWAP 의미 훼손
```

금지:

```text
Sequence 값으로 거래일 추정
봉 개수나 가격 변화로 세션 경계 추정
화면에 보이는 데이터가 하루라고 가정
VWAP 모듈 내부에서 첫 봉마다 무조건 reset
다일 parity 검증을 생략하고 병합
```

권장 해결:

```text
ChartPrimaryBar에 provider-neutral TradingDate 또는 SessionKey 추가
MainForm.CreatePrimarySeriesSnapshot에서 Candle 거래일 전달
모든 VWAP fixture에 명시적 거래일 제공
2개 이상의 거래일을 포함한 parity test 필수
```

권장 타입은 `DateOnly TradingDate`다. 시간대 추론을 피하고 VWAP이 실제로 필요한 세션 경계만 표현한다.

호환성을 위해 기존 7개 지표 fixture를 한 번에 대량 수정하지 않으려면 다음 중 하나를 선택한다.

```text
A. primary constructor에 DateOnly TradingDate 추가
   - 모든 호출부를 명시적으로 수정
   - 가장 안전하지만 변경 범위가 큼

B. DateOnly TradingDate를 포함한 새 constructor를 추가하고
   기존 constructor는 DateOnly.MinValue로 위임
   - 기존 fixture 변경 최소화
   - VWAP runtime은 MinValue 입력 정책을 명확히 해야 함
```

정확성을 우선하면 A가 권장된다. B를 선택할 경우 실제 App 경로와 VWAP 검증에서는 `DateOnly.MinValue`를 허용하지 않아야 한다.

---

# 1. 최상위 불변식 — 시장 데이터 순서

Cybos 틱 데이터는 `HHmm`까지만 제공될 수 있으므로 같은 분 안의 실제 체결 순서는 공급자 배열 위치로 보존한다.

금지:

```text
List.Sort
Array.Sort
OrderBy
ThenBy
SortedDictionary로 시간 재조립
시간/가격/수량/sequence 기반 재정렬
동일 HHmm 행 삭제
가격+수량/OHLCV 중복 제거
```

허용:

```text
Forward
ReverseWhole
페이지 append/prepend
페이지 또는 전체 배열 한 번 Reverse
```

`ChartKit.DataSources`의 정렬 API는 CI 단계 `Reject DataSources row-reordering APIs`가 차단한다.

동일 시각·동일 가격·동일 수량 체결은 신뢰 가능한 공급자 체결번호가 없으므로 중복 제거하지 않는다.

---

# 2. 제품·모듈 구조

```text
csharp_chartkit
├─ src/ChartKit.Contracts
├─ src/ChartKit.Engine
├─ src/ChartKit.Charting
├─ src/ChartKit.DataSources
├─ src/ChartKit.Modules.Abstractions
├─ src/ChartKit.Modules.Platform
├─ src/ChartKit.Modules.Indicators
├─ src/ChartKit.ModuleHost
├─ src/ChartKit.Persistence
├─ src/ChartKit.Scene
├─ src/ChartKit.Composition
├─ src/ChartKit.UiModel
├─ src/ChartKit.Rendering
├─ src/ChartKit.App
├─ tests/ChartKit.EngineVerification
├─ migration/LegacyInventory
└─ migration/LegacyParity
```

표준 연결 경로:

```text
ChartProfile
→ Module Registry / ChartModuleHost
→ 활성 Module
→ ChartContribution
→ SceneCompiler
→ immutable ChartRenderPlan
→ generic SkiaChartRenderer
```

핵심 경계:

```text
Renderer는 기능 이름을 모른다.
Module은 SkiaSharp와 WinForms를 모른다.
UI는 지표별 메뉴·설정 Form을 하드코딩하지 않는다.
Context Menu·Quick Toolbar·Property Inspector는 같은 metadata에서 생성한다.
기능 On/Off는 범용 명령으로 처리한다.
비활성 모듈에는 데이터가 전달되지 않고 Contribution도 0이어야 한다.
```

금지 참조:

```text
Engine → WinForms/SkiaSharp
Rendering → DataSources
Charting → DataSources/WinForms
Modules → WinForms/SkiaSharp
Renderer → SMA/RSI/MACD 등 기능별 형식
C# product solution → VB runtime project
```

---

# 3. Architecture Baseline 1.0

상태:

```text
Architecture Baseline 1.0
Approved
P1 수직 경로 구현 완료
```

기준 문서:

```text
docs/chart-module-platform/README.md
docs/chart-module-platform/architecture-baseline-1.0.md
docs/chart-module-platform/architecture-constitution.md
docs/chart-module-platform/feature-capability-matrix.md
docs/chart-module-platform/implementation-roadmap.md
docs/chart-module-platform/module-file-standard.md
docs/chart-module-platform/module-registry-host-standard.md
docs/chart-module-platform/module-composition-probe-standard.md
docs/chart-module-platform/profile-persistence-standard.md
docs/chart-module-platform/ui-metadata-property-standard.md
docs/chart-module-platform/app-integration-standard.md
docs/chart-module-platform/templates/ChartModule.template.cs
scripts/verify_chart_module_headers.ps1
```

모든 플랫폼 진입 `*Module.cs`는 상단에 `<chart-module>` 계약을 가진다.

필수 키 13개:

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

검사:

```powershell
.\scripts\verify_chart_module_headers.ps1
```

Disparity 병합 시점 기준 결과:

```text
chart_module_header_contract=PASS module_files=8
```

VWAP 추가 후 예상:

```text
chart_module_header_contract=PASS module_files=9
```

---

# 4. 병합 완료 이력

## Baseline / P1

```text
PR #4  Architecture Baseline 1.0
merge 65000f0cd3ec8c346c8007f28e712ea3423844e3

PR #5  Realtime diagnostics nullability
merge 68bfbf73603bf5e729d8f5388b20313194463bff

PR #6  P1-A contracts and scene foundations
merge 27319e4ae895b13cde209544f08b85658c3738fe

PR #7  P1-B module registry and host
merge 8512d3e1e432a433051af10dacea85ec32bec610

PR #8  P1-C composition and platform probe
merge 11cad5a2eec92346c380f8f3040976466d926c2a

PR #9  P1-D ChartProfile JSON persistence
merge 54ac480188da9f7efa6fd753196705b75de2a731

PR #10 P1-E generic UI metadata and property mutation
merge 43752f75f3b9ce589d17bd46626f06a555fa361e

PR #11 P1-F WinForms app shell integration
merge 3e28c1924b3cc54aa0ca832adbfee6f4610c798a

PR #12 P1-G ChartRenderPlan to Skia renderer
merge 31a6b9f56b98add2786182458ea6379780ba8ada
```

## P2 Legacy 지표 모듈 이전

```text
PR #13 SMA
merge 17f5dfe885009f5f74fa2d21d65c183ed7f32c5d

PR #14 RSI
merge 98e5a10da4b8313e31c73c451f01d6076a7697c0

PR #15 MACD
merge 43a40a9e05819579a2e5c1f9afce987a6318ba02

PR #16 SuperTrend
merge e81a0fb97aaee8ae004d83c6fc64cc8b27e5d67b

PR #17 JMA
merge eb449c092c75065756af80ffcd780670b7ba98ae

PR #18 OBV
merge d49170c9fb3df4c1de2a2c9a3623bbf165ee5010

PR #19 Disparity
merge 22886133c1358d8c3b4c33a1f46fa558f4ceb6e9
```

현재 `csharp/standalone-engine` HEAD와 PR #3 head는 `2288613...`이다.

PR #3은 여전히 다음 상태다.

```text
Open
Draft
Unmerged
Base improve/chart-engine-hardening
```

**실제 장중 WebSocket·물리 재연결·soak 완료 전 PR #3을 병합하지 않는다.**

---

# 5. 복원 체크포인트

시장 데이터·실시간:

```text
checkpoint/csharp-120tick-source-order-pass
checkpoint/csharp-rest-realtime-boundary-pass
checkpoint/csharp-representative-trading-day-pass
checkpoint/csharp-realtime-reconnect-pass
```

모듈 플랫폼:

```text
checkpoint/csharp-module-file-standard-pass
checkpoint/csharp-module-baseline-nullability-pass
checkpoint/csharp-module-platform-p1a-pass
checkpoint/csharp-module-platform-p1b-pass
checkpoint/csharp-module-platform-p1c-pass
checkpoint/csharp-module-platform-p1d-pass
checkpoint/csharp-module-platform-p1e-pass
checkpoint/csharp-module-platform-p1f-pass
checkpoint/csharp-module-platform-p1g-pass
checkpoint/csharp-module-platform-p2-sma-pass
checkpoint/csharp-module-platform-p2-rsi-pass
checkpoint/csharp-module-platform-p2-macd-pass
checkpoint/csharp-module-platform-p2-supertrend-pass
checkpoint/csharp-module-platform-p2-jma-pass
checkpoint/csharp-module-platform-p2-obv-pass
checkpoint/csharp-module-platform-p2-disparity-pass
```

---

# 6. P2 이전 완료 지표의 공통 계약

이전 완료 지표:

```text
SMA
RSI
MACD
SuperTrend
JMA
OBV
Disparity
```

모든 지표는 다음 경로를 통과했다.

```text
Legacy Indicator와 full parity
동일 봉 Update parity
새 봉 Append parity
rolling snapshot Rebuild parity
비활성 계산 0
Contribution 수·패널 계약
ObjectId별 Style
Property 변경 impact
Profile 저장·복원
App self-test
실제 화면 smoke test
기존 시장 데이터·실시간 회귀검증
```

중요 사례:

```text
MACD는 화면상 네 번째 패널이었지만 내부 PanelIndex는 7이었다.
처음 indicator.4로 구현해 plan은 생성되나 화면에 안 나오는 결함이 발생했다.
수정 후 indicator.7을 사용하고 잘못 저장된 indicator.4 Profile도 호환 변환한다.
```

따라서 새 지표는 화면 순서로 패널 번호를 추정하지 말고 Legacy Descriptor의 실제 `PanelIndex`를 확인한다.

---

# 7. 최근 검증 기준 — Disparity 병합

정확한 검증 HEAD:

```text
f836939375ae220733ee5deb01f5aac1b0aa301b
```

병합 커밋:

```text
22886133c1358d8c3b4c33a1f46fa558f4ceb6e9
```

검증 결과:

```text
chart_module_header_contract=PASS module_files=8
Release build PASS
errors 0
OpenTK/OpenTK.GLControl NU1701 반복 경고 4개만 잔존
Disparity full/update/append/rebuild parity PASS
indicator.6 panel contract PASS
Profile round-trip PASS
EngineVerification PASS
App self-test PASS
processed_events=220
max_queue_depth=23
multi_symbol_fifo=PASS
```

`max_queue_depth`에는 아직 공식 threshold가 없으므로 단일 값으로 성능 합격을 선언하지 않는다.

---

# 8. P2-8 VWAP Legacy 계약

파일:

```text
csharp_chartkit/src/ChartKit.Engine/VwapIndicator.cs
```

Legacy 기본값:

```text
StdDev1  1.0
StdDev2  2.0
Panel    0 / price.main
```

출력 5개:

```text
Value
Upper1
Lower1
Upper2
Lower2
```

계산:

```text
typicalPrice = (High + Low + Close) / 3
priceVolume += typicalPrice × Volume
volume += Volume
priceSquaredVolume += typicalPrice² × Volume

VWAP = priceVolume / volume
variance = max(0, priceSquaredVolume / volume - VWAP²)
deviation = sqrt(variance)

Upper1 = VWAP + StdDev1 × deviation
Lower1 = VWAP - StdDev1 × deviation
Upper2 = VWAP + StdDev2 × deviation
Lower2 = VWAP - StdDev2 × deviation
```

거래일 변경 시 다음 누적 상태를 0으로 초기화한다.

```text
priceVolume
volume
priceSquaredVolume
```

누적 거래량이 0 이하이면 5개 출력 모두 `NaN`이다.

UpdateLast를 위해 Legacy는 다음 상태를 저장·복원한다.

```text
priceVolume
volume
priceSquaredVolume
lastDate
```

---

# 9. VWAP 권장 구현 구조

## 9-1. 데이터 계약 선행 수정

대상:

```text
csharp_chartkit/src/ChartKit.Modules.Abstractions/ChartModuleDataContracts.cs
csharp_chartkit/src/ChartKit.App/MainForm.ModuleVisualContext.cs
각 모듈 App/Engine fixture의 ChartPrimaryBar 생성부
```

권장:

```csharp
DateOnly TradingDate
```

`MainForm.CreatePrimarySeriesSnapshot`에서 각 `Candle`의 거래일을 반드시 전달한다.

## 9-2. 신규 파일

```text
csharp_chartkit/src/ChartKit.Modules.Indicators/VwapSeriesRuntime.cs
csharp_chartkit/src/ChartKit.Modules.Indicators/VwapModule.cs
csharp_chartkit/tests/ChartKit.EngineVerification/VwapModuleParityVerification.cs
csharp_chartkit/src/ChartKit.App/VwapModuleAppVerification.cs
docs/chart-module-platform/vwap-module-parity-standard.md
```

필요 수정:

```text
ChartModulePlatformController.cs      registry.Register<VwapModule>()
AppSelfTestRunner.cs                  VWAP App verification 호출
Program.cs                            VWAP Engine verification 호출
```

## 9-3. 모듈 계약

```text
Module-Id       indicator.vwap
Default Panel   price.main
Contributions   5 × Polyline
Data            PrimarySymbol.OHLCV + TradingDate
```

권장 ObjectId:

```text
vwap.value
vwap.upper1
vwap.lower1
vwap.upper2
vwap.lower2
```

권장 Property:

```text
stdDev1                    RecalculateModule
stdDev2                    RecalculateModule
vwap.value.stroke          RedrawOnly
vwap.upper1.stroke         RedrawOnly
vwap.lower1.stroke         RedrawOnly
vwap.upper2.stroke         RedrawOnly
vwap.lower2.stroke         RedrawOnly
```

기본 색상은 현재 Legacy 화면 색상과 동일하거나 명확히 구별되는 5개 색으로 지정한다. Renderer에는 VWAP 전용 분기를 추가하지 않는다.

## 9-4. Runtime 상태

```text
priceVolume
volume
priceSquaredVolume
lastTradingDate
savedPriceVolume
savedVolume
savedPriceSquaredVolume
savedLastTradingDate
sourceSequences
sourceHighs
sourceLows
sourceCloses
sourceVolumes
sourceTradingDates
values
```

Update/Append 판정에 거래일도 포함한다.

```text
UpdateLast:
  이전 모든 봉 동일
  마지막 sequence 동일
  마지막 OHLCV/TradingDate 변경 허용

Append:
  기존 모든 봉 동일
  마지막 sequence = committed + 1
  새 거래일이면 Step에서 reset

Rebuild:
  rolling snapshot 또는 중간 봉 변경
```

## 9-5. 필수 자동검증

단일 거래일만 검사해서는 안 된다.

필수 fixture:

```text
거래일 1: 최소 40봉
거래일 2: 최소 40봉
거래일 3: zero-volume 또는 volume 변형 봉 포함
```

검증:

```text
Legacy full parity
마지막 봉 OHLCV Update parity
같은 거래일 Append parity
다음 거래일 첫 봉 Append reset parity
rolling snapshot Rebuild parity
volume 0 NaN parity
비활성 계산 0
5개 Contribution
price.main 패널
Style override
StdDev 변경
Profile round-trip
reference boundary
Release configuration
```

예상 Engine marker:

```text
csharp_vwap_module_release_configuration=PASS
csharp_vwap_module_definition=PASS
csharp_vwap_module_metadata=PASS
csharp_vwap_full_parity=PASS
csharp_vwap_update_parity=PASS
csharp_vwap_append_parity=PASS
csharp_vwap_session_reset_parity=PASS
csharp_vwap_rebuild_parity=PASS
csharp_vwap_zero_volume_parity=PASS
csharp_vwap_disabled_zero=PASS
csharp_vwap_contributions=PASS
csharp_vwap_panel_contract=PASS
csharp_vwap_style_override=PASS
csharp_vwap_parameter_change=PASS
csharp_vwap_reference_boundary=PASS
csharp_vwap_module_contracts=PASS
csharp_engine_verification=PASS
```

예상 App marker:

```text
csharp_app_vwap_module_data=PASS
csharp_app_vwap_module_parameters=PASS
csharp_app_vwap_module_style=PASS
csharp_app_vwap_panel_contract=PASS
csharp_app_vwap_session_reset=PASS
csharp_app_vwap_module_roundtrip=PASS
csharp_app_self_test=PASS
```

---

# 10. 다음 세션 작업 순서

## 단계 1. 로컬 동기화

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

git status
git pull --ff-only origin csharp/module-platform-p2-vwap-parity

git log -2 --oneline
git rev-parse HEAD
```

예상 구조:

```text
최상단: session_handoff.md 갱신 커밋
그 아래: 2288613 Merge PR #19
```

## 단계 2. 선행 파일 직접 확인

```text
session_handoff.md
csharp_chartkit/src/ChartKit.Engine/VwapIndicator.cs
csharp_chartkit/src/ChartKit.Modules.Abstractions/ChartModuleDataContracts.cs
csharp_chartkit/src/ChartKit.App/MainForm.ModuleVisualContext.cs
csharp_chartkit/src/ChartKit.Modules.Indicators/DisparityModule.cs
csharp_chartkit/src/ChartKit.Modules.Indicators/DisparitySeriesRuntime.cs
```

## 단계 3. TradingDate 데이터 계약 구현

```text
ChartPrimaryBar에 TradingDate 추가
실제 Candle → module snapshot 경로 연결
모든 컴파일 오류 수정
기존 7개 지표 회귀검증 유지
다일 fixture 추가
```

이 단계만 별도 커밋하는 것이 좋다.

권장 커밋:

```text
Add trading-date contract to module primary bars
```

## 단계 4. VwapSeriesRuntime 구현

```text
Legacy 누적식 그대로 복제
거래일 reset
Save/Restore
UpdateLast
Append
Rebuild
diagnostics
```

## 단계 5. VwapModule 구현

```text
5개 Polyline
price.main
metadata/property/command
active-only calculation
Profile style
```

## 단계 6. EngineVerification·App self-test

다일 session reset을 반드시 포함한다.

## 단계 7. 문서·Draft PR

예상 PR:

```text
#20 Migrate VWAP to the module platform with legacy parity
```

검증 전:

```text
Open
Draft
Unmerged
```

## 단계 8. 로컬 검증

```powershell
.\scripts\verify_chart_module_headers.ps1

dotnet build .\csharp_chartkit\ChartKit.CSharp.sln `
  -c Release `
  --no-incremental

dotnet run `
  --project .\csharp_chartkit\tests\ChartKit.EngineVerification\ChartKit.EngineVerification.csproj `
  -c Release `
  --no-build

dotnet run `
  --project .\csharp_chartkit\src\ChartKit.App\ChartKit.App.csproj `
  -c Release `
  --no-build `
  -- `
  --self-test `
  --count 120
```

## 단계 9. 화면 smoke test

VWAP만 활성화한 새 Profile의 예상 상태:

```text
modules 1/9
plan 5
faults 0
```

검증:

```text
StdDev 1/2에서 Legacy 5개 선과 겹침
StdDev 변경 시 band만 즉시 재계산
색상 변경 즉시 반영
종료·재실행 후 On/설정/색상 복원
다일 replay에서 날짜 경계 첫 봉이 새 VWAP으로 시작
```

## 단계 10. 병합 후 P2 checkpoint

모든 게이트 통과 후:

```text
PR #20 Ready
exact-head merge
checkpoint/csharp-module-platform-p2-vwap-pass
```

VWAP까지 완료되면 Legacy 8개 지표의 모듈 이전이 끝난다.

---

# 11. 실시간·거래일 관련 미완료 P0

자동 scripted 검증은 통과했지만 실제 장중 검증은 별도다.

대표종목:

```text
005930 삼성전자
000660 SK하이닉스
```

다음 거래일 확인:

```text
day trading
ws receiving
첫 이벤트 seed-update 또는 seed-append
events 증가
stale 0
errors 0
중복 봉 0
sequence 역전 0
```

물리 재연결:

```text
connection attempts +1
registration count +1
reconnecting → registered → receiving
현재 봉 상태 유지
다음 봉 정상 append
```

soak:

```text
1종목 6시간
20종목 6시간
100종목 replay 6시간
종목·주기 100회 변경
working/private/managed memory
threads
handles/GDI
queue depth/max
accepted/processed/published/errors
latency
CSV/JSONL
```

**scripted reconnect PASS를 실제 물리 reconnect PASS라고 표현하지 않는다.**

---

# 12. 거래일 판정 계약

대표종목 기반 판정:

```text
오늘:
  최신 1분봉 1건
  하나라도 오늘 날짜 → TradingDay
  둘 다 정상이나 오늘 날짜 없음 → NoTradingDay

과거:
  일봉 1건
  base_dt 정확히 일치 → TradingDay
  둘 다 정상이나 정확한 날짜 없음 → NoTradingDay

실패·일부 실패·미래 날짜:
  Unknown
```

중요:

```text
오늘은 분봉, 과거는 일봉
과거 휴장 판정에 분봉 연속조회 금지
대표종목 probe는 realtime seed를 저장·덮어쓰기 금지
실제 realtime event가 오면 no-data 판정보다 TradingDay 우선
```

---

# 13. 잔존 경고와 성능 판정

잔존 빌드 경고:

```text
OpenTK 3.1.0 NU1701
OpenTK.GLControl 3.1.0 NU1701
```

현재 허용한다. 신규 C# nullable/컴파일 경고는 허용하지 않는다.

`max_queue_depth`는 실행마다 변동했다.

```text
21
141
162
20
25
22
32
37
53
23
```

아직 공식 threshold가 없다.

금지:

```text
단일 실행값만으로 성능 개선/악화 원인 단정
queue depth가 낮다고 soak PASS 선언
queue depth가 높다고 기능 PR 자동 실패 처리
```

향후 soak recorder에서 workload와 함께 threshold를 정의한다.

---

# 14. 하지 말아야 할 실수

```text
틱 데이터를 시간으로 정렬
동일 HHmm 행 삭제
오류를 감추려고 틱 행 제거
조회 실패를 휴장으로 판정
과거 휴장 판정에 분봉 사용
대표종목 probe로 realtime seed 덮어쓰기
동일 시각/가격/수량만으로 실시간 체결 중복 제거
재연결마다 RealtimeCandleBuilder 초기화
Renderer에 VWAP/RSI/전략 등 기능별 분기 추가
MainForm에 지표별 Toggle 메서드 추가
모듈에서 SKCanvas 직접 사용
기능 파일 상단 <chart-module> 계약 생략
기능마다 별도 csproj 생성
화면 순서로 내부 PanelIndex 추정
VWAP 거래일 reset을 생략
Sequence로 거래일 추정
단일 거래일 fixture만으로 VWAP parity 선언
검증하지 않은 HEAD를 Ready 또는 병합
Draft PR #3 병합
실데이터 검증 전에 PR #3 병합
```

---

# 15. 다음 세션 완료 조건

최소 완료 목표:

```text
TradingDate module data contract 확정
다일 VWAP full/update/append/session-reset/rebuild parity
VwapModule 5개 가격 패널 Contribution
App 등록·Property·Profile round-trip
Release build 오류 0
기존 지표·시장 데이터·실시간 회귀 PASS
화면 smoke test PASS
Draft PR #20 생성
```

병합까지 진행할 경우 추가 조건:

```text
PR #20 head가 로컬 검증 SHA와 정확히 일치
검증 결과 PR comment 기록
Ready 전환
expected-head merge
checkpoint/csharp-module-platform-p2-vwap-pass 생성
```
