# MACD Module Parity Standard

## Scope

P2-3 migrates the existing legacy `MacdIndicator` into the generic chart-module platform while retaining the legacy path for direct parity comparison.

## Legacy contract

- Fast EMA: 12
- Slow EMA: 26
- Signal EMA: 9
- EMA seed: simple average of the first full period
- MACD: fast EMA minus slow EMA
- Histogram: MACD minus signal
- Internal panel index: 7
- Module panel ID: `indicator.7`
- Visuals: MACD Polyline, Signal Polyline, Histogram

The visual order of the MACD pane is not its internal panel index. The legacy `MacdIndicator` descriptor uses panel index 7, so the module must target `indicator.7`.

## Module contract

- Module ID: `indicator.macd`
- Entry point: `MacdModule.cs`
- Runtime: `MacdSeriesRuntime.cs`
- Primary data: ordered OHLCV snapshot
- Default panel: `indicator.7`
- Default enabled: false
- Renderer dependency: none
- UI dependency: none

Profiles created by the first P2-3 draft used the incorrect placement `indicator.4`. `MacdModule.ApplyProfile` maps that exact legacy value to `indicator.7` so existing test profiles continue to render. New profiles use `indicator.7` directly.

## Incremental paths

The runtime distinguishes:

1. initial or incompatible snapshot: full calculation;
2. same sequence with only the final close changed: restore and update last;
3. exact next sequence appended: append;
4. rolling-window or interior change: full rebuild;
5. unchanged snapshot: no calculation.

Input order is preserved. The module never sorts or deduplicates bars.

## Contributions

- `macd.value`: Polyline
- `macd.signal`: Polyline
- `macd.histogram`: Histogram

All contributions target `indicator.7` after profile compatibility normalization and use object-specific style keys.

## Properties

- `fastPeriod`
- `slowPeriod`
- `signalPeriod`
- `macd.value.stroke`
- `macd.signal.stroke`
- `macd.histogram.stroke`

Period changes require module recalculation. Style changes require redraw only. The slow period must remain greater than the fast period.

## Verification gate

Required before merge:

- module header contract;
- Release build with no new C# warnings;
- legacy full/update/append/rebuild parity;
- disabled calculation count zero;
- three contribution types and styles;
- `indicator.7` panel contract;
- incorrect legacy `indicator.4` profile compatibility migration;
- parameter-change recalculation;
- App registration, property projection, rendering and profile round-trip;
- desktop overlay parity with legacy MACD;
- all existing engine, market-data and realtime verification markers.

## Explicit non-goals

This step does not remove or modify:

- `MacdIndicator`;
- `DefaultIndicatorFactory`;
- `SymbolRuntime`;
- existing fixed chart rendering;
- market-data ordering rules;
- realtime subscription or reconnect behavior.
