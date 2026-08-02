# ChartKit ChartProfile 영속화 표준

상태: P1-D 구현 기준  
기준 스키마: `ChartProfile.CurrentSchemaVersion = 2`

## 책임 경계

```text
ChartProfile
→ ChartProfileCodec
→ ChartProfileStore
```

- `ChartProfile`은 timeframe, layout, interaction, theme, module profile 목록을 보유한다.
- `ChartProfileCodec`은 결정적 JSON 직렬화, 역직렬화, 버전 판정과 마이그레이션만 담당한다.
- `ChartProfileStore`는 UTF-8 BOM 없는 파일 저장과 동일 디렉터리 임시 파일을 이용한 교체를 담당한다.
- `ChartKit.Persistence`는 Renderer, App, ModuleHost, Composition, Scene, DataSources를 참조하지 않는다.

## 스키마 버전

현재 저장 버전은 2다.

```json
{
  "schemaVersion": 2,
  "timeframe": "5m",
  "layout": {},
  "interaction": {},
  "theme": {},
  "modules": []
}
```

버전 처리 원칙:

```text
schemaVersion 누락 → 레거시 버전 1로 해석
버전 1 → 버전 2로 마이그레이션
버전 2 → 직접 로드
버전 3 이상 → NotSupportedException
버전 0 이하 → InvalidDataException
```

버전 1은 `layout`과 `interaction`이 없을 수 있다. 버전 2 마이그레이션에서 빈 JSON 객체를 보강한다. 미래 버전은 필드 의미를 추측하지 않는다.

## 모듈 보존

Persistence는 Module Registry를 참조하지 않는다. 따라서 현재 실행 파일에 등록되지 않은 `moduleId`도 다음 값을 그대로 보존한다.

```text
moduleId
instanceId
moduleSchemaVersion
isEnabled
zIndex
placement
parameters
style
persistentState
```

미등록 모듈을 삭제하거나 비활성화하거나 기본 모듈로 치환하지 않는다. 실제 활성화 가능 여부는 이후 ModuleHost 적용 단계에서 판단한다.

## 방어 복사

다음 객체는 생성 시 깊은 복사를 수행한다.

```text
layout
interaction
theme
modules[].parameters
modules[].style
modules[].persistentState
```

호출자가 원본 `JsonObject`를 변경하거나 Profile getter가 반환한 객체를 변경해도 내부 저장 결과가 바뀌지 않아야 한다.

## 검증 규칙

로드 또는 Profile 생성 시 다음을 거부한다.

```text
빈 timeframe
빈 moduleId
빈 instanceId
빈 placement
moduleSchemaVersion < 1
중복 instanceId
modules가 배열이 아닌 JSON
정수·불리언·객체 필드의 잘못된 JSON 타입
```

ModuleId는 등록 여부를 검증하지 않는다. InstanceId는 ChartProfile 전체에서 유일해야 한다.

## 결정적 저장

직렬화 속성 순서는 다음으로 고정한다.

```text
schemaVersion
timeframe
layout
interaction
theme
modules
```

모듈 속성 순서도 고정한다. 동일한 Profile을 반복 직렬화하거나 현재 스키마 JSON을 로드 후 다시 저장하면 같은 문자열이 생성되어야 한다.

## 파일 저장

`ChartProfileStore.SaveAsync`는 다음 순서를 사용한다.

```text
동일 디렉터리에 고유 .tmp 파일 작성
UTF-8 BOM 없이 파일 닫기
File.Move(temp, target, overwrite: true)
성공·실패와 관계없이 남은 temp 삭제
```

Profile JSON에는 현재 ChartFrame, pixel 좌표, SKPaint, SKPath, SKCanvas, GPU 자원, 임시 세션 override를 저장하지 않는다.

## 자동검증

`ProfilePersistenceVerification`은 다음을 검사한다.

```text
Release 어셈블리 구성
결정적 round-trip
호출자 및 getter 변경 차단
버전 1 → 2 마이그레이션
미등록 모듈 보존
미래 버전 거부
중복·빈 식별자·잘못된 JSON 검증
원자적 덮어쓰기와 temp 정리
UTF-8 BOM 부재
Persistence 참조 경계
```
