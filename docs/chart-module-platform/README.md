# ChartKit 범용 모듈 플랫폼 문서

상태: **Architecture Freeze Candidate 1**  
작성일: 2026-08-01  
대상 브랜치: `csharp/standalone-engine`

이 디렉터리는 앞으로 ChartKit에 추가되는 모든 지표, 비교종목, 시장지수, 작도도구, 마우스 기능, 전략, 신호, 호가, 시장분석, 매매후보 및 외부 UI 기능의 공통 개발 기준이다.

핵심 목표는 다음 한 문장으로 고정한다.

> 개별 기능의 내부 복잡도는 깊어질 수 있지만, 차트 연결 복잡도는 항상 1이어야 한다.

모든 신규 기능은 다음 경로만 사용한다.

```text
개별 모듈 개발
→ Module Registry 등록
→ ChartProfile에서 On/Off
→ 표준 Contribution 생성
→ Scene Compiler가 RenderPlan으로 변환
→ Renderer가 범용 Primitive를 batch 출력
```

Renderer는 RSI, 전략, 호가, 피보나치, 비교종목 등의 업무 기능을 알지 않는다. Renderer는 제한된 범용 Primitive만 처리한다.

## 문서 구성

### 1. `architecture-constitution.md`

다음을 정의한다.

- 절대 변경하지 않을 아키텍처 원칙
- Module Registry와 범용 On/Off 스위치
- 역할별 모듈 인터페이스
- 공용 컨텍스트 메뉴·퀵버튼·Property Inspector
- ChartProfile과 종목별 상태 저장
- Contribution, Scene, RenderPlan, Primitive
- PanelGraph, 축, HitTest, Interaction Router
- invalidation, 캐시, double buffering
- 오류 격리, 버전 호환, CI 보호 규칙
- 신규 기능 1개를 추가하는 표준 절차

### 2. `feature-capability-matrix.md`

개발할 기능을 다음 범주로 분류한다.

- 기본 가격·거래량
- 기술적 지표
- 비교종목·시장지수
- 패널·축·레이아웃
- 작도·마우스·상호작용
- 전략·신호·주문·포지션
- 호가·체결·고빈도 정보
- 시장분석·매매대상 후보
- DSL 수식관리
- 작업공간·프로필·UI
- 다중 차트·동기화
- 진단·성능·내보내기

각 기능은 데이터 요구, 계산 특성, 출력 Primitive, 상호작용, 저장 상태, 성능 위험, 검증 단계로 분류한다.

### 3. `implementation-roadmap.md`

다음을 고정한다.

- 기존 실시간 안정성 검증과 플랫폼 구축의 관계
- 플랫폼 뼈대 구현 순서
- 기존 8개 지표의 모듈 방식 이전 순서
- 복수 데이터·동적 패널·작도·DSL·호가·시장분석의 단계
- 단계별 완료 기준
- 성능 게이트와 복원 체크포인트
- Renderer 수정이 허용되는 예외

## 승인 및 변경 절차

현재 상태는 `Architecture Freeze Candidate 1`이다.

사용자 승인 후 다음처럼 변경한다.

```text
Status: Architecture Freeze Candidate 1
→ Status: Architecture Baseline 1.0
```

승인 이후 다음 변경은 근거 없이 허용하지 않는다.

- Renderer가 개별 모듈을 직접 참조
- 모듈이 `SKCanvas` 또는 `SKPaint`를 직접 사용
- 기능별 전용 메뉴·전용 프로퍼티 Form을 App에 하드코딩
- `MainForm`에 기능별 On/Off 분기 추가
- `ChartFrameBuilder`에 기능 이름별 분기 추가
- `DefaultIndicatorFactory` 방식의 중앙 고정 등록 확대
- 특정 기능 하나만을 위한 Renderer API 추가

공통 Primitive나 Capability가 부족한 경우 먼저 두 개 이상의 기능에서 재사용 가능한 범용 계약인지 검토한다.

## 기능 구현 완료 정의

신규 모듈은 다음을 모두 만족해야 완료다.

```text
[ ] 독립 Module 클래스
[ ] 독립 설정 모델
[ ] Property Schema
[ ] Command Descriptor
[ ] 데이터 요구사항 선언
[ ] 표준 Contribution 생성
[ ] Registry 등록
[ ] On/Off 즉시 반영
[ ] Profile 저장·복원
[ ] 독립 자동검증
[ ] Renderer 기능명 참조 0
[ ] 비활성 상태 계산 0
[ ] 성능 기준 통과
[ ] 오류가 다른 모듈과 차트를 중단시키지 않음
```

## 현재 코드와의 관계

현재 C# 구조는 다음 기반을 이미 확보했다.

- Rendering은 Engine/DataSources를 참조하지 않음
- Engine과 Charting은 Contracts에 의존
- 지표 계산은 `IIncrementalIndicator`로 분리
- Renderer는 `SymbolSnapshot`과 `ChartFrame`을 받아 출력
- 고정 Paint/Path 재사용과 steady-state 무할당 검증

앞으로 다음 중앙 고정 구조를 모듈 플랫폼으로 교체한다.

- `DefaultIndicatorFactory`
- `SymbolRuntime`의 직접 지표 생성
- 고정 `PanelIndex`
- Line/Histogram 중심의 제한된 시각 출력 계약

기존 기능 결과와 성능을 유지하면서 점진적으로 이전하며, 한 번에 전면 교체하지 않는다.
