# ChartKit C# 세션 인수인계

작성 시각: 2026-08-01 12:16 KST  
저장소: `hoonoh57/ChartKit`  
작업 브랜치: `csharp/standalone-engine`  
Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
PR base: `improve/chart-engine-hardening`  
기존 VB 안정 기준: `316f0decfa5981d081f88585d4db5d4d18830c0f`  
현재 검증 HEAD: `b55f4b260beb87c9a5669289f1894f35d717ee51`

Windows CI:

```text
ChartKit CSharp Engine
run 30681571514
result success

ChartKit CSharp Legacy Inventory
run 30681571493
result success
```

복원 체크포인트:

```text
checkpoint/csharp-120tick-source-order-pass
checkpoint/csharp-rest-realtime-boundary-pass
checkpoint/csharp-representative-trading-day-pass
```

---

# 0. 최상위 금지사항 — 틱 데이터 순서

Cybos 틱 데이터는 `HHmm`까지만 제공될 수 있다. 같은 분의 여러 체결은 동일 시각이므로 시간·가격·수량·sequence 기준 정렬 또는 중복 제거를 절대 사용하지 않는다.

금지:

```text
List.Sort
Array.Sort
OrderBy
ThenBy
SortedDictionary로 시간 재조립
CloseTime/OpenTime 기반 재정렬
Sequence 기반 원본 재정렬
동일 HHmm 행 삭제
가격+수량/OHLCV 중복 제거
```

허용되는 방향 변경:

```text
Forward
ReverseWhole
페이지 append/prepend
페이지 또는 전체 배열 한 번 Reverse
```

`ChartKit.DataSources`의 정렬 API는 Windows CI 단계 `Reject DataSources row-reordering APIs`가 차단한다.

---

# 1. 현재 제품 구조

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
Rendering: 계산된 ChartFrame/Snapshot만 Skia로 그림
DataSources: Kiwoom REST/WebSocket, replay, 정상화, 집계
App: WinForms shell, 사용자 입력, 상태 표시
```

금지 참조:

```text
Engine → WinForms/SkiaSharp
Rendering → DataSources
Charting → DataSources/WinForms
C# product solution → VB project
```

---

# 2. 현재 완료 상태

## 2.1 독립 C# 차트

```text
VB 참조 없는 C# solution
독립 WinForms 실행
Windows publish
키움 REST 과거봉
키움 WebSocket 계약
replay
```

## 2.2 엔진과 지표

```text
종목별 이벤트 순서 보장
종목 간 병렬 처리
bounded channel/backpressure
고정 용량 ring buffer
snapshot 주기 제한
20종목 self-test
MA/JMA/RSI/OBV/Disparity/MACD/SuperTrend/VWAP
VB↔C# 지표 parity
```

## 2.3 차트와 UI

```text
캔들/거래량/오버레이/서브패널
우측 Y축과 시간축
한국 주식 호가단위
좌우 이동/줌/가격축 이동
십자선/레전드
우측 미래 12봉 공간
종목코드/종목명/주기/총봉/표시봉
종목정보 패널
실시간·경계 진단
```

## 2.4 120틱 4,000봉

로컬 확인:

```text
symbol 034020_AL
timeframe 120T
history 4,000
gap 0
errors 0
비정상 가격 점프 없음
```

자동검증:

```text
30틱 원본 16,004행
17개 연속조회 페이지
모든 원본 시각 동일 HHmm
정렬·중복 제거 없음
ReverseWhole만 사용
최종 120틱 4,000봉 OHLCV 검증
```

---

# 3. REST → WebSocket 경계 진단

우측 패널과 하단 상태줄에 표시:

```text
연결: connecting/login/registered/receiving/reconnecting/faulted
경계: waiting/seed-update/seed-append/no-seed-append
Update/Append 수
연결 시도/등록 수
stale 거부 수
REST seed 시각
첫 realtime 체결 시각
```

틱 실시간 정책:

```text
동일 시각 체결 허용
현재 틱봉 마지막 체결시각보다 엄격히 과거인 이벤트만 stale 거부
정렬 없음
동일 시각 중복 제거 없음
```

토요일 로컬 확인:

```text
ws registered
boundary waiting
events 0
stale 0
errors 0
```

장중 첫 체결 검증은 아직 남아 있다.

---

# 4. 거래일·휴장 판정 — 대표종목 데이터 방식

복잡한 휴장일 달력을 유지하지 않는다.

대표종목:

```text
005930 삼성전자
000660 SK하이닉스
```

판정 계약:

```text
오늘:
  각 대표종목의 최신 1분봉 1건 조회
  하나라도 오늘 날짜 → 거래일
  둘 다 정상 조회됐으나 오늘 날짜 없음 → 휴장

과거 날짜:
  각 대표종목의 일봉 1건 조회
  base_dt = 판정 대상 날짜
  하나라도 정확히 해당 날짜 → 거래일
  둘 다 정상 조회됐으나 해당 날짜 없음 → 휴장

조회 실패/일부 실패:
  휴장으로 오판하지 않고 Unknown

미래 날짜:
  Unknown
```

중요:

```text
대표종목 probe는 realtime seed를 저장하거나 덮어쓰지 않는다.
오늘은 1분봉, 과거는 일봉이므로 과거 휴장일 검색이 빠르다.
실시간 이벤트가 실제로 수신되면 기존 no-data 판정보다 거래일을 우선한다.
```

구현 파일:

```text
csharp_chartkit/src/ChartKit.DataSources/TradingDayProbe.cs
csharp_chartkit/src/ChartKit.App/MainForm.TradingDay.cs
csharp_chartkit/src/ChartKit.App/MainForm.Shell.cs
csharp_chartkit/tests/ChartKit.EngineVerification/TradingDayProbeVerification.cs
```

화면 표시:

```text
하단: day trading / day closed / day unknown
우측 거래일:
  거래일, 대표 1분봉, 005930/000660
  휴장, 대표 1분봉, 005930/000660 없음
  확인불가, 대표종목 조회 오류
```

자동검증:

```text
csharp_trading_day_today_minute=PASS
csharp_trading_day_history_daily=PASS
csharp_trading_day_failure_unknown=PASS
```

---

# 5. 현재 Git 상태

검증 HEAD:

```text
b55f4b260beb87c9a5669289f1894f35d717ee51
```

기능 코드 검증 커밋:

```text
5115d96aba3fb371c539233e38fdba78731d5285
```

`5115d96` 이후 임시 파일 추가·즉시 삭제 두 커밋이 있으나 최종 HEAD와 `5115d96`의 파일 차이는 0이다. 현재 HEAD도 Windows CI 전체 성공했다.

Draft PR #3은 계속 미병합 상태로 유지한다.

---

# 6. 로컬 반영 명령

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

Get-CimInstance Win32_Process |
Where-Object { $_.CommandLine -match "ChartKit\.App" } |
ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

git status
git switch csharp/standalone-engine
git pull --ff-only origin csharp/standalone-engine
git rev-parse HEAD

dotnet build .\csharp_chartkit\ChartKit.CSharp.sln -c Release
```

기대 HEAD:

```text
b55f4b260beb87c9a5669289f1894f35d717ee51
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

토요일 기대:

```text
day closed
ws registered
boundary waiting
events 0
stale 0
errors 0
```

우측 패널 기대:

```text
거래일  휴장, 대표 1분봉, 005930/000660 없음
```

---

# 7. 다음 작업 순서

1. 토요일 최신 빌드에서 `day closed` 실제 화면 확인
2. 다음 거래일 장중 `day trading` 확인
3. REST seed와 첫 WebSocket 체결:
   - 동일 봉이면 `seed-update`
   - 다음 봉이면 `seed-append`
4. stale 0, 중복 봉 0, sequence 역전 0 확인
5. 1종목 6시간 soak
6. 20종목 6시간 soak
7. 종목·주기 100회 반복 변경
8. Core Candidate 1 동결
9. Range Navigator와 과거 데이터 지연 로딩
10. 지표·패널 plugin
11. 작도·전략·주문 overlay
12. 다중 차트
13. GPU A/B

---

# 8. 완료 기준

```text
120틱 4,000봉 예외 0
틱 행 삭제 0
동일 HHmm 보존
시간/sequence 역전 0
실시간 동일 봉 update 정상
신규 봉 append 정상
REST/realtime 중복 0
재연결 중복 등록 0
대표종목 조회 오류를 휴장으로 오판하지 않음
과거 거래일 판정은 일봉 1건 경로 사용
UI 멈춤 0
메모리/핸들/GDI 지속 증가 없음
```

---

# 9. 새 세션에서 하지 말아야 할 실수

```text
틱 데이터를 시간으로 정렬
동일 HHmm 행 삭제
오류를 감추기 위해 틱 행 제거
과거 휴장일 판정에 분봉 연속조회 사용
대표종목 probe로 realtime seed 덮어쓰기
조회 실패를 휴장으로 판정
CI 실패 HEAD를 로컬 pull 대상으로 안내
Draft PR 병합
```
