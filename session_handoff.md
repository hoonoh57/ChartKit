# ChartKit C# 세션 인수인계

작성 시각: 2026-08-01 09:33 KST  
저장소: `hoonoh57/ChartKit`  
작업 브랜치: `csharp/standalone-engine`  
Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
PR base: `improve/chart-engine-hardening`  
기존 VB 안정 기준: `316f0decfa5981d081f88585d4db5d4d18830c0f`  
최종 Windows CI 검증 코드 커밋: `1d4c373dec5731b1f59595f5770ddab3d07bf8f7`  
복원 체크포인트: `checkpoint/csharp-120tick-source-order-pass`

Windows CI:

```text
ChartKit CSharp Engine
run 30675932345
result success

ChartKit CSharp Legacy Inventory
run 30675932390
result success
```

> 새 세션은 이 문서의 **0. 최상위 금지사항**과 **8. 다음 세션 첫 작업**부터 읽고 실제 작업을 시작한다.

---

# 0. 최상위 금지사항 — 틱 데이터 순서

## 0.1 틱 행을 정렬하지 않는다

Cybos 틱 데이터는 시간 정보가 `HHmm`까지만 들어오는 경우가 있다. 같은 분 안의 여러 틱은 시간값이 동일할 수 있으므로 시간·가격·거래량·sequence를 정렬 키로 사용하면 실제 체결 순서가 파괴된다.

금지:

```text
List.Sort
Array.Sort
OrderBy
ThenBy
SortedDictionary로 시간 재조립
CloseTime/OpenTime 기반 재정렬
Sequence를 키로 원본 순서 재구성
같은 HHmm 행 축약
가격+수량/OHLCV 기준 중복 제거
```

틱 데이터에서 배열 위치는 데이터 자체다.

## 0.2 허용되는 방향 변환은 배열 전체 Reverse뿐이다

공급자 계약이 최신→과거임이 명확할 때만 배열 전체를 한 번 뒤집는다.

```csharp
public enum SourceArrayDirection
{
    Forward = 0,
    ReverseWhole = 1
}
```

허용:

```text
Forward: 원본 배열 위치 유지
ReverseWhole: 배열 전체를 한 번 뒤집음
페이지 append/prepend
페이지 배열 전체 Reverse
최종 배열 전체 Reverse
```

금지:

```text
개별 행 위치 교환
시간값을 보고 방향 추론
모든 페이지 결합 후 sort
같은 시각 행 삭제
```

## 0.3 틱 중복 제거 금지

신뢰 가능한 공급자 체결 고유번호가 확인되기 전에는 틱 행을 제거하지 않는다.

다음 값은 중복키가 아니다.

```text
HHmm
CloseTime
OpenTime
가격+수량
OHLCV 전체
```

같은 시각·같은 가격·같은 수량의 체결이 실제로 여러 번 존재할 수 있다.

## 0.4 실패 시 행을 조용히 삭제하지 않는다

잘못된 가격, 음수 거래량, 잘못된 시각은 예외와 진단으로 중단한다. 틱 수를 맞추기 위해 행을 임의 삭제하지 않는다.

## 0.5 CI가 정렬 API를 차단한다

`.github/workflows/chartkit-csharp-engine.yml`의 다음 단계가 DataSources 내 행 재정렬 API를 자동 차단한다.

```text
Reject DataSources row-reordering APIs
```

현재 검출 대상:

```text
.Sort(
Array.Sort(
OrderBy(
ThenBy(
SortedDictionary<
```

검증 커밋 `1d4c373...`에서 이 단계가 PASS했다.

---

# 1. 사용자 최우선 요구사항

1. 완전히 독립 실행 가능한 C# 전용 차트 프로그램을 만든다.
2. 최종 제품 실행 경로에서 VB 프로젝트를 참조하지 않는다.
3. VB는 지표 결과 병렬 비교용 기준으로만 사용한다.
4. 렌더러는 계산된 화면 모델만 그린다.
5. 데이터 조회·지표·입력 상태·전략·주문·도구 기능은 렌더러와 분리한다.
6. 수천 개 기능을 추가해도 Engine/Rendering 핵심 계약이 오염되지 않도록 한다.
7. 원격에서 구현하고 Windows CI 전체 통과 후 실제 데이터 연결만 로컬에서 검증한다.
8. 단계마다 복원 체크포인트를 만든다.
9. `main`, 기존 VB 안정 브랜치, PR base를 임의 변경하거나 병합하지 않는다.
10. 실제 데이터 이상이 발견되면 기능 추가보다 데이터 무결성을 먼저 해결한다.

---

# 2. 로컬·원격 위치

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

Solution:

```text
csharp_chartkit/ChartKit.CSharp.sln
```

실행 프로젝트:

```text
csharp_chartkit/src/ChartKit.App/ChartKit.App.csproj
```

검증 프로젝트:

```text
csharp_chartkit/tests/ChartKit.EngineVerification/ChartKit.EngineVerification.csproj
```

Draft PR #3은 아직 병합하지 않는다.

---

# 3. 현재 제품 구조와 책임 경계

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

## ChartKit.Contracts

```text
Candle/Tick/CandleEvent
CandleTimeframe
Snapshot
데이터소스·메타데이터 계약
```

## ChartKit.Engine

```text
다종목 bounded channel
종목별 FIFO
종목 간 병렬 처리
고정 용량 candle/indicator ring buffer
증분 지표 계산
snapshot 발행
queue depth/latency/error metrics
```

## ChartKit.Charting

```text
viewport
표시 범위
가격·시간축
패널 레이아웃
한국 주식 호가단위
십자선 좌표
레전드 화면 모델
```

## ChartKit.Rendering

```text
ChartFrame/Snapshot의 Skia 렌더링
데이터 조회 없음
지표 수식 없음
마우스 상태 소유 없음
전략 판단 없음
```

## ChartKit.DataSources

```text
Kiwoom REST/WebSocket
replay
연속조회
소스 배열 방향 계약
틱봉 재집계
메타데이터
```

## ChartKit.App

```text
WinForms shell
종목·주기·총봉·표시봉 입력
메뉴·정보패널·컨텍스트 메뉴
사용자 명령 전달
```

참조 금지:

```text
Engine → WinForms/SkiaSharp 금지
Rendering → DataSources 금지
Charting → DataSources/WinForms 금지
Contracts → 상위 계층 금지
C# product solution → VB project 금지
```

---

# 4. 현재 구현 완료 기능

## 4.1 독립 C# 제품

```text
VB 참조 없는 C# solution
독립 WinForms 앱
Windows publish artifact
deterministic replay
Kiwoom mock/real 환경설정
```

## 4.2 다종목 엔진

```text
종목별 이벤트 순서 보장
종목 간 병렬 처리
bounded channel/backpressure
고정 용량 상태
snapshot 주기 제한
실시간 계산과 렌더링 주기 분리
20종목 self-test
```

## 4.3 C# 지표 8개

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

전체 재계산과 마지막 봉 증분 계산을 비교하며 VB 결과와 봉별·결과키별 parity를 검증한다.

## 4.4 차트

```text
실제 Kiwoom 과거 데이터
캔들/거래량
가격 오버레이
RSI/OBV/Disparity/MACD 서브패널
메인·서브패널 우측 Y축
시간축/날짜 경계
한국 주식 호가단위 가격축
우측 미래 공간
상·하단 안전 여백
좌우 이동
가격축 상하 이동
마우스 중심 줌
전체 패널 크로스헤어
선택 봉 OHLCV
지표 레전드
steady-state 관리 힙 할당 0 bytes
```

## 4.5 애플리케이션 셸

```text
종목코드 직접 입력
최근 조회 종목 메뉴
종목명 표시
주기 선택
실제 다운로드 총 봉수
화면 표시 봉수
줌 ↔ 표시 봉수 양방향 동기화
조회 버튼
일자 토글
종목정보 패널
도구/컨텍스트 메뉴
상태 표시줄
오류 발생 시 기존 차트 유지
```

WinForms `ComboBox.CreateHandle()` 문제 때문에 활성 툴바에서는 다음만 사용한다.

```text
ToolStripTextBox
ToolStripDropDownButton
ToolStripButton
ToolStripLabel
```

네이티브 `ToolStripComboBox`를 다시 사용하지 않는다.

## 4.6 Kiwoom 데이터

```text
OAuth token
요청 시작 간격 제어
401 재인증
429 전역 대기
분/틱/일/주/월 과거조회
연속조회
종목명·시장 메타데이터
REST history seed → WebSocket realtime 연결 구조
```

---

# 5. 틱 방향 코드 상태

## 5.1 MarketDataNormalizer

파일:

```text
csharp_chartkit/src/ChartKit.DataSources/MarketDataNormalizer.cs
```

현재 계약:

```text
Forward
ReverseWhole
Sort/OrderBy 없음
틱 시간 기준 중복 제거 없음
행 삭제 없음
잘못된 행은 InvalidDataException
```

비틱 캔들의 동일 시각 경계 병합 정책과 틱 정책을 혼동하지 않는다. 틱은 제거하지 않는다.

## 5.2 TickCandleAggregator

파일:

```text
csharp_chartkit/src/ChartKit.DataSources/TickCandleAggregator.cs
```

현재 계약:

```text
입력 배열 순서대로 그룹 구성
거래일 경계 통과 금지
각 거래일의 최신 쪽에서 target tick 단위 그룹 생성
그룹 결과 배열 전체 Reverse만 사용
시간값 기반 순서 추론 없음
```

## 5.3 KiwoomRestDataSource.History

현재 공급자 계약:

```text
분봉: ReverseWhole
일/주/월: ReverseWhole
틱 base candle: ReverseWhole
집계 완료 target tick: Forward
```

페이지는 수신 순서대로 append한다. 전체 결합 후 시간 정렬하지 않는다.

---

# 6. 이번 세션 완료 작업

## 6.1 120틱·4,000봉 source-order fixture 추가

파일:

```text
csharp_chartkit/tests/ChartKit.EngineVerification/KiwoomTickSourceOrderVerification.cs
```

테스트 조건:

```text
요청: 120틱 4,000봉
base: 30틱
필요 원본: 16,004행
연속조회: 17페이지
모든 원본 시각: 동일 20260730090100
원본 가격: 최신→과거 배열 방향
행 정렬 없음
중복 제거 없음
ReverseWhole 1회
```

검증 항목:

```text
최종 4,000봉
각 120틱 봉에 30틱 원본 4행 포함
모든 원본 행 보존
페이지 append 계약
첫/마지막 OHLC
전체 봉별 OHLC 순서
동일 HHmm 행 미축약
기존 tail sequence 계약
```

전체 verifier 출력에 다음 PASS가 추가됐다.

```text
csharp_kiwoom_120tick_4000_source_order=PASS
csharp_kiwoom_equal_hhmm_tick_order=PASS
csharp_kiwoom_tick_page_append_contract=PASS
```

## 6.2 DataSources 정렬 금지 CI 보호 추가

파일:

```text
.github/workflows/chartkit-csharp-engine.yml
```

CI 단계:

```text
Reject DataSources row-reordering APIs
```

검증 결과:

```text
csharp_datasources_no_row_sort=PASS
```

## 6.3 Windows CI 전체 통과

검증 코드 커밋:

```text
1d4c373dec5731b1f59595f5770ddab3d07bf8f7
```

통과 범위:

```text
C# product boundary
DataSources 정렬 API 0건
Release solution build
120틱 4,000봉 source-order fixture
동일 HHmm 틱 순서
연속조회 페이지 계약
ring buffer
8개 지표 증분 동등성
다종목 FIFO
viewport/축/호가단위
서브패널 Y축
십자선/레전드
렌더링 무할당
Kiwoom fake session/history
replay
app self-test
실제 WinForms shell smoke
VB↔C# 지표 parity
Windows publish
Legacy Inventory
```

## 6.4 복원 체크포인트

```text
checkpoint/csharp-120tick-source-order-pass
```

이 체크포인트는 검증 코드 커밋 `1d4c373...`을 가리킨다.

---

# 7. 아직 완료되지 않은 영역

## 7.1 실제 로컬 120틱·4,000봉

서버 fixture는 PASS했지만 실제 Kiwoom 응답으로 다음 조건을 로컬에서 다시 확인해야 한다.

```text
symbol 034020_AL
timeframe 120t
count 4000
```

확인:

```text
예외 0
실제 봉수
첫/마지막 봉 시각
거래일 경계
OHLCV 급변 여부
마지막 봉 seed
시간 역전 0
sequence 역전 0
```

실패해도 Sort/OrderBy/중복 제거를 추가하지 않는다. 먼저 raw page 방향 진단을 수집한다.

## 7.2 장중 WebSocket 실데이터

미완료:

```text
WebSocket 로그인/등록 상태
REST 마지막 봉 ↔ 첫 realtime update
동일 봉 update
새 봉 append
재연결 후 중복 구독
KRX/NXT 세션 전환
네트워크 단절 복구
장중 6시간 안정성
```

CI fake test만으로 완료 처리하지 않는다.

## 7.3 데이터 정상화 후속

```text
페이지 경계 진단
수정주가/기업행사
거래정지
가격제한폭
KRX/NXT 거래 세션 달력
휴장일/임시 휴장
불완전 마지막 봉
요청량보다 적은 반환 사유
```

## 7.4 UI 확장

```text
Range Navigator
과거 데이터 지연 로딩
패널 splitter/숨김/순서 변경
지표 등록소/속성창
작도 도구
전략·주문 오버레이
다중 차트
GPU A/B
```

---

# 8. 다음 세션 첫 작업 — 순서 고정

## 8.1 로컬 동기화

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

git status
git switch csharp/standalone-engine
git pull --ff-only origin csharp/standalone-engine
git rev-parse HEAD
Get-Content .\session_handoff.md -TotalCount 180
```

로컬 변경이 있으면 reset/restore하지 말고 먼저 내용을 확인한다.

## 8.2 Release 빌드

```powershell
dotnet build .\csharp_chartkit\ChartKit.CSharp.sln -c Release
```

기대:

```text
0 errors
OpenTK NU1701 known warnings only
```

## 8.3 실제 120틱·4,000봉 실행

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

확인 결과를 그대로 수집한다.

```text
요청 봉수
실제 봉수
첫 봉/마지막 봉 시각
거래일 경계
오류 메시지
차트 급격한 점프
마지막 봉 상태
```

## 8.4 실패 시 raw page 방향 진단

집계기에 정렬을 넣지 않는다. 다음 로그를 source adapter에 추가한다.

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

API key/token은 로그에 출력하지 않는다.

## 8.5 실제 120틱 PASS 후 다음 순서

```text
P1. REST 마지막 봉 ↔ WebSocket 첫 봉 연결 검증
P2. 1종목 장중 6시간 soak
P3. 20종목 장중 6시간 soak
P4. 종목·주기 100회 반복 변경
P5. 핵심 엔진 Core Candidate 1 동결
P6. Range Navigator/지연 로딩
P7. 패널·지표 plugin
P8. 작도·전략·주문 overlay
P9. 다중 차트
P10. GPU A/B
```

---

# 9. 완료 기준

## 실제 120틱·4,000봉

```text
예외 0
요청 4,000봉 충족 또는 부족 사유 명확
시간 역전 0
sequence 역전 0
틱 행 삭제 0
동일 HHmm 행 보존
OHLCV 그룹 정확
차트 비정상 점프 0
```

## 장중 실시간

```text
동일 봉 update 정상
새 봉 append 정상
REST/realtime 중복 0
재연결 중복 구독 0
UI 멈춤 0
메모리/핸들/GDI 지속 증가 없음
```

---

# 10. Git/CI 운영 원칙

```text
main 변경 금지
Draft PR #3 병합 금지
improve/chart-engine-hardening 변경 금지
작업은 csharp/standalone-engine에서만 진행
CI 실패 HEAD를 로컬 pull 대상으로 안내하지 않음
Windows CI 전체 성공 코드 커밋만 로컬 검증 대상으로 사용
known OpenTK NU1701 경고를 0 warning이라고 보고하지 않음
실제 tool 작업 없이 진행 중이라고만 말하지 않음
```

현재 검증 코드 HEAD:

```text
1d4c373dec5731b1f59595f5770ddab3d07bf8f7
```

현재 복원 지점:

```text
checkpoint/csharp-120tick-source-order-pass
```

---

# 11. 새 세션에서 하지 말아야 할 실수

```text
Cybos HHmm 틱을 시간으로 정렬
오류 제거를 위해 틱 행 삭제
같은 시각을 중복으로 간주
집계기에서 공급자 방향 추론
Sequence로 원본 배열 재정렬
페이지 결합 후 sort
UI 기능 때문에 Engine/Rendering 계약 변경
CI 통과 전 pull 요청
장중 실시간을 fake test만으로 완료 선언
Draft PR 병합
```

---

# 12. 최우선 성공 조건

> **Kiwoom과 Cybos의 원본 틱 배열 순서를 훼손하지 않고, 동일 HHmm 체결을 전부 보존한 상태에서 120틱 4,000봉을 정확히 구성한다.**
