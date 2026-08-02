# ChartKit C# 세션 인수인계

작성 시각: 2026-08-02 10:56 KST  
저장소: `hoonoh57/ChartKit`  
로컬 경로: `E:\2026\gpt\vb\sciaChart\ChartKit`  
기준 브랜치: `csharp/standalone-engine`  
현재 기준 커밋: `b90c8a7c41083f32ef6b0599d9e71e6c93fb8e93`  
VWAP 체크포인트: `checkpoint/csharp-module-platform-p2-vwap-pass`  
장기 Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
장중 검증 Draft PR: `#21 Add Kiwoom actual-session validation probe`  
기존 VB 안정 기준: `316f0decfa5981d081f88585d4db5d4d18830c0f`

---

# 0. 현재 확정 상태

```text
P1 모듈 플랫폼 기반                 완료·병합
P1 App/Renderer 수직 연결            완료·병합
P2 SMA parity                        완료·병합
P2 RSI parity                        완료·병합
P2 MACD parity                       완료·병합
P2 SuperTrend parity                 완료·병합
P2 JMA parity                        완료·병합
P2 OBV parity                        완료·병합
P2 Disparity parity                  완료·병합
P2 VWAP parity                       완료·병합
VWAP 수동 화면 검증                  완료
VWAP 설정·색상 재시작 복원           완료
Skia AccessViolation 회귀 수정        완료·병합
종목명/코드 자동완성                 완료·병합
강조 행 Enter 선택                   완료·병합
종목 입력 포커스 전체선택            완료·병합
최근 10종목 이름+코드 MRU            완료·병합
실제 Kiwoom REST 과거봉              완료
실제 장중 WebSocket                  미완료
물리 네트워크 재연결                 미완료
장중 soak                            미완료
PR #3 Ready/merge                    금지 상태 유지
```

장중 검증 미완료는 `PR #3`과 실시간 검증 PR의 최종 승인·병합 게이트다. 일반 기능 개발, UI 개선, 리플레이 검증, 자동 테스트, 성능 개선은 계속 진행한다.

---

# 1. 최신 병합 기록

## 1-1. VWAP

```text
PR #20: Migrate VWAP to the module platform with legacy parity
verified head: 187303fb4c129ee41452e9b63875e58238b27f4b
merge commit: 1b8a0284a232347b5685e74805e41e7ff43a7c60
checkpoint: checkpoint/csharp-module-platform-p2-vwap-pass
```

## 1-2. 종목명 자동완성

```text
PR #22: Add Kiwoom instrument name autocomplete
verified head: 8e902490b04265627f89ae3edcf0ad42b66912f6
merge commit: b90c8a7c41083f32ef6b0599d9e71e6c93fb8e93
base: csharp/standalone-engine
state: merged
```

확정 기능:

```text
Kiwoom ka10099 종목 마스터를 앱 세션에서 1회 캐시
종목명·6자리 코드 부분검색
키 입력마다 서버 호출하지 않고 로컬 검색
검색 후보에 종목명·코드·시장·NXT 표시
방향키/Enter 및 마우스 선택
드롭다운이 열리면 현재 강조 행을 Enter 우선 선택
입력란 최초 포커스 시 기존 값 전체 선택
최근 조회 종목 최대 10개 MRU
MRU를 종목명 [6자리코드] 형식으로 표시
선택 후 기존 과거봉·종목정보·차트·실시간 구독 재구성 경로 재사용
직접 6자리 코드 입력 유지
```

수동 검증 사례:

```text
SK 입력
→ SK하이닉스 [000660] 강조
→ Enter
→ 000660 / SK하이닉스 선택

034020 두산에너빌리티
일봉 240봉
종목정보·차트 동기화
modules 1/9 plan 5 faults 0
```

---

# 2. 장중 검증 도구 상태

브랜치:

```text
csharp/realtime-validation-probe
head: 76ab87613c466f829d33bbfe11a6ea143176e088
PR #21: Draft, 미병합
```

검증 도구는 지정 시간 전체 실행, 다종목 REST seed 연속성, Update/Append, stale 거부, 연결 시도, 등록 횟수, 물리 재연결 여부를 요약한다.

실제 REST 과거봉 검증 완료:

```text
source=Kiwoom CSharp REST real
symbols=005930,000660
005930 count=240
000660 count=240
range=2026-07-31 11:20:00 ~ 15:30:00
kiwoom_history_validation=PASS
```

PowerShell에서 KRX 코드는 반드시 문자열로 전달한다.

```powershell
--symbols "005930,000660"
```

따옴표가 없으면 `005930`이 `5930`으로 변형될 수 있다. probe는 잘린 숫자 종목코드를 API 호출 전에 거부한다.

---

# 3. 장중 검증과 일반 개발의 분리 원칙

장중이 반드시 필요한 검증:

```text
실제 WebSocket 장중 체결 수신
물리 네트워크 단절·복구
실제 종목 등록 복원
장중 장시간 soak
```

장중과 무관하게 계속 진행:

```text
제품 기능 개발
차트 운용성 개선
종목 검색·선택 UX
리플레이·fixture 자동검증
강제 소켓 종료·지연·중복등록 테스트
성능·메모리·종료 경합 개선
문서화
```

실제 장중 검증은 실시간 경로의 최종 후보 HEAD가 정해진 뒤 한 번에 수행한다. 관련 없는 기능 변경마다 장이 열리기를 기다리지 않는다.

---

# 4. 다음 세션 첫 확인

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

git status
git fetch origin
git switch csharp/standalone-engine
git pull --ff-only origin csharp/standalone-engine
git log -5 --oneline
git rev-parse HEAD
```

다음 세션에서는 최소한 아래 커밋이 로그에 있어야 한다.

```text
handoff 갱신 커밋
b90c8a7 Merge Kiwoom instrument name autocomplete
8e90249 Fix autocomplete selection and recent symbol UX
```

기준 브랜치 작업 트리는 깨끗해야 한다.

---

# 5. 다음 개발 운영 순서

장중 검증을 기다리며 전체 개발을 중단하지 않는다.

```text
1. csharp/standalone-engine 최신화
2. 제품 기능마다 별도 feature branch 생성
3. Release build + EngineVerification + App self-test
4. 필요한 실제 화면 수동검증
5. 기능 PR 병합
6. 실시간 경로 최종 후보가 정해지면 PR #21 도구로 장중 검증
7. 실제 WebSocket·물리 재연결·soak 완료 후에만 PR #3 Ready/merge 판단
```

현재 즉시 가능한 개발 범위:

```text
실제 종목 차트 운용성 개선
다중 종목 전환 UX
조회 기간·주기·표시봉수 UX
로딩·오류·취소 상태 개선
비장중 실시간 경로 자동검증 강화
종료·재시작·구독 재구성 경합 검증
```

새 작업은 반드시 별도 브랜치에서 시작한다. `csharp/standalone-engine`을 직접 기능 개발 브랜치로 사용하지 않는다.

---

# 6. 최상위 불변식 — 시장 데이터 순서

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

동일 시각·동일 가격·동일 수량 체결은 신뢰 가능한 공급자 체결번호가 없으므로 삭제하지 않는다.

`ChartKit.DataSources`의 정렬 API는 CI 단계 `Reject DataSources row-reordering APIs`가 차단한다. 종목검색 결과 순위 계산도 DataSources에서 금지된 정렬 API를 사용하지 않는다.

---

# 7. 모듈·렌더러 경계

표준 연결 경로:

```text
ChartProfile
→ Module Registry / ChartModuleHost
→ 활성 Module
→ ChartContribution
→ SceneCompiler
→ immutable ChartRenderPlan
→ generic Skia renderer
```

고정 원칙:

```text
Renderer는 SMA/RSI/MACD/VWAP 등 기능 이름을 모른다.
Module은 SkiaSharp와 WinForms를 참조하지 않는다.
UI는 지표별 메뉴·설정 Form을 하드코딩하지 않는다.
비활성 모듈에는 데이터가 전달되지 않고 Contribution도 0이다.
VWAP 세션 경계는 TradingDate로만 판정한다.
```

금지 참조:

```text
Engine → WinForms/SkiaSharp
Rendering → DataSources
Charting → DataSources/WinForms
Modules → WinForms/SkiaSharp
Renderer → 기능별 전용 분기
C# product solution → VB runtime project
```

---

# 8. Skia AccessViolation 회귀 수정

재현 오류:

```text
System.AccessViolationException
SkiaSharp.SkiaApi.sk_canvas_draw_path
SkiaChartRenderer.DrawLineSeries
```

수정 내용:

```text
변환 후 x/y 좌표 finite 검사
유효점 없는 빈 SKPath는 DrawPath에 전달하지 않음
동일 SkiaChartRenderer의 중첩·동시 Render 진입 직렬화
렌더 중 Dispose 경합 방지
예외 시 SKCanvas.Restore 보장
Histogram SKPaint.Style 복원 보장
병렬 재진입 EngineVerification 추가
```

회귀 표식:

```text
csharp_rendering_reentry_guard=PASS
csharp_rendering_verification=PASS
```

---

# 9. 기본 검증 명령

```powershell
.\scripts\verify_chart_module_headers.ps1

dotnet build `
  .\csharp_chartkit\ChartKit.CSharp.sln `
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

핵심 표식:

```text
csharp_datasources_no_row_sort=PASS
kiwoom_instrument_search_name=PASS
kiwoom_instrument_search_code=PASS
kiwoom_instrument_search_cache=PASS
csharp_rendering_reentry_guard=PASS
csharp_engine_verification=PASS
csharp_app_self_test=PASS
```

기존 OpenTK/OpenTK.GLControl `NU1701`은 잔존 허용 경고다.

---

# 10. PR과 병합 게이트

병합 완료:

```text
PR #20 VWAP parity
PR #22 Kiwoom instrument name autocomplete
```

계속 Draft·미병합:

```text
PR #3 Build standalone C# multi-symbol chart engine
PR #21 Kiwoom actual-session validation probe
```

PR #3 병합 금지 조건:

```text
실제 장중 WebSocket 미완료
물리 네트워크 재연결 미완료
장중 soak 미완료
```

이 조건은 다른 독립 기능 PR의 개발·검증·병합을 막지 않는다.
