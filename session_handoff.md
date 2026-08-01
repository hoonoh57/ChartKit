# ChartKit C# 세션 인수인계

작성 시각: 2026-08-01 12:43 KST  
저장소: `hoonoh57/ChartKit`  
작업 브랜치: `csharp/standalone-engine`  
Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
PR base: `improve/chart-engine-hardening`  
기존 VB 안정 기준: `316f0decfa5981d081f88585d4db5d4d18830c0f`

검증된 기능 코드 체크포인트:

```text
bd21cd849d44c5e4d892701326377b825f3af67d
```

Windows CI:

```text
ChartKit CSharp Engine
run 30682726299
result success

ChartKit CSharp Legacy Inventory
run 30682726300
result success
```

복원 체크포인트:

```text
checkpoint/csharp-120tick-source-order-pass
checkpoint/csharp-rest-realtime-boundary-pass
checkpoint/csharp-representative-trading-day-pass
checkpoint/csharp-realtime-reconnect-pass
```

현재 브랜치가 검증 기능 코드를 포함하는지 확인:

```powershell
git merge-base --is-ancestor `
  bd21cd849d44c5e4d892701326377b825f3af67d `
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

120틱 로컬 확인:

```text
034020_AL
120T
총 4,000봉
gap 0
errors 0
비정상 가격 점프 없음
```

120틱 자동검증:

```text
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
  각 대표종목의 최신 1분봉 1건 조회
  하나라도 오늘 날짜 → 거래일
  둘 다 정상 조회됐으나 오늘 날짜 없음 → 휴장

과거 날짜:
  각 대표종목의 일봉 1건 조회
  base_dt = 대상 날짜
  하나라도 정확히 해당 날짜 → 거래일
  둘 다 정상 조회됐으나 해당 날짜 없음 → 휴장

조회 실패/일부 실패:
  Unknown — 휴장으로 오판하지 않음

미래 날짜:
  Unknown
```

중요:

```text
오늘은 분봉, 과거는 일봉을 사용한다.
과거 휴장일 판정에 분봉 연속조회 금지.
대표종목 probe는 realtime seed를 저장하거나 덮어쓰지 않는다.
실시간 이벤트가 실제로 수신되면 no-data 판정보다 거래일을 우선한다.
```

자동검증:

```text
csharp_trading_day_today_minute=PASS
csharp_trading_day_history_daily=PASS
csharp_trading_day_failure_unknown=PASS
```

---

# 5. WebSocket 재연결 자동검증

실제 `ClientWebSocket` 경로를 테스트 가능한 내부 어댑터로 분리했다.

구현:

```text
csharp_chartkit/src/ChartKit.DataSources/IKiwoomWebSocket.cs
csharp_chartkit/src/ChartKit.DataSources/KiwoomRestDataSource.Realtime.cs
csharp_chartkit/tests/ChartKit.EngineVerification/ScriptedKiwoomWebSocket.cs
csharp_chartkit/tests/ChartKit.EngineVerification/KiwoomRealtimeReconnectVerification.cs
```

재현 시나리오:

```text
REST 5분봉 seed
첫 WebSocket 연결
LOGIN 1회
REG 1회
동일 seed 봉 Update
연결 종료
두 번째 WebSocket 연결
LOGIN 1회
REG 1회
동일 봉 추가 Update
다음 5분봉 Append
```

검증 내용:

```text
재연결 중 RealtimeCandleBuilder 유지
첫 Update 거래량 105
재연결 후 같은 봉 Update 거래량 112
다음 봉 sequence 1 Append
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

동일 시각·동일 가격·동일 수량 체결은 중복으로 제거하지 않는다. 신뢰 가능한 공급자 고유 체결번호가 없기 때문이다.

---

# 6. 로컬 반영

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

Get-CimInstance Win32_Process |
Where-Object { $_.CommandLine -match "ChartKit\.App" } |
ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

git status
git switch csharp/standalone-engine
git pull --ff-only origin csharp/standalone-engine

git merge-base --is-ancestor `
  bd21cd849d44c5e4d892701326377b825f3af67d `
  HEAD

$LASTEXITCODE

dotnet build .\csharp_chartkit\ChartKit.CSharp.sln -c Release
```

실행:

```powershell
dotnet run `
  --project .\csharp_chartkit\src\ChartKit.App\ChartKit.App.csproj `
  -c Release `
  --no-build `
  -- `
  --kiwoom `
  --symbol 034020_AL `
  --timeframe 120t `
  --count 4000
```

---

# 7. 다음 작업 — 순서 고정

## P1. 다음 거래일 실제 실시간 경계

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

대표종목은 실제 체결이 많은 `005930` 또는 `000660`으로 먼저 검증한다.

## P2. 실제 재연결

장중 네트워크를 한 번 차단하거나 연결을 재시작하여 확인한다.

```text
connection attempts 증가
registration count도 연결당 1씩 증가
reconnecting → registered → receiving
현재 봉 상태 유지
다음 봉 정상 append
```

## P3. soak

```text
1종목 6시간
20종목 6시간
100종목 replay 6시간
종목·주기 100회 반복 변경
최소화·복원
메모리·핸들·GDI 지속 관찰
```

## 이후

```text
Core Candidate 1 동결
Range Navigator와 과거 데이터 지연 로딩
지표·패널 plugin
작도·전략·주문 overlay
다중 차트
GPU A/B
```

---

# 8. 하지 말아야 할 실수

```text
틱 데이터를 시간으로 정렬
동일 HHmm 행 삭제
오류를 감추기 위해 틱 행 제거
과거 휴장일 판정에 분봉 사용
대표종목 probe로 realtime seed 덮어쓰기
조회 실패를 휴장으로 판정
동일 시각/가격/수량만으로 실시간 체결 중복 제거
재연결마다 RealtimeCandleBuilder 초기화
CI 실패 코드를 로컬 pull 대상으로 안내
Draft PR 병합
```
