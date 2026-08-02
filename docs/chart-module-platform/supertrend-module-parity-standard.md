# SuperTrend Module Parity Standard

## Scope

P2-4 migrates the existing legacy `SuperTrendIndicator` into the generic chart-module platform while retaining the legacy path for direct parity comparison.

## Legacy contract

- ATR period: 10
- multiplier: 3.0
- true range: maximum of high-low, high-previous close, and low-previous close distances
- ATR seed: simple average of the first full period
- subsequent ATR: Wilder smoothing
- initial direction: up
- output values: Value, Up, Down, Direction, ATR
- panel: price panel (`price.main` in the module platform)

The module runtime reproduces the legacy final-upper/final-lower and direction transition rules without sorting or deduplicating bars.

## Module contract

- Module ID: `indicator.supertrend`
- Entry point: `SuperTrendModule.cs`
- Runtime: `SuperTrendSeriesRuntime.cs`
- primary data: ordered OHLCV snapshot
- default panel: `price.main`
- default enabled: false
- renderer dependency: none
- UI dependency: none

## Incremental paths

The runtime distinguishes:

1. initial or incompatible snapshot: full calculation;
2. same sequence with final high, low, or close changed: restore and update last;
3. exact next sequence appended: append;
4. rolling-window or interior OHLC change: full rebuild;
5. unchanged snapshot: no calculation.

## Contributions

The runtime retains all five legacy outputs for parity. The visual module emits the two non-duplicated trend segments:

- `supertrend.up`: Polyline;
- `supertrend.down`: Polyline.

Both contributions target `price.main`. The duplicated legacy `Value` output is retained in calculation state but is not emitted as a third line because it equals either Up or Down on every valid bar.

## Properties

- `period`
- `multiplier`
- `supertrend.up.stroke`
- `supertrend.down.stroke`

Period and multiplier changes require recalculation. Style changes require redraw only.

## Verification gate

Required before merge:

- module header contract;
- Release build with no new C# warnings;
- legacy full/update/append/rebuild parity for Value, Up, Down, Direction, and ATR;
- disabled calculation count zero;
- two price-panel contributions with object-specific styles;
- parameter-change recalculation;
- App registration, property projection, price-panel rendering, and profile round-trip;
- desktop overlay parity with the legacy SuperTrend;
- all existing engine, renderer, market-data, and realtime verification markers.

## Explicit non-goals

This step does not remove or modify:

- `SuperTrendIndicator`;
- `DefaultIndicatorFactory`;
- `SymbolRuntime`;
- existing fixed chart rendering;
- market-data ordering rules;
- realtime subscription or reconnect behavior.
