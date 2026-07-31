# ChartKit C# 세션 인수인계

작성 시각: 2026-08-01 06:56 KST  
저장소: `hoonoh57/ChartKit`  
작업 브랜치: `csharp/standalone-engine`  
Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
PR base: `improve/chart-engine-hardening`  
기존 VB 안정 기준: `316f0decfa5981d081f88585d4db5d4d18830c0f`  
문서 작성 직전 검증된 C# 커밋: `82a9c9205f6ef4d30f23d90485dd1f3c40e96d89`  
Windows CI:
- C# Engine run `30667880537`: **success**
- Legacy Inventory run `30667880530`: **success**

> 이 문서를 읽기 전에 코드를 수정하지 말 것. 새 세션은 반드시 이 문서의 **최상위 금지사항**과 **다음 세션 첫 작업**부터 따른다.

---

# 0. 최상위 금지사항 — 틱 데이터 순서

## 0.1 틱 데이터에 `Sort`, `OrderBy`, 시간값 기반 재정렬을 절대 사용하지 않는다

Cybos 틱 데이터는 경우에 따라 시간 정보가 `HHmm`까지만 들어온다. 같은 분 안의 여러 틱은 시간값이 동일할 수 있으므로 다음 방식은 **데이터 순서를 파괴한다**.

```text
금지:
- List.Sort(...)
- Array.Sort(...)
- OrderBy(CloseTime)
- ThenBy(OpenTime)
- Sequence를 정렬 키로 사용해 원본 순서를 재구성
- Dictionary/SortedDictionary에 시각을 키로 넣어 재조립
- 같은 HHmm이라는 이유로 중복 제거
- OHLCV가 같다는 이유로 틱 제거
```

같은 `HHmm` 틱의 실제 선후는 **원본 배열 위치**가 유일한 근거일 수 있다. 정렬하면 시가·종가·틱 수·120틱 봉 경계가 모두 깨질 수 있다.

## 0.2 허용되는 방향 변환은 배열 전체 Reverse뿐이다

소스 API 계약상 최신→과거 방향으로 반환된다는 것이 명확할 때만 다음이 허용된다.

```text
허용:
- 원본 순서를 그대로 유지: Forward
- 배열 전체를 한 번 뒤집기: ReverseWhole
```

개별 행을 비교해 위치를 바꾸면 안 된다.

현재 계약:

```csharp
public enum SourceArrayDirection
{
    Forward = 0,
    ReverseWhole = 1
}
```

`MarketDataNormalizer.NormalizeHistory()`는 호출자가 명시한 방향만 적용해야 한다. 시간값을 보고 방향을 추론하거나 정렬해서는 안 된다.

## 0.3 틱 중복 제거 금지

틱에는 신뢰할 수 있는 거래 고유번호가 아직 없다. 따라서 다음 기준으로 중복 제거하면 안 된다.

```text
금지 중복키:
- CloseTime
- OpenTime
- HHmm
- 가격 + 거래량
- OHLCV 전체
```

같은 분·같은 가격·같은 수량의 체결은 실제로 여러 번 발생할 수 있다. 공급자가 제공하는 고유 체결번호가 확인되기 전까지 틱 행은 제거하지 않는다.

## 0.4 페이지 결합도 행 정렬이 아니라 페이지 방향 계약으로 처리한다

연속조회 페이지는 다음 2개 방향을 각각 명시해야 한다.

```text
- 페이지 내부 행 방향
- 페이지 간 방향
```

페이지 병합 시 허용되는 조작:

```text
- 페이지를 받은 순서대로 append
- 페이지 전체를 앞에 prepend
- 각 페이지 배열 전체 Reverse
- 최종 배열 전체 Reverse
```

금지:

```text
- 모든 페이지를 합친 후 시간값으로 sort
- 같은 시각 행을 하나로 축약
```

---

# 1. 사용자 최우선 요구사항

1. 완전히 독립 실행 가능한 **C# 전용 차트 프로그램**을 만든다.
2. VB 프로그램은 제품 실행 경로에 남기지 않는다.
3. 기존 VB는 지표 결과 병렬 비교용 기준으로만 사용한다.
4. 차트 고유 로직은 오직 계산된 화면 모델을 그리는 데 전념한다.
5. 데이터 조회, 지표 계산, 입력 상태, 전략, 주문, 도구 기능은 차트 렌더러와 분리한다.
6. 수천 개 기능을 추가해도 핵심 엔진·렌더러가 오염되지 않는 구조를 유지한다.
7. 사용자에게 중간 구현을 떠넘기지 않는다. 원격 서버에서 구현·Windows CI 검증 후 마지막 실제 데이터 연결만 로컬에서 확인한다.
8. 단계마다 복원 가능한 체크포인트를 만든다.
9. `main`과 기존 VB 안정 브랜치는 임의로 변경하거나 병합하지 않는다.
10. 실제 데이터에서 어느 하나라도 이상하면 기능 추가보다 데이터 무결성과 핵심 안정성을 먼저 해결한다.

---

# 2. 로컬 및 원격 위치

로컬:

```text
E:\2026\gpt\vb\sciaChart\ChartKit
```

작업 브랜치:

```text
csharp/standalone-engine
```

C# 독립 폴더:

```text
csharp_chartkit
```

C# solution:

```text
csharp_chartkit/ChartKit.CSharp.sln
```

실행 프로젝트:

```text
csharp_chartkit/src/ChartKit.App/ChartKit.App.csproj
```

Draft PR #3은 아직 병합하지 않는다.

---

# 3. 현재 C# 프로젝트 구조

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

책임 경계:

```text
ChartKit.Contracts
- Candle, Tick, CandleEvent
- timeframe
- snapshot
- 데이터소스·메타데이터 계약

ChartKit.Engine
- 다종목 bounded-channel 처리
- 종목별 FIFO
- 종목 간 병렬 처리
- 캔들·지표 링버퍼
- 지표 증분 계산
- snapshot 발행

ChartKit.Charting
- viewport
- 표시 구간
- 가격·시간축
- 패널 레이아웃
- 한국 주식 호가단위
- 십자선 좌표
- 레전드 화면 모델

ChartKit.Rendering
- ChartFrame/Snapshot을 SkiaCanvas에 그리기
- 데이터 조회·지표 수식·입력 상태를 소유하지 않음

ChartKit.DataSources
- Kiwoom REST/WebSocket
- replay
- 연속조회
- 소스 방향 계약
- 틱봉 재집계

ChartKit.App
- WinForms 셸
- 종목·주기·총봉·표시봉 입력
- 메뉴·정보패널·컨텍스트 메뉴
- 사용자 명령을 각 계층에 전달
```

참조 금지 원칙:

```text
Engine → WinForms/SkiaSharp 참조 금지
Rendering → DataSources 참조 금지
Charting → DataSources/WinForms 참조 금지
Contracts → 상위 계층 참조 금지
```

---

# 4. 현재 구현 완료 기능

## 4.1 C# 독립 제품

- VB 참조 없는 C# 전용 solution
- C# WinForms 독립 실행 앱
- Windows publish artifact
- deterministic replay 모드
- 실제 Kiwoom mock/real 환경설정 경로

## 4.2 다종목 엔진

- bounded channel
- 종목별 FIFO
- 종목 간 병렬 처리
- queue depth/latency/error metrics
- 고정 용량 캔들·지표 링버퍼
- 20종목 self-test

## 4.3 C# 포트 지표 8개

```text
MA
JMA
RSI
OBV
Disparity
MACD
SuperTrend
VWAP
```

- 전체 계산/증분 계산 비교
- VB 결과와 봉별·결과키별 parity 검증

## 4.4 차트 기능

- 실제 Kiwoom 과거 데이터 캔들 차트
- 거래량
- 오버레이 지표
- RSI/OBV/Disparity/MACD 서브패널
- 메인 가격축
- 서브패널별 우측 Y축
- 시간축
- 날짜 경계
- 한국 주식 호가단위 가격축
- 우측 미래 봉 공간
- 상단·하단 안전 여백
- 좌우 이동
- 가격축 상하 이동
- 마우스 중심 확대·축소
- 십자선
- 선택 봉 OHLCV
- 전체 패널 수직 크로스헤어
- 지표 레전드
- 마우스 이탈 시 최신 봉 레전드
- steady-state 렌더링 할당 0 bytes 검증

## 4.5 상단 셸

- 종목코드 직접 입력
- 최근 종목 드롭다운
- 종목명 표시
- 주기 선택
- 실제 다운로드 총 봉수 입력
- 화면 표시 봉수 입력
- 마우스 줌과 표시 봉수 양방향 동기화
- 조회 버튼
- 일자 토글
- 종목정보 패널
- 도구 메뉴
- 차트 컨텍스트 메뉴
- 상태 표시줄

WinForms `ComboBox.CreateHandle()` 오류 때문에 활성 툴바는 다음만 사용한다.

```text
ToolStripTextBox
ToolStripDropDownButton
ToolStripButton
ToolStripLabel
```

네이티브 `ToolStripComboBox`를 다시 사용하지 않는다.

## 4.6 키움 데이터

- OAuth token
- 요청 간격 제어
- 401 재인증
- 429 전역 대기
- 분/틱/일/주/월 과거조회
- 연속조회
- 종목명·시장 메타데이터
- REST history seed → WebSocket realtime 연결 구조

---

# 5. 현재 데이터 방향 관련 코드 상태

## 5.1 `MarketDataNormalizer`

파일:

```text
csharp_chartkit/src/ChartKit.DataSources/MarketDataNormalizer.cs
```

현재 방향:

- `SourceArrayDirection.Forward`
- `SourceArrayDirection.ReverseWhole`
- `Sort/OrderBy` 없음
- 틱 데이터는 시간순 판정으로 재배열하지 않음
- 틱 데이터는 시간값으로 중복 제거하지 않음
- 무효 가격·음수 거래량·잘못된 시각은 행을 조용히 삭제하지 않고 예외로 중단
- OHLC 범위는 입력 행의 위치를 바꾸지 않고 행 내부에서만 복구

중요:

`OHLC 범위 복구`도 원본 체결 순서를 변경하지 않는다. 다만 원시 데이터 보존이 더 중요하다고 판단되면 향후 복구 대신 오류 보고만 하도록 정책을 재검토할 수 있다. 사용자 확인 없이 틱 행 자체를 삭제하면 안 된다.

## 5.2 `TickCandleAggregator`

파일:

```text
csharp_chartkit/src/ChartKit.DataSources/TickCandleAggregator.cs
```

현재 상태:

- 정렬 없음
- 시간값 기반 방향 교정 없음
- 전달받은 배열 순서대로 그룹 생성
- 거래일 경계를 넘지 않음
- 각 거래일의 최신 쪽에서 target tick 단위로 묶은 뒤 결과 배열만 전체 Reverse

주의:

이 집계기는 **입력 방향이 이미 계약대로 정해져 있다는 전제**를 가진다. 다음 세션은 집계기에서 순서를 추론하지 말고 Kiwoom/Cybos source adapter에서 방향을 명시해야 한다.

## 5.3 `KiwoomRestDataSource.History`

현재 Kiwoom API 반환이 최신→과거라는 계약을 기준으로 `ReverseWhole`을 명시한다.

```text
분봉: ReverseWhole
일/주/월: ReverseWhole
틱 base candle: ReverseWhole
집계 완료 target tick: Forward
```

이 설정을 시간값 정렬로 대체하지 않는다.

---

# 6. 현재 검증 상태

문서 작성 직전 검증 커밋:

```text
82a9c9205f6ef4d30f23d90485dd1f3c40e96d89
```

CI:

```text
ChartKit CSharp Engine
run 30667880537
success

ChartKit CSharp Legacy Inventory
run 30667880530
success
```

통과 범위:

```text
C# product boundary
Release build
ring buffer
8개 지표 증분 동등성
다종목 FIFO
viewport
가격·시간축
한국 주식 호가단위
서브패널 Y축
십자선/레전드
렌더링 무할당
Kiwoom session fake 검증
Kiwoom history fake 검증
replay
app self-test
실제 WinForms 셸 smoke
VB↔C# 지표 parity
Windows publish
```

알려진 경고:

```text
OpenTK 3.1.0 NU1701
OpenTK.GLControl 3.1.0 NU1701
```

현재 CPU `SKControl` 기준이며 GPU 전환은 아직 하지 않는다.

---

# 7. 최근 로컬 실제 결과

최근 화면:

```text
종목: 034020_AL 두산에너빌리티
주기: 5분
요청 총 봉수: 4,000
실제 수신/엔진 봉수: 약 4,000
현재 화면 표시: 줌 상태에 따라 약 416봉
상태: events 0, errors 0
```

정상 확인:

- 실제 Kiwoom 과거봉 표시
- 총 봉수/표시 봉수 분리
- 줌과 표시 봉수 동기화
- 오버레이·서브지표 표시
- 종목정보 패널
- 호가단위 축

과거 발생 오류:

```text
120틱 / 총 4,000봉
Base tick candles must be ascending
```

원인 추정:

- 연속조회 페이지 방향 결합 문제
- 행 단위 정렬로 해결하면 Cybos HHmm 틱 순서를 파괴하므로 금지

현재 코드는 정렬을 제거하고 source-declared `ReverseWhole` 방식으로 변경됐지만, **실제 로컬 120틱 4,000봉 재검증은 다음 세션 최우선 작업**이다.

---

# 8. 다음 세션 첫 작업 — 순서 고정

## 8.1 먼저 현재 브랜치와 문서를 확인한다

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

git status
git switch csharp/standalone-engine
git pull --ff-only origin csharp/standalone-engine
git rev-parse HEAD
Get-Content .\session_handoff.md -TotalCount 160
```

로컬 변경이 있으면 덮어쓰거나 reset하지 말고 먼저 확인한다.

## 8.2 Release 빌드

```powershell
dotnet build .\csharp_chartkit\ChartKit.CSharp.sln -c Release
```

기대:

```text
0 errors
OpenTK NU1701 known warnings only
```

## 8.3 문제 조건 재현

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

확인 항목:

```text
예외 0
요청 봉수와 실제 봉수
첫 봉/마지막 봉 시각
거래일 경계
각 120틱 봉의 OHLCV
차트 급격한 비정상 점프 여부
마지막 봉 seed
```

## 8.4 raw page 방향 진단을 먼저 만든다

실패하면 집계기에서 정렬하지 말고 진단 로그를 추가한다.

필수 로그:

```text
source name
api id
page index
page row count
page first raw index/time
page last raw index/time
page direction contract
merged first/last raw index
ReverseWhole 적용 여부
base candle count
target candle count
```

민감정보·API key·token은 로그에 출력하지 않는다.

## 8.5 Kiwoom과 Cybos 방향 계약을 별도 fixture로 만든다

필수 테스트:

### Kiwoom 최신→과거 전체 Reverse

```text
입력 원본 배열 위치 보존
전체 Reverse 1회
120틱 그룹 경계 확인
```

### Cybos 동일 HHmm 다중 틱

예:

```text
09:01 price 100 qty 1 rawIndex 0
09:01 price 101 qty 2 rawIndex 1
09:01 price 99  qty 3 rawIndex 2
```

검증:

```text
Forward면 0→1→2 유지
ReverseWhole이면 2→1→0
시간/가격/수량 Sort 없음
중복 제거 없음
```

### 페이지 결합

```text
각 페이지 내부 순서 보존
페이지 단위 append/prepend만 사용
결과가 계약된 방향과 일치
```

## 8.6 완료 기준

```text
120틱 4,000봉 실제 로컬 PASS
Cybos HHmm 동일시간 fixture PASS
정렬 API 검색 결과 0
틱 행 삭제 0
틱 수 보존
OHLCV 그룹 결과 검증
Windows CI 전체 PASS
새 체크포인트 생성
```

코드 검색:

```powershell
Get-ChildItem .\csharp_chartkit -Recurse -Filter *.cs |
    Select-String -Pattern "\.Sort\(|Array\.Sort|OrderBy\(|ThenBy\(|SortedDictionary" 
```

발견 시 틱 데이터 경로인지 반드시 검토한다. 일반 UI 목록 정렬과 틱 데이터 정렬은 구분하되, DataSources 틱 경로에서는 0건이어야 한다.

---

# 9. 다음 기능 추진 순서

데이터 방향 검증이 완료되기 전에는 기능을 추가하지 않는다.

완료 후 순서:

```text
P1. 실데이터 장중 REST 마지막 봉 ↔ WebSocket 첫 봉 연결 검증
P2. 장중 6시간 soak test
P3. Range Navigator
P4. 과거 데이터 지연 로딩
P5. 패널 splitter·숨김·순서 변경
P6. 지표 등록소·속성창
P7. 작도 도구
P8. 전략·주문 오버레이
P9. 다중 차트 탭·분할
P10. GPU A/B 검증
```

---

# 10. 장중 실시간 검증 미완료 항목

```text
WebSocket 로그인/등록 상태 표시
REST 마지막 봉과 첫 realtime update 중복 여부
동일 봉 update
새 봉 append
재연결 후 중복 구독
KRX/NXT 세션 전환
네트워크 단절 복구
장중 6시간 메모리/핸들/GC 안정성
```

이 항목들은 CI fake test만으로 완료 처리하지 않는다. 실제 장중 로컬 증거가 필요하다.

---

# 11. 핵심 엔진 보호 원칙

1. 기능을 추가하기 전에 어느 계층의 책임인지 결정한다.
2. 렌더러는 데이터 조회·지표 수식·마우스 상태를 소유하지 않는다.
3. App은 명령을 해석하되 엔진 내부 상태를 직접 수정하지 않는다.
4. DataSources는 원시 공급자 계약을 표준 Candle 계약으로 바꾸되 원본 순서를 임의로 재구성하지 않는다.
5. 틱 데이터는 배열 위치가 정보다.
6. 새 기능은 가능한 별도 provider/controller/overlay로 추가한다.
7. steady-state 렌더링 할당 0 bytes 기준을 유지한다.
8. 모든 변경은 Release build, 전체 verifier, app self-test, desktop smoke, VB parity, publish를 통과해야 한다.
9. 성공 후 체크포인트를 생성한다.
10. 사용자에게 로컬 검증을 요청하는 시점은 서버 CI 전체 통과 후다.

---

# 12. Git/CI 운영 원칙

- `main` 변경 금지
- Draft PR #3 병합 금지
- `improve/chart-engine-hardening` 변경 금지
- 작업은 `csharp/standalone-engine`에서만 진행
- 실패한 HEAD를 로컬 pull 대상으로 안내하지 않음
- Windows CI 전체 성공 HEAD만 안내
- known OpenTK NU1701을 0 warning이라고 허위 보고하지 않음
- 원격 작업 중이라고만 말하고 실제 tool 작업 없이 대기하지 않음

최근 체크포인트 예:

```text
checkpoint/csharp-engine-pass
checkpoint/csharp-rendering-pass
checkpoint/csharp-chart-viewport-pass
checkpoint/csharp-chart-axes-pass
checkpoint/csharp-chart-crosshair-pass
checkpoint/csharp-chart-legends-pass
checkpoint/csharp-chart-price-grid-pass
checkpoint/csharp-subchart-axes-pass
checkpoint/csharp-chart-shell-pass
checkpoint/csharp-chart-shell-layout-pass
checkpoint/csharp-chart-data-inputs-pass
checkpoint/csharp-toolbar-handle-pass
checkpoint/csharp-toolbar-combobox-free-pass
checkpoint/csharp-tick-order-toolbar-pass
```

---

# 13. 새 세션에서 하지 말아야 할 실수

```text
- Cybos HHmm 틱을 시간으로 정렬
- 오류를 없애기 위해 틱을 임의 삭제
- 같은 시각을 중복으로 간주
- 집계기 내부에서 공급자 방향 추론
- UI 기능 때문에 Engine/Rendering 계약 변경
- CI 통과 전 사용자에게 pull 요청
- 장중 실시간을 fake test만으로 완료 선언
- Draft PR 병합
```

---

# 14. 새 세션 첫 응답 기준

사용자가 `진행`이라고 하면 설명만 하지 말고 다음을 실제로 수행한다.

```text
1. session_handoff.md 읽기
2. PR #3 current HEAD 확인
3. 현재 CI 상태 확인
4. 틱 경로의 Sort/OrderBy 검색
5. 120틱 4,000봉 재현용 source-direction fixture 확인/보강
6. 서버 Windows CI 실행
7. 실패 시 job log의 첫 원인부터 수정
8. 성공 HEAD와 로컬 재검증 명령 안내
```

최우선 성공 조건은 기능 추가가 아니라 다음이다.

> **Cybos와 Kiwoom의 원본 틱 순서를 절대 훼손하지 않으면서 120틱 4,000봉을 정확하게 구성하는 것.**
