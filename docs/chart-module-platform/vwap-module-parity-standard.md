# VWAP Module Legacy Parity Standard

## Scope

`indicator.vwap` migrates the Legacy `VwapIndicator` calculation into the module platform without adding renderer-specific behavior.

Standard path:

```text
ChartPrimaryBar
→ VwapSeriesRuntime
→ VwapModule
→ ChartContribution
→ SceneCompiler
→ ChartRenderPlan
→ generic Skia renderer
```

## Primary data contract

VWAP is session cumulative and therefore requires an explicit provider-neutral trading-session boundary.

```csharp
DateOnly TradingDate
```

`ChartPrimaryBar` retains a compatibility constructor for earlier non-session indicators, but VWAP rejects `DateOnly.MinValue`. The production App path must populate `TradingDate` from `Candle.TradingDate`.

The following are prohibited:

```text
inferring a session boundary from Sequence
assuming the visible snapshot contains one trading day
resetting on every first visible bar
sorting or deduplicating market data to manufacture a boundary
running only a single-day parity fixture
```

## Legacy calculation contract

For each bar:

```text
typicalPrice = (High + Low + Close) / 3
priceVolume += typicalPrice × Volume
volume += Volume
priceSquaredVolume += typicalPrice² × Volume

VWAP = priceVolume / volume
variance = max(0, priceSquaredVolume / volume - VWAP²)
deviation = sqrt(variance)

Upper1 = VWAP + StdDev1 × deviation
Lower1 = VWAP - StdDev1 × deviation
Upper2 = VWAP + StdDev2 × deviation
Lower2 = VWAP - StdDev2 × deviation
```

At a `TradingDate` change, the three cumulative values are reset before consuming the first bar of the new session.

If cumulative volume is zero or negative, all five values are `NaN`.

## Incremental runtime contract

`VwapSeriesRuntime` supports:

```text
Full calculation
UpdateLast
Append in the same session
Append at a new session boundary
Rolling-snapshot rebuild
Unchanged snapshot detection
```

Update/append identity includes:

```text
Sequence
High
Low
Close
Volume
TradingDate
```

The runtime saves and restores:

```text
priceVolume
volume
priceSquaredVolume
lastTradingDate
```

## Module contract

```text
Module-Id       indicator.vwap
Default Panel   price.main
Primitive       Polyline
Contributions   5
```

Object identities:

```text
vwap.value
vwap.upper1
vwap.lower1
vwap.upper2
vwap.lower2
```

Properties:

```text
stdDev1                    RecalculateModule
stdDev2                    RecalculateModule
vwap.value.stroke          RedrawOnly
vwap.upper1.stroke         RedrawOnly
vwap.lower1.stroke         RedrawOnly
vwap.upper2.stroke         RedrawOnly
vwap.lower2.stroke         RedrawOnly
```

## Required fixture

The parity fixture contains at least three explicit trading dates:

```text
Session 1: 40 or more bars
Session 2: 40 or more bars
Session 3: first bar has zero volume, followed by positive volume
```

Required paths:

```text
Legacy full parity
last-bar OHLCV update parity
same-session append parity
next-session first-bar reset parity
zero-volume NaN parity
rolling-snapshot rebuild parity
missing-TradingDate rejection
active-only calculation
five price.main contributions
object-specific style override
StdDev recalculation
profile save and restore
reference-boundary verification
Release configuration verification
```

## Expected verification markers

```text
csharp_vwap_module_release_configuration=PASS
csharp_vwap_module_definition=PASS
csharp_vwap_module_metadata=PASS
csharp_vwap_full_parity=PASS
csharp_vwap_update_parity=PASS
csharp_vwap_append_parity=PASS
csharp_vwap_session_reset_parity=PASS
csharp_vwap_rebuild_parity=PASS
csharp_vwap_zero_volume_parity=PASS
csharp_vwap_disabled_zero=PASS
csharp_vwap_contributions=PASS
csharp_vwap_panel_contract=PASS
csharp_vwap_style_override=PASS
csharp_vwap_parameter_change=PASS
csharp_vwap_trading_date_contract=PASS
csharp_vwap_reference_boundary=PASS
csharp_vwap_module_contracts=PASS
csharp_app_vwap_module_data=PASS
csharp_app_vwap_module_parameters=PASS
csharp_app_vwap_module_style=PASS
csharp_app_vwap_panel_contract=PASS
csharp_app_vwap_session_reset=PASS
csharp_app_vwap_module_roundtrip=PASS
```
