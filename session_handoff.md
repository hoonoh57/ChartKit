# ChartKit C# 세션 인수인계

작성 시각: 2026-08-01 14:58 KST  
저장소: `hoonoh57/ChartKit`  
작업 브랜치: `csharp/standalone-engine`  
Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
PR base: `improve/chart-engine-hardening`  
기존 VB 안정 기준: `316f0decfa5981d081f88585d4db5d4d18830c0f`

검증된 기능 코드 체크포인트:

```text
bd21cd849d44c5e4d892701326377b825f3af67d
```

모듈 플랫폼 Baseline 및 nullable 정리 검증 체크포인트:

```text
68bfbf73603bf5e729d8f5388b20313194463bff
checkpoint/csharp-module-baseline-nullability-pass
```

병합 완료:

```text
PR #4 Architecture Baseline 1.0
merge commit 65000f0cd3ec8c346c8007f28e712ea3423844e3

PR #5 Realtime diagnostics nullability
merge commit 68bfbf73603bf5e729d8f5388b20313194463bff
```

Windows CI — 체크포인트 `68bfbf7...`:

```text
ChartKit CSharp Engine
run 30686893110
result success

ChartKit CSharp Legacy Inventory
run 30686893109
result success
```

로컬 검증:

```text
Release build PASS
DataSources CS8602 0
EngineVerification PASS
ChartKit.App self-test PASS
OpenTK NU1701만 잔존
```

기존 복원 체크포인트:

```text
checkpoint/csharp-120tick-source-order-pass
checkpoint/csharp-rest-realtime-boundary-pass
checkpoint/csharp-representative-trading-day-pass
checkpoint/csharp-realtime-reconnect-pass
checkpoint/csharp-module-file-standard-pass
checkpoint/csharp-module-baseline-nullability-pass
```

현재 브랜치가 검증 기준을 포함하는지 확인:

```powershell
git merge-base --is-ancestor `
  68bfbf73603bf5e729d8f5388b20313194463bff `
  HEAD

$LASTEXITCODE   # 0이어야 함
```

---

# 0. 최상위 금지사항 — 틱 데이터 순서

Cybos 틱 데이터는 `HHmm`까지만 제공될 수 있으므로 같은 분 안의 실제 체결 순서는 배열 위치로 보존한다.

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

---

# 1. 제품 구조

```text
csharp_chartkit
├─ src/ChartKit.Contracts
├─ src/ChartKit.Engine
├─ src/ChartKit.Charting
├─ src/ChartKit.Rendering
├─ src/ChartKit.DataSources
├─ src/ChartKit.App
├─ tests/ChartKit.EngineVerification
├─ migration/LegacyInventory
└─ migration/LegacyParity
```

경계:

```text
Contracts: Candle/Timeframe/Event/Snapshot 계약
Engine: 다종목 FIFO, bounded channel, ring buffer, 지표
Charting: viewport, 축, 패널, 호가단위, 십자선
Rendering: 계산된 ChartFrame/Snapshot만 렌더링
DataSources: Kiwoom REST/WebSocket, replay, 정상화, 집계
App: WinForms shell과 사용자 명령
```

금지 참조:

```text
Engine → WinForms/SkiaSharp
Rendering → DataSources
Charting → DataSources/WinForms
C# product solution → VB project
```

---

# 2. 완료 상태

```text
독립 C# WinForms 앱
Windows publish
종목별 FIFO와 종목 간 병렬 처리
bounded channel/backpressure
고정 용량 ring buffer
20종목 self-test
MA/JMA/RSI/OBV/Disparity/MACD/SuperTrend/VWAP
VB↔C# 지표 parity
Skia 차트·축·패널·호가단위·십자선·레전드
Kiwoom REST 과거봉
Kiwoom WebSocket 계약
replay
```

120틱 검증:

```text
034020_AL
120T
총 4,000봉
gap 0
errors 0
30틱 원본 16,004행
17개 연속조회 페이지
동일 HHmm 원본 행 전부 보존
정렬·중복 제거 없음
ReverseWhole만 사용
최종 120틱 4,000봉 OHLCV 검증
```

---

# 3. REST → WebSocket 경계 진단

화면 표시:

```text
연결: connecting/login/registered/receiving/reconnecting/faulted
경계: waiting/seed-update/seed-append/no-seed-append
Update/Append 수
연결 시도/등록 수
stale 거부 수
REST seed 시각
첫 realtime 시각
```

틱 실시간 정책:

```text
동일 시각 체결 허용
현재 틱봉 마지막 체결시각보다 엄격히 과거인 이벤트만 stale 거부
정렬 없음
동일 시각 중복 제거 없음
```

휴장일 로컬 확인:

```text
day closed
ws registered
boundary waiting
events 0
stale 0
errors 0
```

---

# 4. 거래일·휴장 판정

대표종목:

```text
005930 삼성전자
000660 SK하이닉스
```

계약:

```text
오늘:
  각 대표종목 최신 1분봉 1건
  하나라도 오늘 날짜 → TradingDay
  둘 다 정상이나 오늘 날짜 없음 → NoTradingDay

과거:
  각 대표종목 일봉 1건
  base_dt가 대상 날짜와 일치 → TradingDay
  둘 다 정상이나 정확한 날짜 없음 → NoTradingDay

조회 실패·일부 실패·미래 날짜:
  Unknown
```

중요:

```text
오늘은 분봉, 과거는 일봉 사용
과거 휴장일 판정에 분봉 연속조회 금지
대표종목 probe는 realtime seed 저장·덮어쓰기 금지
실제 realtime 이벤트가 오면 no-data 판정보다 TradingDay 우선
```

자동검증:

```text
csharp_trading_day_today_minute=PASS
csharp_trading_day_history_daily=PASS
csharp_trading_day_failure_unknown=PASS
```

---

# 5. WebSocket 재연결 자동검증

구현:

```text
csharp_chartkit/src/ChartKit.DataSources/IKiwoomWebSocket.cs
csharp_chartkit/src/ChartKit.DataSources/KiwoomRestDataSource.Realtime.cs
csharp_chartkit/tests/ChartKit.EngineVerification/ScriptedKiwoomWebSocket.cs
csharp_chartkit/tests/ChartKit.EngineVerification/KiwoomRealtimeReconnectVerification.cs
```

검증 시나리오:

```text
REST seed
첫 연결 LOGIN/REG 각 1회
seed 봉 Update
연결 종료
두 번째 연결 LOGIN/REG 각 1회
같은 봉 Update
다음 봉 Append
```

검증 내용:

```text
재연결 중 RealtimeCandleBuilder 유지
누적 거래량 연속
sequence 정상 증가
연결당 LOGIN 1회
연결당 REG 1회
ConnectionAttempts 2
RegistrationCount 2
stale 0
```

자동검증:

```text
csharp_realtime_reconnect_continuity=PASS
csharp_realtime_one_registration_per_connection=PASS
csharp_realtime_builder_survives_reconnect=PASS
```

동일 시각·동일 가격·동일 수량 체결은 신뢰 가능한 공급자 체결번호가 없으므로 중복 제거하지 않는다.

---

# 6. nullable 경고 정리

원인:

```text
TryGetRealtimeDiagnosticsState가 성공 시 non-null임을 컴파일러가 알지 못함
```

수정:

```csharp
[NotNullWhen(true)] out RealtimeDiagnosticsState? state
```

결과:

```text
DataSources CS8602 5개 제거
호출부와 reconnect 동작 변경 없음
null-forgiving 연산자로 경고 은폐하지 않음
Release build PASS
EngineVerification PASS
App self-test PASS
```

잔존 경고:

```text
OpenTK 3.1.0 NU1701
OpenTK.GLControl 3.1.0 NU1701
```

이 경고는 GPU/OpenGL 의존성 정리 단계에서 별도 처리한다.

---

# 7. Architecture Baseline 1.0 — 확정

상태:

```text
Architecture Baseline 1.0
Approved
```

기준 문서:

```text
docs/chart-module-platform/README.md
docs/chart-module-platform/architecture-baseline-1.0.md
docs/chart-module-platform/architecture-constitution.md
docs/chart-module-platform/feature-capability-matrix.md
docs/chart-module-platform/implementation-roadmap.md
docs/chart-module-platform/module-file-standard.md
docs/chart-module-platform/templates/ChartModule.template.cs
scripts/verify_chart_module_headers.ps1
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

기능 단위:

```text
개별 기능 → 개별 <Feature>Module.cs 또는 작은 기능 폴더
유사 기능군 → 하나의 ChartKit.Modules.* 프로젝트
특수 런타임·독립 배포 → 별도 프로젝트 또는 플러그인
```

모든 신규 `*Module.cs` 파일은 상단에 `<chart-module>` 연결 계약을 기록한다.

필수 연결 정보:

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

CI 검사:

```powershell
.\scripts\verify_chart_module_headers.ps1
```

핵심 불변식:

```text
Renderer는 개별 기능명을 모른다.
Module은 SkiaSharp와 WinForms를 모른다.
UI는 기능별 메뉴·설정 Form을 하드코딩하지 않는다.
Context Menu·Quick Button·Property Inspector는 동일 메타데이터에서 생성한다.
기능 On/Off는 범용 명령 하나로 처리한다.
비활성 모듈 계산·구독·Contribution 비용은 0에 가깝게 유지한다.
```

---

# 8. 로컬 동기화

현재 로컬이 `fix/csharp-realtime-nullability`에 있을 수 있으므로 기준 브랜치로 복귀한다.

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

git status
git fetch origin
git switch csharp/standalone-engine
git pull --ff-only origin csharp/standalone-engine

git status
git log -3 --oneline
git rev-parse HEAD
```

검증:

```powershell
.\scripts\verify_chart_module_headers.ps1

dotnet build .\csharp_chartkit\ChartKit.CSharp.sln -c Release

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

---

# 9. 다음 작업 순서

## P0. 다음 거래일 실데이터 검증

대표종목 `005930` 또는 `000660`, 1분봉으로 확인한다.

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

## P0-2. 실제 물리 재연결

```text
connection attempts +1
registration count +1
reconnecting → registered → receiving
현재 봉 상태 유지
다음 봉 정상 append
```

## P0-3. soak recorder 및 soak

```text
working/private/managed memory
threads
handles/GDI
queue depth/max
accepted/processed/published/errors
realtime diagnostics
latency
CSV/JSONL 기록
종료 요약 및 threshold 판정
```

검증 범위:

```text
1종목 6시간
20종목 6시간
100종목 replay 6시간
종목·주기 100회 변경
최소화·복원
```

## P1. 모듈 플랫폼 첫 수직 경로

```text
1. ChartKit.Modules.Abstractions
2. ChartKit.Scene
3. ChartKit.ModuleHost / Registry
4. ChartProfile / Persistence
5. ChartKit.Composition / ChartRenderPlan
6. PlatformProbeModule
7. 공용 Context Menu
8. 공용 Quick Button
9. 공용 Property Inspector
10. 기존 SMA Module 이전
11. RSI → MACD → SuperTrend 이전
```

`PlatformProbeModule` 완료 기준:

```text
표준 파일 상단 계약
Registry 등록
Profile On/Off
숫자·색상 Property
Command metadata
Polyline Contribution
SceneCompiler
RenderPlan
Renderer 출력
저장·복원
비활성 계산 0
오류 격리
CI 검사 통과
```

실시간 P0 검증과 모듈 플랫폼 P1은 분리된 브랜치에서 진행한다.

---

# 10. 하지 말아야 할 실수

```text
틱 데이터를 시간으로 정렬
동일 HHmm 행 삭제
오류를 감추기 위해 틱 행 제거
과거 휴장일 판정에 분봉 사용
대표종목 probe로 realtime seed 덮어쓰기
조회 실패를 휴장으로 판정
동일 시각/가격/수량만으로 실시간 체결 중복 제거
재연결마다 RealtimeCandleBuilder 초기화
Renderer에 RSI/전략/호가 등 기능별 분기 추가
MainForm에 기능별 Toggle 메서드 추가
모듈에서 SKCanvas 직접 사용
기능 파일 상단 <chart-module> 계약 생략
기능마다 별도 csproj 남발
CI 실패 코드를 로컬 pull 대상으로 안내
Draft PR #3 병합
실데이터 검증 전에 PR #3 병합
```
