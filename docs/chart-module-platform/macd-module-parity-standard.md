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
- Panel: `indicator.4`
- Visuals: MACD Polyline, Signal Polyline, Histogram

## Module contract

- Module ID: `indicator.macd`
- Entry point: `MacdModule.cs`
- Runtime: `MacdSeriesRuntime.cs`
- Primary data: ordered OHLCV snapshot
- Default enabled: false
- Renderer dependency: none
- UI dependency: none

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

All contributions target the profile placement and use object-specific style keys.

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
