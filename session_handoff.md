# ChartKit C# 세션 인수인계

작성 시각: 2026-08-02 08:52 KST  
저장소: `hoonoh57/ChartKit`  
로컬 경로: `E:\2026\gpt\vb\sciaChart\ChartKit`  
현재 작업 브랜치: `csharp/module-platform-p2-vwap-parity`  
VWAP 자동검증 기능 HEAD: `842fccfc8b8a8bc34a4c42294b8dfb4336cc3a7d`  
기준 브랜치: `csharp/standalone-engine`  
기준 브랜치 HEAD: `22886133c1358d8c3b4c33a1f46fa558f4ceb6e9`  
VWAP Draft PR: `#20 Migrate VWAP to the module platform with legacy parity`  
장기 Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
기존 VB 안정 기준: `316f0decfa5981d081f88585d4db5d4d18830c0f`

> 이 문서 커밋은 자동검증 기능 HEAD 위에 존재한다.  
> 다음 세션에서는 `git log -3 --oneline`으로 문서 커밋과 `842fccf...` 기능 HEAD를 함께 확인한다.

---

# 0. 다음 세션이 반드시 먼저 읽을 사항

## 0-1. 현재 실제 상태

```text
P1 모듈 플랫폼 기반             완료·병합
P1 App/Renderer 수직 연결        완료·병합
P2 SMA parity                    완료·병합
P2 RSI parity                    완료·병합
P2 MACD parity                   완료·병합
P2 SuperTrend parity             완료·병합
P2 JMA parity                    완료·병합
P2 OBV parity                    완료·병합
P2 Disparity parity              완료·병합
P2 VWAP parity                   구현·자동검증 완료, Draft PR #20
VWAP 실제 화면 중첩 smoke        미완료
PR #20 Ready/merge/checkpoint     미완료
실제 장중 WebSocket              미완료
물리 네트워크 재연결             미완료
장중 soak                        미완료
```

VWAP 구현 전 데이터 계약 충돌은 해결됐다.

```text
ChartPrimaryBar
  + DateOnly TradingDate

Candle.TradingDate
  → MainForm.CreatePrimarySeriesSnapshot
  → ChartPrimaryBar.TradingDate
  → VwapSeriesRuntime
```

`VwapSeriesRuntime`은 `DateOnly.MinValue`를 허용하지 않는다.

## 0-2. 지금 절대 하지 말아야 할 일

```text
PR #20을 자동으로 Ready 전환하거나 병합
화면 중첩 검증 없이 VWAP 완료 선언
PR #3 병합
PR #3을 실제 장중 검증 완료로 표현
scripted reconnect를 물리 reconnect로 표현
틱 데이터를 시간·가격·수량·sequence로 재정렬
동일 HHmm 체결 또는 동일 가격·수량 체결 삭제
VWAP 세션 경계를 Sequence나 봉 개수로 추정
Renderer에 VWAP 전용 분기 추가
MainForm에 VWAP 전용 UI 처리 추가
```

## 0-3. 다음 첫 작업

다음 세션의 첫 작업은 코드 구현이 아니다.

```text
1. 원격 브랜치 pull
2. 기능 HEAD와 문서 HEAD 확인
3. 로컬 동일 검증 재실행
4. 실제 데스크톱에서 Legacy VWAP 5선과 Module VWAP 5선 중첩 확인
5. 다일 replay에서 거래일 첫 봉 reset 시각 확인
6. 설정·색상 변경 및 재시작 복원 확인
```

수동 검증이 성공한 뒤에만 PR #20을 Ready → exact-head merge한다.

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

VWAP 변경은 DataSources 순서 처리에 손대지 않았다.

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
Renderer → SMA/RSI/MACD/VWAP 등 기능별 형식
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

VWAP 자동검증 HEAD 결과:

```text
chart_module_header_contract=PASS module_files=9
```

VWAP 기준 문서:

```text
docs/chart-module-platform/vwap-module-parity-standard.md
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

VWAP:

```text
PR #20
Open
Draft
Unmerged
Base csharp/standalone-engine
Verified feature head 842fccfc8b8a8bc34a4c42294b8dfb4336cc3a7d
```

PR #3:

```text
Open
Draft
Unmerged
Base improve/chart-engine-hardening
Head csharp/standalone-engine
Head SHA 22886133c1358d8c3b4c33a1f46fa558f4ceb6e9
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

아직 생성하지 않음:

```text
checkpoint/csharp-module-platform-p2-vwap-pass
```

PR #20 병합 후에만 생성한다.

---

# 6. TradingDate 데이터 계약

대상 파일:

```text
csharp_chartkit/src/ChartKit.Modules.Abstractions/ChartModuleDataContracts.cs
csharp_chartkit/src/ChartKit.App/MainForm.ModuleVisualContext.cs
```

현재 계약:

```csharp
public readonly record struct ChartPrimaryBar(
    long Sequence,
    DateOnly TradingDate,
    double Open,
    double High,
    double Low,
    double Close,
    long Volume,
    bool IsFinal)
```

기존 7개 비세션 지표 fixture의 변경 범위를 제한하기 위해 이전 시그니처 호환 생성자를 유지한다.

```text
기존 생성자 → DateOnly.MinValue
실제 App 경로 → 명시적 TradingDate
VWAP fixture → 명시적 TradingDate
VWAP runtime → MinValue 거부
```

이 호환 생성자는 VWAP의 세션 경계 추론에 사용하지 않는다.

App 전달:

```csharp
DateOnly.FromDateTime(candle.TradingDate)
```

`Candle.TradingDate`는 `OpenTime.Date`다.

---

# 7. VWAP Legacy 계약

Legacy 파일:

```text
csharp_chartkit/src/ChartKit.Engine/VwapIndicator.cs
```

기본값:

```text
StdDev1 1.0
StdDev2 2.0
Panel   0 / price.main
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

UpdateLast 저장·복원 상태:

```text
priceVolume
volume
priceSquaredVolume
lastTradingDate
```

---

# 8. VWAP 구현 파일

신규:

```text
csharp_chartkit/src/ChartKit.Modules.Indicators/VwapSeriesRuntime.cs
csharp_chartkit/src/ChartKit.Modules.Indicators/VwapModule.cs
csharp_chartkit/tests/ChartKit.EngineVerification/VwapModuleParityVerification.cs
csharp_chartkit/src/ChartKit.App/VwapModuleAppVerification.cs
docs/chart-module-platform/vwap-module-parity-standard.md
```

수정:

```text
csharp_chartkit/src/ChartKit.Modules.Abstractions/ChartModuleDataContracts.cs
csharp_chartkit/src/ChartKit.App/MainForm.ModuleVisualContext.cs
csharp_chartkit/src/ChartKit.App/ChartModulePlatformController.cs
csharp_chartkit/src/ChartKit.App/AppSelfTestRunner.cs
csharp_chartkit/tests/ChartKit.EngineVerification/Program.cs
```

모듈 계약:

```text
Module-Id       indicator.vwap
Default Panel   price.main
Data            OHLCV + TradingDate
Contributions   5 × Polyline
```

ObjectId:

```text
vwap.value
vwap.upper1
vwap.lower1
vwap.upper2
vwap.lower2
```

Property:

```text
stdDev1                    RecalculateModule
stdDev2                    RecalculateModule
vwap.value.stroke          RedrawOnly
vwap.upper1.stroke         RedrawOnly
vwap.lower1.stroke         RedrawOnly
vwap.upper2.stroke         RedrawOnly
vwap.lower2.stroke         RedrawOnly
```

Renderer에는 VWAP 전용 분기를 추가하지 않았다.

---

# 9. VwapSeriesRuntime 판정 계약

상태:

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

판정:

```text
Unchanged:
  전체 Sequence/High/Low/Close/Volume/TradingDate 동일

UpdateLast:
  이전 모든 봉 동일
  마지막 Sequence 동일
  마지막 High/Low/Close/Volume/TradingDate 변경 허용

Append:
  기존 모든 봉 동일
  마지막 Sequence = committed + 1
  새 TradingDate이면 Step에서 누적값 reset

Rebuild:
  rolling snapshot
  중간 봉 변경
  기존 배열과 구조 불일치
```

Open 값은 Legacy VWAP 계산에 사용되지 않으므로 runtime identity 비교 대상에 포함하지 않았다.

---

# 10. 자동검증 결과

검증 기능 HEAD:

```text
842fccfc8b8a8bc34a4c42294b8dfb4336cc3a7d
```

Windows Actions:

```text
ChartKit CSharp Engine
run 30723966119
result success

ChartKit CSharp Legacy Inventory
run 30723966064
result success
```

빌드:

```text
C# product Release build PASS
errors 0
OpenTK/OpenTK.GLControl NU1701 경고 2개
신규 C# 컴파일 경고 0
```

모듈:

```text
chart_module_header_contract=PASS module_files=9
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
csharp_vwap_trading_date_contract=PASS
csharp_vwap_reference_boundary=PASS
csharp_vwap_module_contracts=PASS
csharp_engine_verification=PASS
```

App:

```text
csharp_app_vwap_module_data=PASS
csharp_app_vwap_module_parameters=PASS
csharp_app_vwap_module_style=PASS
csharp_app_vwap_panel_contract=PASS
csharp_app_vwap_session_reset=PASS
csharp_app_vwap_module_roundtrip=PASS
csharp_app_self_test=PASS
csharp_desktop_shell_smoke=PASS
```

Legacy:

```text
legacy_parity_VWAP=PASS
legacy_parity_indicator_count=8
legacy_csharp_indicator_parity=PASS
```

회귀:

```text
processed_events=220
max_queue_depth=26
multi_symbol_fifo=PASS
csharp_datasources_no_row_sort=PASS
csharp_kiwoom_120tick_4000_source_order=PASS
csharp_kiwoom_equal_hhmm_tick_order=PASS
csharp_realtime_reconnect_continuity=PASS
csharp_realtime_builder_survives_reconnect=PASS
```

Publish:

```text
PASS
Artifact ID 8825733497
SHA256 9ad0b21544dbdf41f4bb445725bfb9eae27c27f83af59584e2ec83c4d2c24a14
```

`max_queue_depth=26`은 단일 실행 관측값이며 공식 성능 threshold가 아니다.

---

# 11. 수동 검증 절차

## 11-1. 로컬 동기화

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

git status
git fetch origin
git switch csharp/module-platform-p2-vwap-parity
git pull --ff-only origin csharp/module-platform-p2-vwap-parity

git log -3 --oneline
git rev-parse HEAD
```

예상:

```text
최상단: 이 인수인계 갱신 커밋
그 아래: 842fccf Document VWAP module parity standard
```

## 11-2. 로컬 자동검증 재실행

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

## 11-3. 화면 smoke

새 임시 Profile로 실행한다.

검증:

```text
VWAP만 활성화
modules 1/9
plan 5
faults 0

Legacy VWAP Value와 Module vwap.value 중첩
Legacy Upper1/Lower1과 Module upper1/lower1 중첩
Legacy Upper2/Lower2와 Module upper2/lower2 중첩
StdDev 1.0 / 2.0 기본값
StdDev 변경 시 5선 재계산
색상 변경 즉시 반영
종료·재실행 후 On/StdDev/색상 복원
```

## 11-4. 다일 replay

최소 3거래일 데이터 사용:

```text
거래일 1: 40봉 이상
거래일 2: 40봉 이상
거래일 3: 첫 봉 volume 0 또는 거래량 변형
```

필수 확인:

```text
거래일 2 첫 봉의 VWAP = 그 봉 typical price
전일 누적이 거래일 2로 넘어오지 않음
거래일 3 volume 0 첫 봉은 5개 값 NaN
거래일 3 다음 양수 volume 봉은 새 세션 기준 계산
화면 이동·축소·확대 후 세션 경계 유지
rolling snapshot에서도 Legacy와 일치
```

스크린샷 또는 화면 관측 결과를 PR #20 comment에 기록한다.

---

# 12. PR #20 완료 순서

수동 smoke 성공 후:

```text
1. git rev-parse HEAD
2. PR #20 head SHA 일치 확인
3. 수동 검증 결과 PR comment
4. Draft → Ready
5. expected-head merge
6. csharp/standalone-engine pull
7. 전체 검증 재실행
8. checkpoint/csharp-module-platform-p2-vwap-pass 생성
9. session_handoff.md 병합 상태 갱신
```

병합 명령은 검증 HEAD가 정확히 일치할 때만 실행한다.

예상 병합 후 상태:

```text
P2 Legacy 8개 지표 모듈 이전 완료
SMA
RSI
MACD
SuperTrend
JMA
OBV
Disparity
VWAP
```

---

# 13. 실제 장중 검증 미완료 P0

대표종목:

```text
005930 삼성전자
000660 SK하이닉스
```

장중 WebSocket:

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

# 14. 거래일 판정 계약

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

이 거래일 판정 서비스와 `ChartPrimaryBar.TradingDate`는 역할이 다르다.

```text
TradingDayProbe: 시장 거래일 여부 판정
ChartPrimaryBar.TradingDate: 각 봉이 속한 세션 경계 전달
```

---

# 15. 잔존 경고와 성능 판정

잔존 빌드 경고:

```text
OpenTK 3.1.0 NU1701
OpenTK.GLControl 3.1.0 NU1701
```

현재 허용한다. 신규 C# nullable/컴파일 경고는 허용하지 않는다.

금지:

```text
단일 queue depth 값으로 성능 개선/악화 단정
queue depth가 낮다고 soak PASS 선언
queue depth가 높다고 기능 PR 자동 실패 처리
```

향후 soak recorder에서 workload와 함께 threshold를 정의한다.

---

# 16. 다음 세션 완료 조건

최소 완료 목표:

```text
로컬 자동검증 재확인
Legacy-vs-module 5선 중첩 smoke PASS
StdDev 변경 PASS
색상 변경 PASS
Profile 종료·재실행 복원 PASS
다일 replay session reset 화면 검증 PASS
PR #20 comment 기록
```

병합까지 진행할 경우:

```text
PR #20 head와 검증 SHA 정확히 일치
Ready 전환
expected-head merge
csharp/standalone-engine 전체 회귀 PASS
checkpoint/csharp-module-platform-p2-vwap-pass 생성
인수인계 문서 병합 상태 갱신
```

PR #3 관련 실제 장중 WebSocket·물리 재연결·soak는 별도 P0이며 계속 미완료다.
