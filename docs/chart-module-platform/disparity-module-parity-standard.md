# Disparity Module Parity Standard

## Status

P2-7 migration standard for moving the existing C# Disparity indicator onto the generic ChartKit module platform while retaining the Legacy runtime in parallel.

## Scope

This stage adds:

- `DisparitySeriesRuntime`
- `DisparityModule`
- exact Legacy full/update/append/rebuild parity verification
- App registration and Profile round-trip verification
- generic RenderPlan contributions for the Disparity panel

This stage does not remove:

- `DisparityIndicator`
- `DefaultIndicatorFactory`
- `SymbolRuntime`
- the existing fixed indicator rendering path

It does not change DataSources, market ordering, Kiwoom history, realtime aggregation, or reconnect behavior.

## Legacy contract

Default parameters:

- Period: `20`
- Upper: `105`
- Baseline: `100`
- Lower: `95`
- Panel index: `6`

For each candle:

1. Add `Close` to a fixed-size SMA ring buffer.
2. Emit `MA = sum / Period` only when the window is full.
3. Emit `Value = Close / MA * 100` when `MA > 0`.
4. Emit `100` when a full-window `MA` is non-positive.
5. Emit `NaN` for `Value` and `MA` during warm-up.
6. Emit the Legacy reference values `105`, `100`, and `95`.

The module runtime preserves the source candle order. It does not sort or deduplicate input bars.

## Incremental contract

The runtime distinguishes:

- full calculation
- unchanged snapshot
- same-sequence last-candle update
- contiguous one-candle append
- rolling or incompatible snapshot rebuild

Before calculating the current last candle, it stores:

- ring head
- ring count
- rolling sum
- overwritten head value when the window is full

A same-sequence last-candle update restores that state before recalculating the candle.

## Module contract

Module ID:

```text
indicator.disparity
```

Default placement:

```text
indicator.6
```

Data requirement:

```text
PrimarySymbol.OHLCV
```

Only `Close` is consumed by the computation, but the shared primary-series contract remains OHLCV.

## Contributions

The module emits four generic Polyline contributions:

```text
disparity.value
disparity.upper
disparity.baseline
disparity.lower
```

The Legacy `MA` value is retained for calculation parity but is not rendered in the Disparity panel because it is expressed in price units while the panel is a percentage-ratio scale.

The Renderer remains unaware of Disparity and consumes only generic RenderPlan primitives.

## Properties

Calculation parameter:

```text
period
```

Changing `period` has `RecalculateModule` impact.

Visual reference parameters:

```text
upper
baseline
lower
```

They must satisfy:

```text
upper > baseline > lower
```

Changing a reference level has `RebuildVisuals` impact and must not recalculate the SMA runtime.

Object-specific style keys:

```text
disparity.value.stroke
disparity.upper.stroke
disparity.baseline.stroke
disparity.lower.stroke
```

Changing a style has `RedrawOnly` impact.

## Verification gates

The following must pass on the exact pull-request HEAD:

```text
csharp_disparity_module_release_configuration=PASS
csharp_disparity_module_definition=PASS
csharp_disparity_module_metadata=PASS
csharp_disparity_full_parity=PASS
csharp_disparity_update_parity=PASS
csharp_disparity_append_parity=PASS
csharp_disparity_rebuild_parity=PASS
csharp_disparity_disabled_zero=PASS
csharp_disparity_contributions=PASS
csharp_disparity_panel_contract=PASS
csharp_disparity_style_override=PASS
csharp_disparity_parameter_change=PASS
csharp_disparity_reference_boundary=PASS
csharp_disparity_module_contracts=PASS
```

App integration must additionally pass:

```text
csharp_app_disparity_module_data=PASS
csharp_app_disparity_module_parameters=PASS
csharp_app_disparity_module_style=PASS
csharp_app_disparity_panel_contract=PASS
csharp_app_disparity_module_roundtrip=PASS
```

Desktop smoke verification must confirm:

- default `20 / 105 / 100 / 95` output overlaps the Legacy Disparity output
- a changed Period separates only the computed Value line
- changed reference levels move without calculation faults
- each object-specific color is applied
- On/Off and restart restore work
- status reports four primitives and zero module faults

## Completion rule

The Legacy path remains in place until the exact-head Release build, complete EngineVerification, App self-test, Profile round-trip, and desktop Legacy-vs-module smoke test all pass.
