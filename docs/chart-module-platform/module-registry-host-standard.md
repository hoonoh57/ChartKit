# ChartKit Module Registry·Host 표준

상태: Architecture Baseline 1.0 구현 표준  
적용 단계: P1-B  

---

## 1. 고정 연결 경로

모든 기능 모듈은 다음 경로로만 생성·활성화·비활성화된다.

```text
<Feature>Module.cs
→ registry.Register<FeatureModule>()
→ ChartModuleRegistry.Create(moduleId, instanceId)
→ ChartModuleHost.UpsertProfile(ChartModuleProfile)
→ Initialize 1회
→ ApplyProfile
→ IsEnabled=true: Activate
→ IsEnabled=false: Deactivate
→ 활성 모듈만 BuildContributions
```

기능명에 따른 `switch`, `if RSI`, `if Strategy` 분기는 금지한다.

---

## 2. 모듈 정적 팩토리 계약

모든 `<Feature>Module.cs`는 다음 두 인터페이스를 구현한다.

```csharp
IChartModule
IChartModuleFactory<FeatureModule>
```

필수 정적 멤버:

```csharp
public static ChartModuleDefinition Definition { get; }
public static FeatureModule Create(string instanceId)
```

필수 인스턴스 멤버:

```csharp
public ChartModuleDefinition ModuleDefinition => Definition;
public string InstanceId { get; }
```

Registry는 생성된 객체가 요청한 `InstanceId`를 그대로 보유하고, 인스턴스의 `ModuleDefinition`이 등록된 정적 `Definition` 객체와 동일한지 검사한다.

---

## 3. Registry 계약

등록은 Composition Root에서 한 번 수행한다.

```csharp
var registry = new ChartModuleRegistry();
registry.Register<RsiModule>();
registry.Register<MacdModule>();
```

Registry 불변식:

```text
ModuleId 전역 유일
중복 등록 즉시 거부
미등록 ModuleId 생성 거부
파일명·클래스·정적 Definition·Registration 헤더 일치
기능별 switch 금지
```

---

## 4. ModuleHost 계약

`ChartModuleHost`가 모듈 인스턴스와 생명주기를 소유한다.

```text
InstanceId별 인스턴스 1개
Initialize는 생성 시 1회
Profile은 방어 복사 후 적용
동일 상태 On/Off는 no-op
Off 전환은 Deactivate
On 전환은 Activate
Remove는 Deactivate 후 Reset
```

동일 `InstanceId`의 `ModuleId`는 변경할 수 없다. 모듈 종류를 바꾸려면 기존 인스턴스를 제거하고 새 인스턴스를 생성한다.

현재 P1-B에서는 Profile schema version이 등록된 Module schema version과 정확히 일치해야 한다. 구버전 Profile 마이그레이션은 Persistence 단계에서 별도 구현한다.

---

## 5. 범용 On/Off

UI·메뉴·키보드·자동화는 기능별 메서드를 호출하지 않는다.

```csharp
moduleHost.Execute(
    new SetModuleEnabledCommand(instanceId, isEnabled));
```

또는:

```csharp
moduleHost.SetEnabled(instanceId, isEnabled);
```

상태 변경 순서:

```text
ChartModuleProfile.IsEnabled 변경
→ ApplyProfile
→ Activate 또는 Deactivate
→ 활성 모듈 Contribution 재수집
→ 후속 단계에서 SceneCompiler 재구성
→ 후속 단계에서 Profile 저장
```

`IsEnabled`가 이미 목표 상태이고 fault retry가 아니라면 lifecycle 메서드를 다시 호출하지 않는다.

---

## 6. 비활성 비용 0 원칙

비활성 모듈은 다음 작업을 수행하지 않는다.

```text
BuildContributions 호출
실시간 구독
지표 계산
렌더링 출력 생성
타이머·백그라운드 작업 유지
```

`ChartModuleHost.CollectVisualContributions`는 Enabled·Active·정상 상태인 `IChartVisualProvider`만 호출한다.

---

## 7. Contribution 소유권

모든 Contribution은 다음 식별자를 가져야 한다.

```text
ModuleId
InstanceId
ObjectId
```

Host는 다음을 검사한다.

```text
Contribution ModuleId = hosted ModuleId
Contribution InstanceId = hosted InstanceId
PrimitiveKind가 ModuleDefinition에 선언됨
동일 모듈 내 ObjectId 중복 없음
```

이후 `SceneCompiler`가 전체 모듈 간 중복과 결정적 정렬을 다시 검사한다.

---

## 8. Fault 격리

한 모듈이 lifecycle 또는 Contribution 생성 중 실패해도 다른 모듈 처리를 중단하지 않는다.

```text
실패 모듈: Faulted, Active=false, Contribution 제외
정상 모듈: 계속 Contribution 생성
RuntimeSnapshot: LastError 기록
```

Faulted 상태에서 동일 인스턴스를 다시 On으로 설정하면 Profile 재적용과 Activate를 재시도한다.

---

## 9. 파일 생성 필수 절차

```text
ChartModule.template.cs 복사
→ 파일명과 Module-Class 일치
→ <chart-module> 상단 계약 작성
→ IChartModuleFactory<T> 구현
→ static Definition 작성
→ static Create(string instanceId) 작성
→ Registry 등록
→ Profile/On-Off 검증
→ Contribution 소유권 검증
→ verify_chart_module_headers.ps1 통과
→ EngineVerification 통과
```

CI는 다음 누락을 실패 처리한다.

```text
Registration 헤더 불일치
IChartModuleFactory<T> 누락
static Definition 누락
static Create(string instanceId) 누락
InstanceId 누락
ApplyProfile 누락
Renderer/WinForms 직접 참조
```

---

## 10. P1-B 비포함 범위

```text
Profile JSON 저장·복원
Context Menu·Quick Button·Property Inspector
SceneCompiler 연결 어댑터
Renderer 전환
PlatformProbeModule
실제 SMA/RSI 등 지표 모듈
```

위 항목은 P1-B 계약 검증 후 다음 수직 단계에서 추가한다.
