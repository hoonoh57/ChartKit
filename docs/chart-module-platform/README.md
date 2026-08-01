# ChartKit 범용 모듈 플랫폼 문서

상태: **Architecture Baseline 1.0**  
확정일: 2026-08-01  
기준 커밋: `7d4fa1c8268ebe11a59875a9d4a1750586b4a31c`  
대상 브랜치: `csharp/module-platform-baseline`

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

## Baseline 1.0 확정 기록

사용자 승인에 따라 `Architecture Freeze Candidate 1`을 `Architecture Baseline 1.0`으로 확정했다.

확정 범위:

```text
Module Registry와 범용 On/Off
ChartProfile과 상태 저장
Contribution → SceneCompiler → RenderPlan → Renderer
기능군 프로젝트 + 기능별 <Feature>Module.cs
모듈 파일 상단 <chart-module> 연결 계약
공용 Command/Property 메타데이터
Context Menu/Quick Button/Property Inspector 자동 연결
동적 PanelGraph·축·Interaction·HitTest
비활성 모듈 계산 0
Renderer/Module 참조 방향 제한
CI 기반 모듈 파일 계약 검사
```

세부 승인 기록은 다음 문서에 보존한다.

```text
docs/chart-module-platform/architecture-baseline-1.0.md
```

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

### 4. `module-file-standard.md` — 필수

모든 신규 `*Module.cs` 파일의 생성 및 연결 표준이다.

- 기능 단위와 프로젝트 단위 구분
- 파일명과 기능 폴더 규칙
- 파일 상단 `<chart-module>` 연결 계약
- ModuleDefinition
- Registry 등록
- 데이터 요구사항
- capability 인터페이스
- Contribution → SceneCompiler → RenderPlan → Renderer 연결
- 컨텍스트 메뉴·퀵버튼·Property Inspector 연결
- Profile·종목 상태 저장
- 독립 Verification
- CI 강제 규칙

신규 기능을 만들 때 다음 템플릿을 복사한다.

```text
docs/chart-module-platform/templates/ChartModule.template.cs
```

파일 상단 연결 계약을 생략하거나 임의 형식으로 작성하지 않는다. 다음 스크립트가 모든 기능군 프로젝트의 `*Module.cs`를 검사한다.

```text
scripts/verify_chart_module_headers.ps1
```

### 5. `module-registry-host-standard.md` — P1-B 구현 표준

다음을 실제 코드 계약으로 고정한다.

- `IChartModuleFactory<TModule>` 정적 팩토리
- `registry.Register<TModule>()` 단일 등록 방식
- `ModuleId` 중복·미등록 생성 차단
- `ChartModuleHost`의 Initialize·ApplyProfile·Activate·Deactivate·Reset 순서
- 범용 `SetModuleEnabledCommand`
- 동일 On/Off 상태의 lifecycle 중복 호출 차단
- 비활성 모듈 `BuildContributions` 호출 0
- Contribution 소유권과 Primitive 선언 검사
- 모듈별 fault 격리와 RuntimeSnapshot
- Profile 방어 복사

신규 기능 파일의 템플릿과 CI 검사는 이 Registry·Host 계약과 동일해야 한다.

### 6. `module-composition-probe-standard.md` — P1-C 구현 표준

다음을 첫 실제 수직 경로로 고정한다.

- `ChartModuleHost`에서 활성 Contribution 수집
- `ChartCompositionService`의 Host 계약 → Scene 계약 변환
- `SceneCompiler`를 통한 immutable `ChartRenderPlan` 생성
- 첫 표준 기능 파일 `PlatformProbeModule.cs`
- 비활성 모듈 RenderPlan primitive 0
- Profile panel·z-index·parameter 변경 반영
- 동일 입력의 결정적 RenderPlan
- `Modules.Platform`와 `Composition` 참조 경계
- Renderer와 App UI 미변경

### 7. `profile-persistence-standard.md` — P1-D 구현 표준

다음을 ChartProfile 저장·복원 계약으로 고정한다.

- `ChartProfile.CurrentSchemaVersion = 2`
- timeframe·layout·interaction·theme·modules 저장
- 동일 Profile의 결정적 JSON
- schemaVersion 1에서 2로 마이그레이션
- 미래 schemaVersion 명시적 거부
- 미등록 moduleId Profile 보존
- JsonObject와 module profile 방어 복사
- 중복 instanceId 및 잘못된 JSON 타입 거부
- UTF-8 BOM 없는 동일 디렉터리 임시 파일 교체
- Persistence의 Renderer·App·ModuleHost 참조 금지

### 8. `ui-metadata-property-standard.md` — P1-E 구현 표준

다음을 공용 UI 메타데이터와 Property 변경 계약으로 고정한다.

- CommandDescriptor·PropertyDescriptor의 Host 소유권 부여
- 같은 메타데이터에서 Context Menu·Quick Toolbar·Inspector 투영
- 모든 모듈 인스턴스의 범용 On/Off 명령 자동 생성
- ChartObjectIdentity 기반 Selection
- Property ValueKind·Storage·범위·허용값 선언
- `ChangeChartPropertyCommand` 단일 변경 경로
- 동일 값 no-op
- 선언된 `ChartChangeImpact`의 정확한 반환
- Profile 변경이 ModuleHost와 RenderPlan까지 전달
- UiModel의 WinForms·Renderer·DataSources 참조 금지

## 변경 절차

Baseline 1.0 이후 다음 변경은 근거 없이 허용하지 않는다.

- Renderer가 개별 모듈을 직접 참조
- 모듈이 `SKCanvas` 또는 `SKPaint`를 직접 사용
- 기능별 전용 메뉴·전용 프로퍼티 Form을 App에 하드코딩
- `MainForm`에 기능별 On/Off 분기 추가
- `ChartFrameBuilder`에 기능 이름별 분기 추가
- `DefaultIndicatorFactory` 방식의 중앙 고정 등록 확대
- 특정 기능 하나만을 위한 Renderer API 추가
- `<Feature>Module.cs` 상단 연결 계약 생략
- 플랫폼 연결 코드를 Calculator·Settings·MainForm 등에 분산

공통 Primitive나 Capability가 부족한 경우 먼저 두 개 이상의 기능에서 재사용 가능한 범용 계약인지 검토한다.

Baseline 변경이 필요한 경우 다음을 문서화한다.

```text
변경 사유
대안 검토
두 개 이상 기능에서의 재사용 근거
성능 영향
기존 Profile migration
Renderer 변경 여부
CI 보호 규칙 변경
```

## 기능 구현 완료 정의

신규 모듈은 다음을 모두 만족해야 완료다.

```text
[ ] Feature Capability Matrix ID
[ ] 독립 <Feature>Module.cs 진입 파일
[ ] 파일 상단 <chart-module> 연결 계약
[ ] ModuleDefinition과 헤더 값 일치
[ ] IChartModuleFactory<TModule> 구현
[ ] static Create(string instanceId)
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
[ ] verify_chart_module_headers.ps1 통과
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
