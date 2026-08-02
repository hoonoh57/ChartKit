# Skia renderer crash regression

## Reproduction

A desktop replay restart with two symbols, one-minute candles, and `--count 1323` terminated the process with `System.AccessViolationException` in `SkiaApi.sk_canvas_draw_path`, reached from `SkiaChartRenderer.DrawLineSeries`.

## Failure boundary

The failure occurred in the legacy indicator renderer before the module render-plan renderer. `SkiaChartRenderer` reused native `SKPath` instances across draw calls and did not prevent nested or concurrent entry while those paths and paints were mutable. The line and histogram paths also validated source values but did not validate transformed canvas coordinates before passing geometry to Skia.

## Guard

The renderer now:

- admits only one active `Render` call per instance;
- makes `Dispose` wait for the active render to leave before releasing native Skia objects;
- restores the canvas in a `finally` block;
- rejects non-finite transformed line and histogram coordinates;
- avoids drawing empty indicator paths;
- restores temporary paint style in a `finally` block.

`RenderingVerification` includes a parallel re-entry probe and emits `csharp_rendering_reentry_guard=PASS`.

## Required verification

1. no-incremental Release build;
2. full EngineVerification including the re-entry marker;
3. App self-test;
4. desktop replay restart with `--symbols S001,S002 --timeframe 1m --count 1323`;
5. confirm VWAP profile restoration and `faults 0` after restart.

The VWAP parity PR must remain Draft until the desktop restart reproducer completes without a native crash.

Incident branch head must be verified exactly before Ready/merge.

CI trigger note: this head includes the renderer crash guard and its parallel re-entry regression test.
