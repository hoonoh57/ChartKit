# ChartKit C# 세션 인수인계

작성 시각: 2026-08-02 11:55 KST  
저장소: `hoonoh57/ChartKit`  
로컬 경로: `E:\2026\gpt\vb\sciaChart\ChartKit`  
기준 브랜치: `csharp/standalone-engine`  
기능 브랜치: `csharp/symbol-switch-cancellation`  
기준 브랜치 커밋: `22396f0fa47032ef74d796a1ec94d0b306a94f51`  
현재 기능 HEAD: `7768b6d69f06c2ba8d0055148442f6aa5c4fd07f`  
기능 Draft PR: `#23`  
장기 Draft PR: `#3 Build standalone C# multi-symbol chart engine`  
장중 검증 Draft PR: `#21 Add Kiwoom actual-session validation probe`

---

# 0. 현재 확정 상태

```text
P1 모듈 플랫폼 기반                 완료·병합
P1 App/Renderer 수직 연결            완료·병합
P2 SMA/RSI/MACD/SuperTrend           완료·병합
P2 JMA/OBV/Disparity/VWAP            완료·병합
종목명/코드 자동완성                 완료·병합
실제 Kiwoom REST 과거봉              완료
데이터 요청 직렬 대기 모델           구현·재검증 대기
실제 장중 WebSocket                  미완료
물리 네트워크 재연결                 미완료
장중 soak                            미완료
PR #3 Ready/merge                    금지 상태 유지
```

---

# 1. PR #23 설계 변경

초기 구현은 새 요청이 들어오면 실행 중 요청을 취소하고 history, engine, metadata, UI 단계마다 generation을 검사했다. 로컬 Release build, EngineVerification, App self-test는 통과했으나 운영 요구와 맞지 않아 폐기했다.

현재 확정 원칙:

```text
실행 중 요청은 정상 완료한다.
실행 중에는 다음 요청을 화면에 대기로 표시한다.
대기 슬롯은 1개만 유지한다.
대기 중 추가 입력은 실행 중 요청을 취소하지 않고 대기 슬롯만 최신 요청으로 병합한다.
현재 요청 완료 후 중앙 스케줄러가 다음 요청을 시작한다.
history/metadata/engine/UI 단계마다 개별 취소·generation 분기를 두지 않는다.
```

중앙 경로:

```text
UI 데이터 명령
→ DataRequestScheduler.EnqueueAsync
→ 실행 중 1건
→ 최신 대기 1건
→ 정상 완료
→ 다음 대기 요청 실행
```

화면 표식:

```text
데이터 처리 중 1.2s
이전 요청 처리 중 2.4s · 다음 요청 대기 0.8s
최근 1.6s · 최대 4.2s · 대기 병합 2
```

병목 계측:

```text
현재 실행 시간
현재 대기 시간
최근 완료 시간
최대 완료 시간
최대 대기 시간
총 입력 수
총 실행 완료 수
대기 병합 수
```

---

# 2. 변경 파일

```text
csharp_chartkit/src/ChartKit.App/DataRequestScheduler.cs
csharp_chartkit/src/ChartKit.App/DataRequestSchedulerVerification.cs
csharp_chartkit/src/ChartKit.App/MainForm.DataRequestLifecycle.cs
csharp_chartkit/src/ChartKit.App/MainForm.DataControls.cs
csharp_chartkit/src/ChartKit.App/MainForm.HandlePreparation.cs
csharp_chartkit/src/ChartKit.App/AppSelfTestRunner.cs
```

삭제:

```text
LatestRequestCoordinator.cs
LatestRequestCoordinatorVerification.cs
```

---

# 3. 검증 표식

새 App self-test 필수 표식:

```text
csharp_app_data_request_running_completes=PASS
csharp_app_data_request_pending_coalesced=PASS
csharp_app_data_request_serial=PASS
csharp_app_data_request_wait_metrics=PASS
csharp_app_data_request_application_stop=PASS
csharp_app_self_test=PASS
```

기존 전체 회귀 표식도 유지한다.

```text
chart_module_header_contract=PASS
csharp_engine_verification=PASS
csharp_app_self_test=PASS
```

---

# 4. 다음 로컬 검증

```powershell
Set-Location "E:\2026\gpt\vb\sciaChart\ChartKit"

git pull --ff-only origin csharp/symbol-switch-cancellation

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

수동 검증:

```text
1. 5,000봉 조회 시작
2. 완료 전에 다른 종목 선택
3. 하단에 이전 요청 처리 중·다음 요청 대기 표시 확인
4. 첫 요청이 오류 없이 정상 완료되는지 확인
5. 이어서 대기 종목이 자동 조회되는지 확인
6. 대기 중 여러 종목 선택 시 마지막 대기 종목만 실행되는지 확인
7. 최종 화면의 종목·메타데이터·차트가 일치하는지 확인
8. 최근/최대/대기 병합 계측 표시 확인
```

---

# 5. 유지 불변식

```text
시장 데이터 공급자 순서 재정렬 금지
동일 HHmm 틱 삭제 금지
Renderer 기능명 분기 금지
Module의 WinForms/SkiaSharp 참조 금지
VWAP 세션 경계는 TradingDate로만 판정
실제 WebSocket·물리 재연결·soak 전 PR #3 병합 금지
```
