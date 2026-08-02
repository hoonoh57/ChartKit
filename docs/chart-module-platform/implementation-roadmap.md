# ChartKit 범용 모듈 플랫폼 구현 로드맵

상태: **Architecture Freeze Candidate 1**  
문서 버전: `1.0-rc1`  
작성일: 2026-08-01

이 문서는 기존 ChartKit의 데이터·엔진·차팅·렌더링 안정성을 유지하면서 범용 모듈 플랫폼으로 점진 이전하는 순서와 단계별 완료 기준을 고정한다.

---

# 1. 추진 원칙

```text
기존 기능을 한 번에 재작성하지 않는다.
새 플랫폼을 기존 경로 옆에 추가한다.
대표 기능 하나씩 이전한다.
매 단계 기존 결과·성능·실행 안정성을 비교한다.
검증된 단계마다 체크포인트를 만든다.
```

핵심 규칙:

```text
기능 추가보다 연결 표준을 먼저 만든다.
Renderer 변경 없이 기능이 연결되는지 매 단계 확인한다.
새 전용 Primitive를 쉽게 추가하지 않는다.
비활성 모듈 비용 0을 검증한다.
기존 8개 지표 parity를 유지한다.
```

---

# 2. 전체 단계

```text
P0  실데이터 안정성 마감
P1  플랫폼 핵심 계약
P2  기존 8개 지표 모듈 이전
P3  공용 UI·Property Inspector·PanelGraph
P4  비교종목·시장지수·복수 데이터
P5  작도·Interaction·상태 저장
P6  Range Navigator·대용량 데이터
P7  DSL 지표·신호·전략
P8  호가·체결·고빈도 overlay
P9  시장분석·매매후보·주문·다중 차트
P10 제품화·극한 성능·GPU A/B
```

---

# 3. P0 — 실데이터 안정성 마감

현재 진행 중인 검증을 플랫폼 구현 전에 마감한다.

## 작업

```text
실제 거래일 005930/000660 1분봉 REST seed
첫 WebSocket 체결 seed-update/seed-append
물리 네트워크 단절·재연결
중복 구독 0
stale 0
sequence 역전 0
1종목 6시간 soak
종목·주기 100회 반복 변경
```

## 완료 기준

```text
예외 0
중복 봉 0
시간 역전 0
sequence 역전 0
재연결 후 등록 횟수 정상
UI 멈춤 0
지속적인 메모리·핸들 증가 없음
```

## 체크포인트

```text
checkpoint/csharp-live-boundary-pass
checkpoint/csharp-single-symbol-soak-pass
```

P0가 완료되기 전에도 P1의 순수 계약·테스트 프로젝트는 시작할 수 있지만, 기존 실시간 경로의 대규모 수정은 하지 않는다.

---

# 4. P1 — 플랫폼 핵심 계약

목표: 화면 변화 없이 Module Registry → Profile → Contribution → Scene → RenderPlan의 최소 수직 경로를 만든다.

## 4.1 신규 프로젝트

```text
ChartKit.Modules.Abstractions
ChartKit.Scene
ChartKit.ModuleHost
ChartKit.Composition
ChartKit.Persistence
```

처음부터 프로젝트를 과도하게 나누기보다 참조 방향을 명확하게 유지할 수 있는 최소 단위로 시작한다. 필요하면 이후 분리한다.

## 4.2 핵심 타입

```text
IChartModule
IDataRequirementProvider
IChartComputationModule
IChartVisualProvider
IChartInteractionProvider
IChartCommandProvider
IChartPropertyProvider
IChartPersistentStateProvider
ChartModuleDefinition
ChartModuleProfile
ChartProfile
ChartCommandDescriptor
ChartPropertyDescriptor
ChartChangeImpact
PanelRequest
AxisRequest
ContributionSet
ChartScene
ChartRenderPlan
```

## 4.3 최소 Vertical Slice

`PlatformProbeModule`을 만든다.

기능:

```text
On/Off
한 개 숫자 파라미터
한 개 색상 파라미터
가격 패널 Polyline
컨텍스트 메뉴 자동 생성
퀵버튼 자동 생성
우측 Property Inspector 표시
프로필 저장·복원
```

이 모듈은 실제 지표가 아니라 플랫폼 연결 검증용이다.

## 4.4 CI 검사

```text
Rendering → Modules 참조 금지
Module → Rendering/SkiaSharp 참조 금지
App에 PlatformProbeModule 이름별 분기 금지
비활성 모듈 BuildContributions 0회
프로필 round-trip
Property ChangeImpact별 최소 invalidation
Fault isolation
```

## 완료 기준

```text
Registry 등록 1줄
Profile On
차트 표시
Profile Off
차트 제거
파라미터 변경
저장 후 재실행 복원
Renderer 기능별 코드 수정 0
```

## 체크포인트

```text
checkpoint/csharp-module-platform-vertical-slice-pass
```

---

# 5. P2 — 기존 8개 지표 모듈 이전

목표: 기존 기능 결과를 그대로 유지하며 중앙 고정 팩토리를 제거할 수 있음을 검증한다.

이전 순서:

```text
1. SMA/MA
2. RSI
3. MACD
4. SuperTrend
5. JMA
6. OBV
7. Disparity
8. VWAP
```

이 순서는 서로 다른 출력 요구를 빠르게 검증한다.

```text
SMA          가격 패널 단일선
RSI          독립 패널 + 기준선
MACD         복수선 + 히스토그램
SuperTrend   조건색상선
```

## 이전 전략

각 지표는 일정 기간 구경로와 신규 Module 경로를 동시에 계산한다.

```text
Legacy Indicator Runtime
New Module Runtime
→ 봉별 IndicatorPoint 비교
→ Contribution 결과 비교
```

## 단계별 검증

각 지표마다:

```text
전체 계산 parity
마지막 봉 Update parity
새 봉 Append parity
On/Off
파라미터 변경
패널 이동
색상 변경
Profile round-trip
비활성 계산 0
렌더 성능
```

## 중앙 팩토리 제거 조건

모든 8개 지표가 신규 경로를 통과한 뒤에만 `DefaultIndicatorFactory`를 기본 실행 경로에서 제거한다.

마이그레이션 검증 경로는 일정 기간 유지한다.

## 완료 기준

```text
8개 지표 신규 Module Registry 등록
기존 결과 parity
Renderer 변경 0 또는 범용 Series 개선만 허용
SymbolRuntime 직접 생성 제거
Profile 기반 인스턴스 생성
동일 지표 복수 인스턴스 지원
```

## 체크포인트

```text
checkpoint/csharp-module-indicators-parity-pass
```

---

# 6. P3 — 공용 UI·Property Inspector·PanelGraph

목표: 기능이 추가될 때 UI 코드를 추가하지 않는 구조를 완성한다.

## 6.1 공용 컨텍스트 메뉴

자동 분류:

```text
가격·거래량
기술적 지표
비교종목·지수
전략·신호
작도
호가·체결
시장분석
패널·화면
```

기능 검색과 체크 상태를 Registry/Profile에서 가져온다.

## 6.2 퀵버튼

```text
모듈 명령에서 자동 생성
사용자 고정·해제
프로필 저장
모든 기능을 툴바에 나열하지 않음
```

## 6.3 공용 Property Inspector

지원 editor:

```text
Boolean
Integer
Decimal
String
Enum
Color
LineStyle
Symbol
Timeframe
PanelId
Formula
DateRange
Collection
```

Property 변경은 `ChartChangeImpact`에 따라 최소 범위만 갱신한다.

## 6.4 Selection Service

```text
차트 선 클릭
범례 클릭
모듈 목록 선택
컨텍스트 메뉴 속성
패널 선택
```

모두 같은 Inspector 대상을 선택한다.

## 6.5 PanelGraph

기존 `MaximumPanelIndex=7` 경로를 호환 계층으로 유지하면서 `PanelId` 기반 새 경로를 추가한다.

대표 테스트:

```text
가격 패널
거래량 패널
RSI
MACD
RSI 패널 위 추가 SMA
패널 높이 드래그
순서 변경
접기·복원
```

## 완료 기준

```text
지표 전용 설정 Form 0
MainForm 기능별 메뉴 분기 0
공용 Inspector에서 모든 지표 설정
패널 상태 저장·복원
패널 On/Off 후 레이아웃 정상
```

## 체크포인트

```text
checkpoint/csharp-common-ui-panelgraph-pass
```

---

# 7. P4 — 비교종목·시장지수·복수 데이터

목표: 현재 종목의 단일 데이터만 가정하지 않는지 검증한다.

구현 순서:

```text
1. KOSPI 정규화 오버레이
2. 삼성전자 정규화 오버레이
3. 삼성전자 독립 서브차트
4. 삼성전자 서브차트 위 SMA
5. 다수 종목 스택 패널
```

필요 계약:

```text
DataRequirement
Symbol/Timeframe identity
시간축 alignment
결측 정책
ScaleGroup
독립축·공유축
복수 구독 수명주기
```

금지:

```text
ComparisonModule이 직접 DataSource 생성
Renderer가 Symbol을 조회
시간값 정렬로 틱 순서 재구성
```

## 완료 기준

```text
모듈 On 시 추가 데이터 자동 구독
Off 시 구독 해제
정규화 기준 저장
독립 서브차트 생성
서브차트 위 다른 모듈 연결
결측 데이터 정책 표시
```

## 체크포인트

```text
checkpoint/csharp-multidata-panel-overlay-pass
```

---

# 8. P5 — 작도·Interaction·상태 저장

구현 순서:

```text
1. 수평선
2. 추세선
3. 피보나치 되돌림
4. 텍스트 메모
5. 구간 측정
```

필요 플랫폼 기능:

```text
HitTestIndex
InteractionRouter
DragHandle
Selection
Command history
Undo/Redo
Symbol State
Snap service
```

대표 검증:

```text
컨텍스트 메뉴 또는 퀵버튼으로 도구 On
차트에서 생성
선택·이동
Inspector에서 가격·색상 변경
Undo/Redo
프로그램 재시작 후 복원
도구 Off 시 숨김, 데이터 보존 정책 확인
```

Renderer는 Line/Text/FillArea/Handle Primitive만 처리한다.

## 체크포인트

```text
checkpoint/csharp-drawing-interaction-pass
```

---

# 9. P6 — Range Navigator·대용량 데이터

구현 순서:

```text
하단 overview
현재 구간 선택창
선택창 이동
핸들 줌
왼쪽 끝 과거 데이터 지연 로딩
로딩 후 화면 anchor 유지
```

성능 검증:

```text
4,000봉
100,000봉
500,000봉
표시 구간만 geometry 생성
과거 로딩 중 UI 응답 유지
```

Range Navigator도 별도 Module/Contribution로 구현하되, 전체 ChartViewport와 협력하는 플랫폼 모듈로 분류한다.

## 체크포인트

```text
checkpoint/csharp-range-navigator-pass
```

---

# 10. P7 — DSL 지표·신호·전략

구현 순서:

```text
1. Lexer/Parser/AST
2. Type checker
3. 함수 catalog
4. dependency graph
5. NumericSeries 지표
6. BooleanSeries 신호
7. 진입·청산 전략
8. 다른 ID 지표 참조
9. 멀티타임프레임
10. 다른 종목 참조
```

첫 수식:

```text
CrossUp(avg(c, MA1), avg(c, MA2))
```

필수 결과:

```text
MA Cross 신호 Marker
Property Inspector의 MA1/MA2
On/Off
색상 변경
저장·복원
수식 검증 오류 표시
```

DSL은 Renderer를 호출하지 않는다.

```text
AST
→ Runtime Series
→ Module Contribution
→ Scene
```

## 체크포인트

```text
checkpoint/csharp-dsl-module-pass
```

---

# 11. P8 — 호가·체결·고빈도 overlay

구현 순서:

```text
1. 체결강도
2. 대량체결 Marker
3. 10단계 호가
4. 호가 불균형
5. 호가 히트맵
6. Tick tape SidePanel
```

별도 고빈도 layer를 사용한다.

```text
캔들·지표 정적 layer
Interaction layer
High-frequency overlay layer
```

호가 변화가 전체 Scene을 재컴파일하지 않아야 한다.

성능 기준:

```text
초당 고빈도 업데이트
Render thread block 0
bounded queue
coalescing 정책
관리 힙 지속 증가 없음
캔들·지표 프레임 영향 제한
```

## 체크포인트

```text
checkpoint/csharp-high-frequency-overlay-pass
```

---

# 12. P9 — 시장분석·매매후보·주문·다중 차트

## 시장분석 대표

```text
조건검색 편입 Marker
실시간 후보 점수 Badge
매수 우선순위 SidePanel
시장 강세 배경
```

한 모듈이 다음을 동시에 제공할 수 있어야 한다.

```text
Chart Contribution
SidePanel Descriptor
Status Contribution
Command Descriptor
```

## 주문·포지션 대표

```text
진입가
평균단가
손절가
목표가
포지션 손익 영역
주문대기·부분체결 Marker
```

## 다중 차트

```text
종목 탭
2·4·6·9분할
시간축 동기화
십자선 동기화
보이지 않는 차트 렌더 중지
```

## 체크포인트

```text
checkpoint/csharp-market-trading-multichart-pass
```

---

# 13. P10 — 제품화·극한 성능·GPU A/B

성능 조합:

```text
활성 모듈 10/50/100
패널 5/10/20
표시 봉 500/4K/100K
마커 100/1K/10K
비교종목 2/10/20
호가 고빈도
다중 차트 1/4/9
```

측정:

```text
frame time
P50/P95/P99
CPU
메모리
GC allocation
GDI/USER handle
queue depth
snapshot latency
module calculation latency
scene compile latency
```

GPU A/B:

```text
SKControl CPU
vs
SKGLControl GPU
```

GPU는 실제 프레임·CPU·전력·안정성에서 우세할 때만 채택한다.

제품화:

```text
설정 진단
데이터 연결 진단
성능 진단 화면
로그 파일
crash dump
자동 복구
버전 정보
단일 publish
설치 패키지
```

---

# 14. 각 단계 공통 완료 게이트

모든 단계는 다음을 통과해야 한다.

## 정확성

```text
기존 결과 parity
Update/Append 정합성
Profile round-trip
On/Off 결과
상태 복원
```

## 경계

```text
Renderer 기능명 참조 0
모듈 SkiaSharp 참조 0
App 기능별 분기 증가 0
새 기능 전용 Renderer API 0
```

## 성능

```text
비활성 계산 0
부분 invalidation
steady-state allocation 기준 유지
프레임 기준 악화 허용범위 내
```

## 안정성

```text
모듈 오류 격리
다른 모듈 계속 동작
기존 차트 유지
명확한 진단
```

## 운영

```text
Windows CI success
세션 문서 갱신
복원 체크포인트
Capability Matrix 상태 갱신
```

---

# 15. 첫 실제 구현 순서

문서 승인 후 바로 시작할 작업은 다음으로 고정한다.

```text
1. 신규 프로젝트와 참조 방향 생성
2. Module Registry
3. ChartProfile 최소 모델
4. Command/Property Descriptor
5. ContributionSet
6. Scene/RenderPlan 최소 모델
7. PlatformProbeModule
8. 컨텍스트 메뉴 자동 생성
9. 공용 Property Inspector 최소 구현
10. Profile JSON 저장·복원
11. On/Off·Property·Fault·성능 자동검증
12. SMA Module 이전
```

첫 단계에서는 패널 동적화, DSL, 호가를 동시에 구현하지 않는다.

PlatformProbeModule과 SMA가 동일 경로로 성공한 뒤 RSI, MACD, SuperTrend 순서로 확장한다.

---

# 16. 기능별 작업 기록 템플릿

각 기능은 다음 형식으로 기록한다.

```text
Feature ID:
Module ID:
Instance example:
Purpose:
Data requirements:
Calculation model:
Incremental update policy:
Panel/axis request:
Visual contributions:
Interaction:
Commands:
Properties:
Persistence scope:
Change impacts:
Fault policy:
Performance budget:
Unit verification:
Integration verification:
Renderer changes:
CI result:
Checkpoint:
Status:
```

`Renderer changes`는 기본적으로 `None`이어야 한다.

---

# 17. 현재 확정 대기 항목

사용자 승인 전 검토할 항목:

```text
[ ] 프로젝트 구분 수준
[ ] Primitive 초기 목록
[ ] Property type 초기 목록
[ ] Profile 병합 우선순위
[ ] PanelGraph 요구사항
[ ] 첫 대표 기능 세트
[ ] 단계 순서
[ ] P0와 P1 병행 범위
```

승인되면 문서 상태를 다음으로 변경한다.

```text
Architecture Freeze Candidate 1
→ Architecture Baseline 1.0
```

그 이후 기능 개발은 이 문서의 순서와 완료 게이트를 따른다.
