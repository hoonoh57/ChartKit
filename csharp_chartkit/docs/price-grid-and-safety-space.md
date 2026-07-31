# Korean price grid and chart safety space

## Price grid

The default stock profile follows the KRX equity quotation bands:

- below KRW 2,000: KRW 1
- KRW 2,000 to below KRW 5,000: KRW 5
- KRW 5,000 to below KRW 20,000: KRW 10
- KRW 20,000 to below KRW 50,000: KRW 50
- KRW 50,000 to below KRW 200,000: KRW 100
- KRW 200,000 to below KRW 500,000: KRW 500
- KRW 500,000 or higher: KRW 1,000

`KoreanEquityPriceGrid` is owned by `ChartKit.Charting`. It aligns price-axis ticks and main-panel crosshair values to prices that can be submitted as stock quotations. Renderers only draw the resolved frame.

## Safety space

The viewport owns horizontal and vertical navigation:

- default right-side future space: 12 bar slots
- additional future space: drag the chart to the left
- historical navigation: drag the chart to the right
- vertical price movement: start a drag in the main price panel and move up or down
- reset: Escape
- latest follow: End or double-click

The price frame includes visible candles and price-panel overlays, then adds default top and bottom margins before applying vertical movement. The renderer does not calculate market rules, ranges, margins, or input state.
