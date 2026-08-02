# ChartKit C# 세션 인수인계

작성 시각: 2026-08-02 09:42 KST  
저장소: `hoonoh57/ChartKit`  
로컬 경로: `E:\2026\gpt\vb\sciaChart\ChartKit`  
기준 브랜치: `csharp/standalone-engine`  
VWAP 병합 커밋: `1b8a0284a232347b5685e74805e41e7ff43a7c60`  
VWAP 검증 기능 HEAD: `187303fb4c129ee41452e9b63875e58238b27f4b`  
VWAP 체크포인트: `checkpoint/csharp-module-platform-p2-vwap-pass`  
장기 Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
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
VWAP 체크포인트                      생성 완료
실제 장중 WebSocket                  미완료
물리 네트워크 재연결                 미완료
장중 soak                            미완료
PR #3 Ready/merge                    금지 상태 유지
```

PR #20은 검증된 HEAD `187303fb...`를 기준으로 Ready 전환 후 병합됐다.

```text
PR #20: Migrate VWAP to the module platform with legacy parity
state: merged
base: csharp/standalone-engine
verified head: 187303fb4c129ee41452e9b63875e58238b27f4b
merge commit: 1b8a0284a232347b5685e74805e41e7ff43a7c60
```

체크포인트는 병합 커밋을 가리킨다.

```text
checkpoint/csharp-module-platform-p2-vwap-pass
→ 1b8a0284a232347b5685e74805e41e7ff43a7c60
```

---

# 1. 다음 세션 첫 확인

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

git status
git fetch origin
git switch csharp/standalone-engine
git pull --ff-only origin csharp/standalone-engine
git log -5 --oneline
git rev-parse HEAD
```

`session_handoff.md` 갱신 커밋이 병합 커밋 위에 추가될 수 있으므로, 다음 세션에서는 반드시 아래 두 커밋을 로그에서 함께 확인한다.

```text
문서 갱신 커밋
1b8a028 Merge VWAP module platform parity
```

코드 복원 기준은 `checkpoint/csharp-module-platform-p2-vwap-pass`다.

---

# 2. 다음 실제 작업 — PR #3의 미완료 P0 검증

P2 지표 모듈 이전은 VWAP까지 완료됐다. 다음 우선순위는 신규 지표 추가가 아니라 장기 Draft PR #3의 실제 시장 검증이다.

## 2-1. 실제 장중 WebSocket

검증 대상:

```text
실제 Kiwoom 연결
실제 장중 체결 수신
종목별 FIFO 보존
분봉 Update/Append 연속성
UI 갱신 중 예외·정지 없음
stale tick 진단의 실제 동작
```

스크립트·fixture·mock 통과를 실제 장중 WebSocket 통과로 표현하지 않는다.

## 2-2. 물리 네트워크 재연결

검증 대상:

```text
실제 네트워크 단절
연결 종료 감지
재접속
종목 등록 복원
기존 CandleBuilder 상태 연속성
중복 등록 없음
체결 유실·재정렬 여부 확인
```

`scripted reconnect` 또는 테스트 더블 재연결은 물리 재연결이 아니다.

## 2-3. 장중 soak

최소 기록 항목:

```text
실행 시작·종료 시각
실제 연결 지속 시간
수신 이벤트 수
종목 수
최대 queue depth
stale/reconnect 진단
UI 응답성
예외·자동 종료 여부
메모리 추이
```

실제 장중 WebSocket·물리 재연결·soak 완료 전에는 PR #3을 Ready 전환하거나 병합하지 않는다.

---

# 3. 최상위 불변식 — 시장 데이터 순서

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

`ChartKit.DataSources`의 정렬 API는 CI 단계 `Reject DataSources row-reordering APIs`가 차단한다.

---

# 4. 모듈·렌더러 경계

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

# 5. VWAP 완료 계약

데이터 경로:

```text
Candle.TradingDate
→ MainForm.CreatePrimarySeriesSnapshot
→ ChartPrimaryBar.TradingDate
→ VwapSeriesRuntime
```

`VwapSeriesRuntime`은 `DateOnly.MinValue`를 거부한다.

출력 5개:

```text
Value
Upper1
Lower1
Upper2
Lower2
```

계산 계약:

```text
typicalPrice = (High + Low + Close) / 3
priceVolume += typicalPrice × Volume
volume += Volume
priceSquaredVolume += typicalPrice² × Volume

VWAP = priceVolume / volume
variance = max(0, priceSquaredVolume / volume - VWAP²)
deviation = sqrt(variance)
```

거래일 변경 시 누적 상태를 초기화한다.

```text
priceVolume
volume
priceSquaredVolume
```

누적 거래량이 0 이하이면 5개 출력은 모두 `NaN`이다.

---

# 6. VWAP 수동 검증 완료 기록

완료 항목:

```text
Legacy VWAP과 Module VWAP 5선 표시
modules 1/9
plan 5
faults 0
StdDev 파라미터 즉시 변경
색상 변경 즉시 반영
앱 종료·재시작 후 활성 상태 복원
앱 종료·재시작 후 StdDev 복원
앱 종료·재시작 후 색상 #FFFFFF 복원
1분봉 1323개 replay에서 장시간 실행
종목 전환·확대·축소·이동 중 자동 종료 없음
```

최종 사용자 확인:

```text
VWAP Stroke #FFFFFF
재시작 후에도 흰색 유지
```

---

# 7. Skia AccessViolation 회귀 수정

재현 오류:

```text
System.AccessViolationException
SkiaSharp.SkiaApi.sk_canvas_draw_path
SkiaChartRenderer.DrawLineSeries
```

재현 조건:

```text
--replay
--symbols S001,S002
--timeframe 1m
--count 1323
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

검증 기능 HEAD:

```text
187303fb4c129ee41452e9b63875e58238b27f4b
```

Windows CI:

```text
ChartKit CSharp Engine run 30725176224 success
ChartKit CSharp Legacy Inventory run 30725176207 success
```

핵심 표식:

```text
Build succeeded.
0 Error(s)
csharp_rendering_reentry_guard=PASS
csharp_rendering_verification=PASS
csharp_engine_verification=PASS
csharp_app_vwap_module_roundtrip=PASS
csharp_app_self_test=PASS
csharp_desktop_shell_smoke=PASS
legacy_parity_VWAP=PASS
publish=PASS
```

기존 OpenTK/OpenTK.GLControl `NU1701`은 잔존 허용 경고다.

---

# 8. 기본 검증 명령

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

Skia 크래시 회귀 화면 실행:

```powershell
dotnet run `
  --project .\csharp_chartkit\src\ChartKit.App\ChartKit.App.csproj `
  -c Release `
  --no-build `
  -- `
  --replay `
  --symbols S001,S002 `
  --timeframe 1m `
  --count 1323
```

---

# 9. PR과 체크포인트

병합 완료:

```text
PR #20
merge 1b8a0284a232347b5685e74805e41e7ff43a7c60
checkpoint/csharp-module-platform-p2-vwap-pass
```

계속 Draft·미병합:

```text
PR #3 Build standalone C# multi-symbol chart engine
```

PR #3 병합 금지 조건:

```text
실제 장중 WebSocket 미완료
물리 네트워크 재연결 미완료
장중 soak 미완료
```

---

# 10. 다음 세션 판단 기준

다음 세션은 P2 지표 이전을 다시 시작하지 않는다.

우선순위:

```text
1. 실제 장중 WebSocket 검증 계획과 실행 로그 형식 확정
2. 실제 장중 수신 검증
3. 물리 네트워크 재연결 검증
4. 장중 soak
5. 결과를 PR #3과 session_handoff.md에 기록
6. 모든 P0 게이트 완료 후에만 PR #3 Ready/merge 판단
```

시장 데이터 순서 불변식과 모듈 경계를 훼손하는 수정은 하지 않는다.
