# OBV module parity standard

## Status

P2-6 introduces `indicator.obv` as an independent chart module while the
Legacy `ObvIndicator` remains active for side-by-side parity validation.
This phase does not remove the fixed indicator factory or renderer path.

## Legacy calculation contract

The module reproduces the current `ObvIndicator` contract exactly:

- the first OBV value equals the first candle volume;
- a higher close adds the current candle volume;
- a lower close subtracts the current candle volume;
- an equal close leaves OBV unchanged;
- the signal is a simple moving average of the latest OBV values;
- the signal remains `NaN` until the complete window is available;
- direction is `1` when OBV is strictly above the signal and `-1` otherwise;
- direction remains `NaN` while the signal is unavailable.

Default signal period: `20`.

## Incremental state

`ObvSeriesRuntime` owns all calculation state:

- previous close;
- previous-close availability;
- cumulative OBV;
- signal ring buffer, head, count and sum;
- state snapshot immediately before the mutable last candle;
- source sequence, close and volume arrays;
- immutable output point snapshot.

The runtime distinguishes:

1. unchanged snapshot;
2. same-sequence last-candle update;
3. sequence-plus-one append;
4. full rebuild for shifted or otherwise changed input.

Input order is preserved. The runtime does not sort or deduplicate candles.

## Module contract

Module ID: `indicator.obv`

Default placement: `indicator.5`

Data requirement: primary symbol OHLCV.

Contributions:

- `obv.value` — Polyline;
- `obv.signal` — Polyline.

Direction is retained in the calculation snapshot and parity verification but
is not rendered as a separate primitive.

## Properties

Parameters:

- `signalPeriod`, integer, range 1 to 10000, recalculates the module.

Styles:

- `obv.value.stroke`;
- `obv.signal.stroke`.

Style changes redraw the existing RenderPlan without recalculating OBV.

## Profile example

```json
{
  "moduleId": "indicator.obv",
  "instanceId": "indicator.obv.default",
  "moduleSchemaVersion": 1,
  "isEnabled": true,
  "zIndex": 0,
  "placement": "indicator.5",
  "parameters": {
    "signalPeriod": 20
  },
  "style": {
    "obv.value.stroke": "#7E57C2",
    "obv.signal.stroke": "#FFC107"
  },
  "persistentState": {}
}
```

## Verification gate

The exact-head Release validation must prove:

- full-series Legacy parity for OBV, Signal and Direction;
- same-candle update parity;
- append parity;
- rolling snapshot rebuild parity;
- disabled module calculation count remains zero;
- two Polyline contributions are emitted;
- both contributions use `indicator.5`;
- object-specific style values reach the RenderPlan;
- signal-period mutation recalculates values;
- module assembly reference boundary remains intact;
- App toggle, properties, panel placement and Profile round-trip pass;
- existing platform, rendering, market-data and realtime verification remains
  green.

## Desktop smoke test

With a clean Profile:

1. enable OBV;
2. verify `modules 1/7 plan 2 faults 0` when only OBV is active;
3. verify module OBV and Signal overlap Legacy OBV(MA20);
4. change Signal Period to 7 and verify the signal separates immediately;
5. restore 20 and verify overlap returns;
6. change each stroke and verify the corresponding line updates;
7. restart and verify enabled state, period and styles are restored.

## Non-goals

This phase does not:

- remove `ObvIndicator`;
- remove `DefaultIndicatorFactory`;
- modify `SymbolRuntime`;
- change renderer feature dispatch;
- change DataSources, market normalization, tick ordering or realtime logic.
