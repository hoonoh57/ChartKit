# ChartKit 개발 기능 Capability Matrix

상태: **Architecture Freeze Candidate 1**  
문서 버전: `1.0-rc1`  
작성일: 2026-08-01

이 문서는 ChartKit이 장기적으로 수용해야 할 기능을 분류하고, 각 기능이 공통 모듈 경로로 연결 가능한지 검증하기 위한 기준 목록이다.

상태 코드:

```text
E  Existing: 현재 기능 존재
M  Migration: 기존 기능을 모듈 플랫폼으로 이전 필요
P  Planned: 신규 구현 대상
R  Research: 설계 검증 또는 실데이터 검증 필요
```

복잡도 코드:

```text
L  낮음: 기존 Contribution과 Primitive로 처리
M  중간: 복수 데이터, PanelGraph, Interaction 필요
H  높음: 고빈도, DSL, 주문, 다중 상태, 별도 성능 검증 필요
```

---

# 1. 기본 가격·거래량·데이터 표현

| ID | 기능 | 상태 | 표준 모듈 | 데이터 요구 | 주요 출력 | 상호작용 | 위험 | 단계 |
|---|---|---:|---|---|---|---|---:|---:|
| PRICE-001 | 일반 캔들 | E/M | CorePriceModule | OHLCV | CandleBatch | 선택·십자선 | L | P2 |
| PRICE-002 | 종가 라인 | P | CorePriceModule | OHLCV | PolylineBatch | 선택 | L | P2 |
| PRICE-003 | OHLC 바 | P | CorePriceModule | OHLCV | LineBatch | 선택 | L | P2 |
| PRICE-004 | 거래량 | E/M | VolumeModule | OHLCV | HistogramBatch | 선택 | L | P2 |
| PRICE-005 | 거래대금 | P | TurnoverModule | OHLCV | HistogramBatch | 선택 | L | P3 |
| PRICE-006 | 틱봉 | E/M | CorePriceModule | Tick/Candle | CandleBatch | 선택 | M | P2 |
| PRICE-007 | 분봉 | E/M | CorePriceModule | Candle | CandleBatch | 선택 | L | P2 |
| PRICE-008 | 일·주·월봉 | E/M | CorePriceModule | Candle | CandleBatch | 선택 | L | P2 |
| PRICE-009 | Heikin-Ashi | P | SyntheticCandleModule | OHLCV | CandleBatch | 선택 | M | P6 |
| PRICE-010 | Renko | R | SyntheticCandleModule | Tick/Candle | CandleBatch | 선택 | H | P10 |
| PRICE-011 | Range Bar | R | SyntheticCandleModule | Tick | CandleBatch | 선택 | H | P10 |
| PRICE-012 | Point & Figure | R | SyntheticCandleModule | Price | Marker/Line | 선택 | H | P10 |
| PRICE-013 | 매물대 | P | VolumeProfileModule | Tick/Candle | HorizontalHistogram | 범위 선택 | H | P8 |
| PRICE-014 | 체결분포 | P | TradeDistributionModule | Tick | Histogram/HeatCell | 범위 선택 | H | P8 |
| PRICE-015 | 가격 제한폭 | P | PriceLimitModule | Instrument metadata | Line/FillArea | 없음 | L | P6 |
| PRICE-016 | VI 가격대 | P | VolatilityInterruptionModule | Market event | Line/Marker/Text | 없음 | M | P8 |
| PRICE-017 | 장 구분 배경 | P | SessionOverlayModule | Session state | FillArea/Text | 없음 | L | P6 |
| PRICE-018 | 수정주가 전후 표시 | R | CorporateActionModule | Corporate action | Marker/Line/Text | 선택 | M | P10 |
| PRICE-019 | 불완전 마지막 봉 표시 | P | IncompleteBarModule | Candle state | Candle style/Text | 없음 | L | P3 |
| PRICE-020 | 데이터 결손·Gap 표시 | P | DataQualityModule | Diagnostics | Marker/FillArea | 선택 | M | P3 |

검증 대표:

```text
일반 캔들 + 거래량 + 틱봉 + 데이터 Gap 표시
```

---

# 2. 기술적 지표

## 2.1 현재 이전 대상

| ID | 기능 | 상태 | 출력 | 패널 | 파라미터 영향 | 단계 |
|---|---|---:|---|---|---|---:|
| IND-001 | SMA/MA | E/M | Polyline | 가격/서브 | Recalculate | P2 |
| IND-002 | JMA | E/M | Polyline | 가격/서브 | Recalculate | P2 |
| IND-003 | RSI | E/M | Polyline/ReferenceLine | 서브 | Recalculate | P2 |
| IND-004 | OBV | E/M | Polyline | 서브 | Recalculate | P2 |
| IND-005 | Disparity | E/M | Polyline/ReferenceLine | 서브 | Recalculate | P2 |
| IND-006 | MACD | E/M | Polyline/Histogram | 서브 | Recalculate | P2 |
| IND-007 | SuperTrend | E/M | Conditional Polyline | 가격 | Recalculate | P2 |
| IND-008 | VWAP | E/M | Polyline | 가격 | Recalculate | P2 |

## 2.2 확장 지표군

| ID | 기능 | 상태 | 표준 출력 | 특수 요구 | 복잡도 | 단계 |
|---|---|---:|---|---|---:|---:|
| IND-009 | EMA/WMA/HMA | P | Polyline | 없음 | L | P3 |
| IND-010 | Bollinger Bands | P | Polyline/FillArea | 밴드 영역 | L | P3 |
| IND-011 | Keltner Channel | P | Polyline/FillArea | 밴드 영역 | L | P3 |
| IND-012 | Donchian Channel | P | Polyline/FillArea | 밴드 영역 | L | P3 |
| IND-013 | ATR | P | Polyline | 없음 | L | P3 |
| IND-014 | Stochastic | P | Polyline/ReferenceLine | 복수선 | L | P3 |
| IND-015 | CCI | P | Polyline/ReferenceLine | 없음 | L | P3 |
| IND-016 | Williams %R | P | Polyline/ReferenceLine | 역축 선택 | L | P3 |
| IND-017 | ROC/Momentum | P | Polyline/ReferenceLine | 없음 | L | P3 |
| IND-018 | DMI/ADX | P | Multi Polyline | DI+/DI-/ADX | M | P3 |
| IND-019 | TRIX | P | Polyline | 없음 | L | P3 |
| IND-020 | Chaikin Oscillator | P | Polyline | 없음 | L | P3 |
| IND-021 | MFI | P | Polyline/ReferenceLine | 거래량 입력 | L | P3 |
| IND-022 | CMF | P | Polyline/ReferenceLine | 거래량 입력 | L | P3 |
| IND-023 | A/D Line | P | Polyline | 거래량 입력 | L | P3 |
| IND-024 | Ichimoku | P | Multi Polyline/FillArea | 미래 shift | M | P4 |
| IND-025 | Parabolic SAR | P | MarkerBatch | 점 표시 | L | P4 |
| IND-026 | Pivot Points | P | Line/Text | 세션 기준 | M | P4 |
| IND-027 | Fractal | P | MarkerBatch | 신호 지연 | L | P4 |
| IND-028 | ZigZag | P | Polyline/Marker | repaint 상태 | M | P4 |
| IND-029 | Anchored VWAP | P | Polyline | 사용자 anchor | M | P5 |
| IND-030 | Volume Weighted MA | P | Polyline | 거래량 입력 | L | P3 |
| IND-031 | Tick Intensity | P | Polyline/Histogram | 실시간 tick | H | P8 |
| IND-032 | 체결강도 | P | Polyline/Histogram | FID/tick | H | P8 |
| IND-033 | 호가 불균형 | P | Polyline/HeatCell | Level2 | H | P8 |
| IND-034 | 상대강도 | P | Polyline | 비교종목 | M | P4 |
| IND-035 | 시장대비 상대수익률 | P | Polyline | 지수 데이터 | M | P4 |
| IND-036 | 멀티타임프레임 지표 | P | Any Series | 복수 timeframe | H | P7 |
| IND-037 | 지표 입력 지표 | P | Any Series | dependency graph | H | P7 |
| IND-038 | 사용자 Formula Indicator | P | Any Series | DSL | H | P7 |
| IND-039 | 조건색상 지표 | P | Conditional Series | style expression | M | P7 |
| IND-040 | 영역형 강세·약세 지표 | P | FillArea | boolean series | M | P7 |

원칙:

```text
지표 수식이 늘어나도 Renderer Primitive 종류는 증가하지 않는다.
대부분 Polyline/Histogram/FillArea/Marker/ReferenceLine 조합으로 처리한다.
```

---

# 3. 비교종목·시장지수·복수 데이터

| ID | 기능 | 상태 | 데이터 | 출력 | 축/패널 | 위험 | 단계 |
|---|---|---:|---|---|---|---:|---:|
| CMP-001 | KOSPI 지수 오버레이 | P | 지수 Candle | Polyline | 정규화/독립축 | M | P4 |
| CMP-002 | KOSDAQ 지수 오버레이 | P | 지수 Candle | Polyline | 정규화/독립축 | M | P4 |
| CMP-003 | 업종지수 오버레이 | P | 지수 Candle | Polyline | 정규화 | M | P4 |
| CMP-004 | 선물지수 오버레이 | P | 선물 Candle | Polyline | 독립축 | M | P4 |
| CMP-005 | 비교종목 정규화 오버레이 | P | 추가 symbol | Polyline | 기준 100 | M | P4 |
| CMP-006 | 비교종목 실제가격 오버레이 | P | 추가 symbol | Polyline/Candle | 복수축 | H | P4 |
| CMP-007 | 비교종목 독립 서브차트 | P | 추가 symbol | CandleBatch | 독립 panel | M | P4 |
| CMP-008 | 다수 종목 스택 패널 | P | N symbols | CandleBatch[] | PanelGraph | H | P4 |
| CMP-009 | 종목 간 스프레드 | P | 2 symbols | Polyline | 계산축 | M | P4 |
| CMP-010 | 종목 간 비율 | P | 2 symbols | Polyline | 계산축 | M | P4 |
| CMP-011 | 상관계수 rolling | P | N symbols | Polyline/HeatCell | panel | H | P9 |
| CMP-012 | Basket/포트폴리오 합성 | P | N symbols | Polyline | weighting | H | P9 |
| CMP-013 | 기준종목 동기화 시간축 | P | N symbols | 없음 | gap alignment | H | P4 |
| CMP-014 | 거래정지·결측 정렬 정책 | R | N symbols | Marker/Gap | alignment | H | P4 |

대표 수용성 테스트:

```text
현재 종목 캔들
+ KOSPI 정규화 오버레이
+ 삼성전자 독립 서브차트
+ 삼성전자 서브차트 위 SMA 오버레이
```

---

# 4. 패널·축·레이아웃

| ID | 기능 | 상태 | 표준 객체 | 저장 | 복잡도 | 단계 |
|---|---|---:|---|---:|---:|---:|
| LAYOUT-001 | 동적 PanelId | P | PanelGraph | 예 | H | P3 |
| LAYOUT-002 | 패널 추가·삭제 | P | PanelRequest | 예 | M | P3 |
| LAYOUT-003 | 패널 높이 splitter | P | PanelProfile | 예 | M | P3 |
| LAYOUT-004 | 프로퍼티 높이 편집 | P | Property Schema | 예 | L | P3 |
| LAYOUT-005 | 패널 순서 변경 | P | PanelGraph | 예 | M | P3 |
| LAYOUT-006 | 패널 접기·펼치기 | P | PanelProfile | 예 | L | P3 |
| LAYOUT-007 | 서브패널 내부 오버레이 | P | PanelId | 예 | M | P3 |
| LAYOUT-008 | 서브패널 아래 서브패널 | P | PanelGraph | 예 | M | P3 |
| LAYOUT-009 | 좌측 축 | P | AxisRequest | 예 | M | P3 |
| LAYOUT-010 | 우측 축 | E/M | AxisRequest | 예 | L | P3 |
| LAYOUT-011 | 복수 Y축 | P | AxisRequest[] | 예 | H | P4 |
| LAYOUT-012 | 공유 가격축 | P | ScaleGroup | 예 | M | P4 |
| LAYOUT-013 | 독립 가격축 | P | AxisRequest | 예 | M | P4 |
| LAYOUT-014 | 로그축 | P | ScaleRequest | 예 | M | P6 |
| LAYOUT-015 | 퍼센트축 | P | ScaleRequest | 예 | M | P4 |
| LAYOUT-016 | 정규화축 | P | ScaleRequest | 예 | M | P4 |
| LAYOUT-017 | 축 상하 이동 | E/M | ViewTransform | 세션/선택 | L | P3 |
| LAYOUT-018 | 축 범위 고정 | P | AxisProfile | 예 | L | P3 |
| LAYOUT-019 | 패널별 범례 | E/M | LegendContribution | 예 | L | P3 |
| LAYOUT-020 | 패널 최대화 | P | Layout command | 세션 | L | P3 |
| LAYOUT-021 | 패널 템플릿 | P | ChartTemplate | 예 | M | P5 |
| LAYOUT-022 | 다중 모니터 DPI 복원 | R | WorkspaceProfile | 예 | M | P10 |

---

# 5. 작도·마우스·상호작용

| ID | 기능 | 상태 | 출력 Primitive | Interaction | 상태 | 단계 |
|---|---|---:|---|---|---|---:|
| DRAW-001 | 십자선 | E/M | Line/Text | pointer move | session | P5 |
| DRAW-002 | 수평선 | P | Line/Text | create/drag | symbol | P5 |
| DRAW-003 | 수직선 | P | Line/Text | create/drag | symbol | P5 |
| DRAW-004 | 추세선 | P | Line/Text | handles | symbol | P5 |
| DRAW-005 | 평행선 | P | Multi Line | handles | symbol | P5 |
| DRAW-006 | 사각형 | P | Rectangle/Fill | handles | symbol | P5 |
| DRAW-007 | 원·타원 | P | Path/Polyline | handles | symbol | P6 |
| DRAW-008 | 텍스트·메모 | P | Text | edit/drag | symbol | P5 |
| DRAW-009 | 가격 라벨 | P | Text/Line | drag | symbol | P5 |
| DRAW-010 | 피보나치 되돌림 | P | Line/Text/Fill | handles | symbol | P5 |
| DRAW-011 | 피보나치 확장 | P | Line/Text | handles | symbol | P6 |
| DRAW-012 | 팬·각도선 | P | Line/Text | handles | symbol | P6 |
| DRAW-013 | 구간 수익률 측정 | P | Line/Text/Fill | drag range | temporary | P5 |
| DRAW-014 | 봉 수·기간 측정 | P | Line/Text | drag range | temporary | P5 |
| DRAW-015 | 전고점·전저점 자동선 | P | Line/Marker/Text | select | profile | P6 |
| DRAW-016 | Snap to candle | P | Interaction | pointer | user | P5 |
| DRAW-017 | Snap to indicator | P | Interaction | pointer | user | P6 |
| DRAW-018 | 객체 선택 | P | HitRegion | click | session | P5 |
| DRAW-019 | 객체 이동·핸들 | P | HitRegion/Handle | drag | symbol | P5 |
| DRAW-020 | 복사·삭제 | P | Command | keyboard/menu | symbol | P5 |
| DRAW-021 | Undo/Redo | P | Command history | keyboard | session | P5 |
| DRAW-022 | 객체 잠금 | P | Property | click | symbol | P5 |
| DRAW-023 | 객체 숨김 | P | Property | click | symbol | P5 |
| DRAW-024 | 작도 그룹 | P | Module state | multi-select | symbol | P6 |

대표 수용성 테스트:

```text
수평선 → 추세선 → 피보나치
선택·이동·삭제·Undo
종목별 저장·복원
Renderer는 Line/Text/FillArea만 처리
```

---

# 6. 전략·신호·주문·포지션

| ID | 기능 | 상태 | 계산 | 출력 | 상태 | 위험 | 단계 |
|---|---|---:|---|---|---|---:|---:|
| STR-001 | 매수 진입 신호 | P | Boolean series | Marker/Text | profile | M | P7 |
| STR-002 | 매수 청산 신호 | P | Boolean series | Marker/Text | profile | M | P7 |
| STR-003 | 매도 진입 신호 | P | Boolean series | Marker/Text | profile | M | P7 |
| STR-004 | 매도 청산 신호 | P | Boolean series | Marker/Text | profile | M | P7 |
| STR-005 | 조건 충족 구간 | P | Boolean series | FillArea | profile | M | P7 |
| STR-006 | 진입가 | P | Position state | Line/Text | runtime | M | P9 |
| STR-007 | 손절가 | P | Strategy state | Line/Text | runtime/profile | M | P9 |
| STR-008 | 목표가 | P | Strategy state | Line/Text | runtime/profile | M | P9 |
| STR-009 | 트레일링 스톱 | P | Strategy state | Line/Marker | runtime | H | P9 |
| STR-010 | 포지션 영역 | P | Position state | FillArea/Text | runtime | M | P9 |
| STR-011 | 평균단가 | P | Position state | Line/Text | runtime | M | P9 |
| STR-012 | 실현손익 | P | Trade state | Text/Badge | runtime | M | P9 |
| STR-013 | 미실현손익 | P | Position state | Text/Fill | runtime | H | P9 |
| STR-014 | 주문대기 | P | Order state | Line/Marker | runtime | H | P9 |
| STR-015 | 부분체결 | P | Fill state | Marker/Text | runtime | H | P9 |
| STR-016 | 주문취소 | P | Order state | Marker | runtime | M | P9 |
| STR-017 | 전략 상태 머신 | P | Strategy runtime | Status/Text | runtime | H | P7 |
| STR-018 | 다중 전략 동시 표시 | P | N runtimes | Marker/Line | profile | H | P9 |
| STR-019 | 전략별 Z-order | P | scene | primitive order | profile | M | P7 |
| STR-020 | 전략 신호 숨김·필터 | P | visual filter | none | profile | L | P7 |
| STR-021 | 백테스트 거래경로 | P | backtest result | Marker/TradePath | file/profile | H | P9 |
| STR-022 | 실시간·백테스트 비교 | R | dual result | Marker/Panel | profile | H | P10 |

---

# 7. 호가·체결·고빈도 정보

| ID | 기능 | 상태 | 데이터 | 출력 | 갱신 | 위험 | 단계 |
|---|---|---:|---|---|---|---:|---:|
| HF-001 | 10단계 호가 | P | Level2 | DepthBar/Text | high | H | P8 |
| HF-002 | 매수·매도 잔량 | P | Level2 | DepthBar | high | H | P8 |
| HF-003 | 호가 불균형 | P | Level2 | HeatCell/Polyline | high | H | P8 |
| HF-004 | 호가 변화 pulse | P | Level2 delta | Marker/HeatCell | high | H | P8 |
| HF-005 | 체결 방향 | P | Tick | Marker/Histogram | high | H | P8 |
| HF-006 | 체결강도 | P | Tick/FID | Polyline | high | H | P8 |
| HF-007 | 대량 체결 | P | Tick | PulseMarker/Text | high | H | P8 |
| HF-008 | 누적 체결량 | P | Tick | Polyline/Histogram | high | H | P8 |
| HF-009 | 예상체결가 | P | Auction data | Line/Text | high | H | P8 |
| HF-010 | 예상체결량 | P | Auction data | Histogram/Text | high | H | P8 |
| HF-011 | 호가 히트맵 | P | Level2 history | HeatCell | very high | H | P8 |
| HF-012 | 매물대와 호가 결합 | P | Level2 + candle | HeatCell/Histogram | high | H | P8 |
| HF-013 | VI/상한·하한 정보 | P | Market state | Line/Text | medium | M | P8 |
| HF-014 | Tick tape side panel | P | Tick | SidePanel model | very high | H | P8 |

성능 게이트:

```text
호가 갱신이 캔들·지표 Scene 전체를 재생성하지 않는다.
별도 high-frequency overlay layer만 교체한다.
```

---

# 8. 시장분석·매매대상 후보

| ID | 기능 | 상태 | 입력 | 출력 채널 | 상태 | 단계 |
|---|---|---:|---|---|---|---:|
| MKT-001 | 시장 강세·약세 | P | index/breadth | FillArea/Status | profile | P9 |
| MKT-002 | 업종 강도 | P | multi-symbol | SidePanel/HeatCell | runtime | P9 |
| MKT-003 | 테마 강도 | P | multi-symbol | SidePanel/HeatCell | runtime | P9 |
| MKT-004 | 상승·하락 종목수 | P | market breadth | Panel/Status | runtime | P9 |
| MKT-005 | 거래대금 순위 | P | ranking | SidePanel/Badge | runtime | P9 |
| MKT-006 | 체결강도 순위 | P | ranking | SidePanel/Badge | runtime | P9 |
| MKT-007 | 조건검색 편입 | P | condition event | Marker/Badge | runtime | P9 |
| MKT-008 | 조건검색 이탈 | P | condition event | Marker/Badge | runtime | P9 |
| MKT-009 | 매매대상 후보 등급 | P | scoring | Badge/Background | runtime | P9 |
| MKT-010 | 종목 실시간 점수 | P | multi-factor | Text/SidePanel | runtime | P9 |
| MKT-011 | 매수 우선순위 | P | ranking | Badge/SidePanel | runtime | P9 |
| MKT-012 | 대장주 후보 | P | ranking | Badge/Marker | runtime | P9 |
| MKT-013 | 급등 후보 | P | detector | Marker/SidePanel | runtime | P9 |
| MKT-014 | 관심종목 상태 | P | watchlist | Badge/SidePanel | persistent | P9 |
| MKT-015 | 후보 편입 시점 | P | event | Marker/VerticalLine | persistent | P9 |
| MKT-016 | 시장 레짐 | P | model | FillArea/Status | profile | P9 |
| MKT-017 | ML 점수 | R | Python/model | Polyline/Badge | runtime | P10 |
| MKT-018 | 모델 confidence | R | Python/model | FillArea/Text | runtime | P10 |

하나의 Market Analysis Module이 차트 Contribution과 SidePanel 모델을 동시에 제공할 수 있어야 한다.

---

# 9. DSL·수식관리

| ID | 기능 | 상태 | 결과 타입 | 출력 | 위험 | 단계 |
|---|---|---:|---|---|---:|---:|
| DSL-001 | Lexer | P | tokens | 없음 | M | P7 |
| DSL-002 | Parser | P | AST | 없음 | M | P7 |
| DSL-003 | Type checker | P | typed AST | 오류표시 | H | P7 |
| DSL-004 | Dependency resolver | P | graph | 없음 | H | P7 |
| DSL-005 | Execution plan | P | runtime plan | 없음 | H | P7 |
| DSL-006 | 지표 수식 | P | NumericSeries | Polyline/Histogram | H | P7 |
| DSL-007 | 신호 수식 | P | BooleanSeries | Marker/Fill | H | P7 |
| DSL-008 | 진입 수식 | P | BooleanSeries | Marker | H | P7 |
| DSL-009 | 청산 수식 | P | BooleanSeries | Marker | H | P7 |
| DSL-010 | 색상 수식 | P | CategorySeries | conditional style | H | P7 |
| DSL-011 | 강세·약세 영역 | P | BooleanSeries | FillArea | H | P7 |
| DSL-012 | 종목검색 수식 | P | Boolean/Score | SidePanel | H | P9 |
| DSL-013 | 경보 수식 | P | Event | Notification | H | P9 |
| DSL-014 | 함수 라이브러리 | P | callable catalog | editor | H | P7 |
| DSL-015 | 사용자 변수 | P | parameters | Property Inspector | M | P7 |
| DSL-016 | 다른 ID 지표 참조 | P | dependency | any | H | P7 |
| DSL-017 | 멀티타임프레임 참조 | P | dependency | any | H | P7 |
| DSL-018 | 다른 종목 참조 | P | dependency | any | H | P7 |
| DSL-019 | 수식 검증 | P | diagnostics | editor | M | P7 |
| DSL-020 | 수식 버전·복제 | P | persistence | manager UI | M | P7 |
| DSL-021 | 수식 import/export | P | file | manager UI | M | P9 |

DSL도 최종적으로 Module Instance를 생성한다.

```text
DSL AST
→ Execution Runtime
→ Numeric/Boolean/Category Series
→ 표준 Contribution
→ Renderer
```

---

# 10. UI·명령·프로퍼티·프로필

| ID | 기능 | 상태 | 표준 서비스 | 단계 |
|---|---|---:|---|---:|
| UI-001 | Module Registry 검색 | P | ModuleCatalog | P1 |
| UI-002 | 범용 On/Off | P | ModuleHost | P1 |
| UI-003 | 컨텍스트 메뉴 자동 생성 | P | Command Adapter | P1 |
| UI-004 | 퀵버튼 자동 생성 | P | Command Adapter | P1 |
| UI-005 | 퀵버튼 고정·해제 | P | UserProfile | P1 |
| UI-006 | 기능 검색창 | P | ModuleCatalog | P3 |
| UI-007 | 공용 Property Inspector | P | Property Adapter | P1 |
| UI-008 | 타입별 공용 editor | P | Editor Registry | P1 |
| UI-009 | 선택 객체 동기화 | P | SelectionService | P3 |
| UI-010 | 기본값 복원 | P | ProfileService | P1 |
| UI-011 | 현재값을 기본값 저장 | P | ProfileService | P3 |
| UI-012 | 모듈 복제 | P | ModuleHost | P3 |
| UI-013 | 모듈 삭제 | P | ModuleHost | P1 |
| UI-014 | 적용 범위 선택 | P | ProfileScope | P3 |
| UI-015 | Undo/Redo | P | CommandHistory | P5 |
| UI-016 | Inspector 자동숨김 | P | WorkspaceProfile | P3 |
| UI-017 | Inspector 너비 저장 | P | WorkspaceProfile | P3 |
| UI-018 | 모듈 상태·오류 표시 | P | Diagnostics | P1 |
| UI-019 | 상태줄 Contribution | P | StatusService | P3 |
| UI-020 | 모듈 관리 화면 | P | ModuleCatalog | P3 |

---

# 11. 작업공간·저장·복원

| ID | 기능 | 상태 | 저장 범위 | 단계 |
|---|---|---:|---|---:|
| PERSIST-001 | System default | P | application | P1 |
| PERSIST-002 | User default | P | user | P1 |
| PERSIST-003 | Workspace profile | P | workspace | P3 |
| PERSIST-004 | Chart template | P | reusable chart | P1 |
| PERSIST-005 | Symbol override | P | symbol | P5 |
| PERSIST-006 | Session override | P | runtime | P1 |
| PERSIST-007 | Module schema version | P | module profile | P1 |
| PERSIST-008 | Profile migration | P | versions | P3 |
| PERSIST-009 | 작도 상태 | P | symbol | P5 |
| PERSIST-010 | 패널 상태 | P | template/workspace | P3 |
| PERSIST-011 | Viewport 복원 | P | chart instance | P5 |
| PERSIST-012 | 충돌 복구 | P | backup | P10 |
| PERSIST-013 | 자동 저장 debounce | P | profile | P1 |
| PERSIST-014 | import/export | P | file | P9 |

---

# 12. 다중 차트·동기화

| ID | 기능 | 상태 | 요구 | 위험 | 단계 |
|---|---|---:|---|---:|---:|
| MULTI-001 | 종목 탭 | P | shared host | M | P9 |
| MULTI-002 | 2분할 | P | layout | M | P9 |
| MULTI-003 | 4분할 | P | layout | M | P9 |
| MULTI-004 | 6·9분할 | P | layout | H | P9 |
| MULTI-005 | 시간축 동기화 | P | sync group | H | P9 |
| MULTI-006 | 십자선 동기화 | P | interaction sync | M | P9 |
| MULTI-007 | 같은 종목 다중 주기 | P | multi-timeframe | H | P9 |
| MULTI-008 | 다른 종목 동일 주기 | P | multi-symbol | H | P9 |
| MULTI-009 | 보이지 않는 차트 렌더 중지 | P | visibility scheduler | M | P9 |
| MULTI-010 | 중앙 데이터 상태 공유 | P | engine sharing | H | P9 |
| MULTI-011 | 창 분리·도킹 | R | workspace | H | P10 |

---

# 13. Range Navigator·대용량 데이터

| ID | 기능 | 상태 | 요구 | 위험 | 단계 |
|---|---|---:|---|---:|---:|
| NAV-001 | 하단 미니맵 | P | overview series | M | P6 |
| NAV-002 | 현재 구간 선택창 | P | interaction | M | P6 |
| NAV-003 | 선택창 이동 | P | interaction | M | P6 |
| NAV-004 | 좌우 핸들 줌 | P | interaction | M | P6 |
| NAV-005 | 왼쪽 끝 지연 로딩 | P | history paging | H | P6 |
| NAV-006 | 로딩 후 위치 유지 | P | index anchor | H | P6 |
| NAV-007 | 수십만 봉 viewport 렌더 | P | virtualization | H | P6 |
| NAV-008 | 데이터 압축 level | R | LOD | H | P10 |

---

# 14. 진단·성능·내보내기

| ID | 기능 | 상태 | 출력 | 단계 |
|---|---|---:|---|---:|
| OPS-001 | 모듈 계산시간 | P | Diagnostics | P1 |
| OPS-002 | Contribution 개수 | P | Diagnostics | P1 |
| OPS-003 | 캐시 hit/miss | P | Diagnostics | P3 |
| OPS-004 | 프레임 시간 | E/M | Diagnostics | P1 |
| OPS-005 | 관리 힙 할당 | E/M | Verification | P1 |
| OPS-006 | 큐 깊이 | E/M | Status/Log | P1 |
| OPS-007 | 데이터 지연 | E/M | Status/Log | P1 |
| OPS-008 | 모듈 오류 격리 | P | Status | P1 |
| OPS-009 | 1종목 6시간 soak | P | Report | P0 |
| OPS-010 | 20종목 6시간 soak | P | Report | P0/P10 |
| OPS-011 | 100종목 replay soak | P | Report | P10 |
| OPS-012 | 종목·주기 100회 변경 | P | Report | P0 |
| OPS-013 | 활성 모듈 10/50/100 | P | Benchmark | P10 |
| OPS-014 | 패널 5/10/20 | P | Benchmark | P10 |
| OPS-015 | 마커 100/1K/10K | P | Benchmark | P10 |
| OPS-016 | screenshot export | P | Image | P10 |
| OPS-017 | CSV export | P | File | P10 |
| OPS-018 | 설정 진단 | P | Report | P10 |
| OPS-019 | crash dump | P | File | P10 |
| OPS-020 | 자동 복구 | P | Persistence | P10 |

---

# 15. 대표 아키텍처 검증 세트

모든 기능을 먼저 개발하지 않는다. 서로 다른 요구를 가진 대표 기능으로 플랫폼 수용성을 검증한다.

## 세트 A — 기존 기능 모듈화

```text
SMA: 가격 오버레이
RSI: 독립 패널 + 기준선
MACD: 복수선 + 히스토그램
SuperTrend: 조건색상선
```

검증:

```text
Registry
On/Off
Property Inspector
Profile 저장
Renderer 수정 없음
기존 결과 parity
```

## 세트 B — 복수 데이터와 패널

```text
KOSPI 정규화 오버레이
삼성전자 독립 서브차트
삼성전자 패널 위 SMA
```

검증:

```text
복수 데이터 요구
시간 정렬
PanelGraph
공유·독립 축
서브차트 오버레이
```

## 세트 C — Interaction

```text
수평선
추세선
피보나치
```

검증:

```text
HitTest
선택·이동
Undo/Redo
종목별 저장
```

## 세트 D — DSL

```text
CrossUp(avg(c, MA1), avg(c, MA2))
RSI 조건 신호
복합 진입·청산
```

검증:

```text
DSL → Runtime Module
Property 변수
Marker/Fill Contribution
```

## 세트 E — 고빈도

```text
10단계 호가
체결강도
대량체결 마커
```

검증:

```text
부분 invalidation
고빈도 layer
Renderer thread 비차단
```

## 세트 F — 시장분석

```text
조건검색 편입
후보 종목 점수
매수 우선순위 SidePanel
차트 Badge/Marker
```

검증:

```text
한 모듈의 차트+외부 UI 동시 출력
다종목 상태 공유
```

이 여섯 세트가 동일한 Module Registry → Contribution → Scene → RenderPlan 경로를 통과하면 범용 구조가 검증된 것으로 본다.
