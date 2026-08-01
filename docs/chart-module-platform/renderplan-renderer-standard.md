# RenderPlan → Skia Renderer 표준

상태: **P1-G 구현 후보**  
기준 브랜치: `csharp/module-platform-p1-renderplan-renderer`

## 목적

P1-G는 모듈 플랫폼의 마지막 첫 수직 경로를 실제 화면까지 연결한다.

```text
ChartModuleProfile.Style
→ ChartModuleHost RuntimeSnapshot
→ ChartCompositionService
→ SceneCompiler
→ immutable ChartRenderPlan
→ SkiaChartRenderPlanRenderer
→ ChartKit.App chart surface
```

Renderer는 `platform.probe`, RSI, 전략, 작도도구 등의 기능 이름을 알지 않는다. 입력은 Scene의 Renderer 전용 Primitive 계약뿐이다.

## 참조 경계

```text
ChartKit.Modules.*
→ ChartKit.Modules.Abstractions

ChartKit.Composition
→ ModuleHost + Scene

ChartKit.Rendering
→ Charting + Contracts + Scene + SkiaSharp
```

`ChartKit.Rendering`에서 금지하는 참조:

```text
ChartKit.Modules.Abstractions
ChartKit.Modules.Platform
ChartKit.ModuleHost
ChartKit.Composition
ChartKit.DataSources
ChartKit.App
System.Windows.Forms
```

Scene은 모듈 Contribution의 업무 계약을 Renderer 전용 값으로 변환한다.

```text
ChartPrimitiveKind → RenderPrimitiveKind
ChartSeriesPoint   → RenderPoint
ChartProfile.Style → RenderPrimitiveStyle
```

## 좌표 계약

`RenderPoint.X`는 현재 SymbolSnapshot의 절대 candle index다.

```text
visibleIndex = RenderPoint.X - ChartWindow.StartIndex
pixelX       = ChartFrame.X(visibleIndex)
```

현재 표준 PanelId:

```text
price.main      가격 패널 / PriceY
volume.main     거래량 패널 / 0..VolumeMaximum
panel.1..7      보조 패널 / PanelY
indicator.1..7  panel.1..7의 호환 별칭
```

알 수 없거나 현재 Layout에서 보이지 않는 PanelId는 전체 렌더링을 중단하지 않고 해당 Primitive만 건너뛴다.

## Style 계약

모듈 Profile의 `Style`은 SceneCompiler가 다음 Renderer 값으로 정규화한다.

```json
{
  "stroke": "accent 또는 #RRGGBB 또는 #AARRGGBB",
  "fill": "선택값",
  "strokeWidth": 1.5,
  "opacity": 1.0
}
```

기본값:

```text
stroke      accent
fill        없음
strokeWidth 1.5
opacity     1.0
```

지원하는 symbolic color:

```text
accent
positive
negative
neutral
warning
white
black
```

잘못된 숫자 범위는 SceneCompiler에서 거부한다. 알 수 없는 색상 문자열은 Renderer에서 accent로 안전하게 대체한다.

## Primitive dispatcher

점 기반 현재 계약으로 안전하게 표현 가능한 Primitive:

```text
Polyline
Line
Marker
Rectangle
Histogram
FillArea
```

다음 Primitive는 추가 payload 계약이 정의될 때까지 격리해 건너뛴다.

```text
Candle
HorizontalHistogram
Text
HeatCell
Image
```

지원되지 않는 Primitive 때문에 다른 모듈이나 가격 차트 렌더링을 중단하지 않는다. Renderer는 `RenderedPrimitives`, `SkippedPrimitives`, `RenderedPoints` 진단을 반환한다.

## 렌더 순서

App chart surface의 순서는 다음과 같다.

```text
기존 가격·거래량·고정 지표 Renderer
→ Module ChartRenderPlan Renderer
→ Legend
→ Crosshair
```

따라서 모듈 Primitive는 가격 차트 위에 표시되고 Legend와 Crosshair는 계속 최상단에 유지된다.

SceneCompiler의 결정적 정렬 순서가 모듈 Primitive의 그리기 순서다.

```text
PanelId
→ ZIndex
→ ModuleId
→ InstanceId
→ ObjectId
```

## 성능 계약

`SkiaChartRenderPlanRenderer`는 다음 리소스를 인스턴스 생성 시 한 번만 만든다.

```text
SKPaint stroke
SKPaint fill
SKPath
```

프레임마다 새 Paint·Path·배열을 만들지 않는다. RenderPlan은 불변 객체를 교체하는 방식으로 전달한다.

검증 기준:

```text
200회 steady-state 렌더 managed allocation ≤ 32,768 bytes
panel clip 밖 style pixel 0
알 수 없는 panel 격리
지원되지 않는 primitive 격리
Rendering 참조 경계 유지
```

## 완료 기준

```text
[ ] Release 빌드 오류 0, 신규 C# 경고 0
[ ] module header contract PASS
[ ] PlatformProbe Polyline 실제 pixel 출력
[ ] Profile stroke style 실제 pixel 반영
[ ] Panel clip PASS
[ ] unknown panel skip PASS
[ ] unsupported primitive skip PASS
[ ] steady-state allocation PASS
[ ] Rendering → Scene 참조 확인
[ ] Rendering의 모듈·App·DataSources 참조 0
[ ] 기존 RenderingVerification PASS
[ ] 기존 EngineVerification 전체 PASS
[ ] App self-test PASS
[ ] Replay desktop에서 On/Off 즉시 표시·삭제
[ ] 창 종료·재실행 후 On/Off·색상 복원
```

## 이번 단계에서 제외

```text
기존 8개 지표의 모듈 이전
Text/Image/HeatCell 전용 payload
동적 PanelGraph 생성
HitTest와 Interaction Router
시장 데이터·실시간 변경
물리 WebSocket 재연결 검증
```
