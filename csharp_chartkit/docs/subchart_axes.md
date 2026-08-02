# Subchart right-axis model

- `ChartKit.Charting` computes each visible subchart panel range and fixed-capacity numeric axis ticks.
- `ChartKit.Rendering` draws only the prepared horizontal grid lines and right-side labels.
- The model is indicator-agnostic and supports positive, negative, bounded, and wide numeric ranges.
- Each visible subchart receives at least two ticks; normal ranges use human-readable 1/2/2.5/5/10 intervals.
- Panel-axis frame generation remains allocation-free after warm-up.
