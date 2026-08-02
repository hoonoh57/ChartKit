# Kiwoom actual-session realtime validation runbook

This runbook defines the remaining acceptance evidence for PR #3.
Scripted fixtures and simulated reconnects do not satisfy these gates.

## PowerShell symbol argument rule

KRX stock codes must remain six-character strings. PowerShell removes leading zeros when an unquoted value such as `005930,000660` is parsed as a numeric array.

Use one of these quoted forms:

```powershell
# Direct dotnet command
--symbols "005930,000660"

# PowerShell validation script
-Symbols '005930','000660'
```

## Preconditions

- Run during an active KRX market session.
- Use liquid symbols that are expected to trade during the probe.
- `.env` or process environment must contain the matching mock or real credentials.
- Do not print or commit credentials.
- Keep PR #3 Draft and unmerged until all three phases pass.

## Phase 1: REST seed to WebSocket continuity

```powershell
.\scripts\run_kiwoom_realtime_validation.ps1 `
  -Mode Baseline `
  -Symbols '005930','000660' `
  -DurationSeconds 180 `
  -Timeframe 1m `
  -HistoryCount 240
```

Required evidence:

```text
kiwoom_realtime_validation_probe=PASS
realtime_event_count > 0
rest_seed_continuity_observed=True
diagnostics_consistent=True
validation_result=PASS
```

At least one active symbol must report `SeedUpdated` or `SeedAppended`.
The first accepted WebSocket event must continue from the REST history seed without a synthetic gap or duplicate reconstruction.

## Phase 2: physical network reconnect

```powershell
.\scripts\run_kiwoom_realtime_validation.ps1 `
  -Mode Reconnect `
  -Symbols '005930','000660' `
  -DurationSeconds 600 `
  -Timeframe 1m `
  -HistoryCount 240 `
  -SkipBuild
```

After realtime samples are arriving:

1. Physically disconnect the active network adapter or cable for 10-20 seconds.
2. Restore the same network.
3. Do not restart the application.
4. Leave the probe running until it exits.

Required evidence:

```text
physical_reconnect_observed=True
connection attempts >= 2
registrations >= 2
diagnostics_consistent=True
validation_result=PASS
```

Disallowed substitutes:

- scripted fake socket disconnect;
- process restart;
- application restart;
- manually rerunning the command after failure;
- test-double reconnect verification.

## Phase 3: intraday soak

```powershell
.\scripts\run_kiwoom_realtime_validation.ps1 `
  -Mode Soak `
  -Symbols '005930','000660' `
  -DurationSeconds 3600 `
  -Timeframe 1m `
  -HistoryCount 240 `
  -SkipBuild
```

Required evidence:

```text
kiwoom_realtime_validation_probe=PASS
realtime_event_count > 0
rest_seed_continuity_observed=True
diagnostics_consistent=True
validation_result=PASS
```

The soak log must show no process crash, unhandled exception, diagnostic count mismatch, row sorting, or duplicate deletion.
Transient stale ticks may be rejected and counted; they must not reorder accepted provider events.

## Log handling

Logs are written under:

```text
artifacts/realtime-validation/
```

Each log records:

- branch and exact Git head;
- mode, symbols, timeframe, duration;
- bounded realtime samples;
- per-symbol update/append/stale counts;
- connection attempts and registrations;
- seed boundary state;
- reconnect observation;
- final validation result.

Do not commit logs containing operational account information. Attach only reviewed logs or sanitized summaries to the PR discussion.

## Merge rule

PR #3 may move from Draft only after all three actual-session phases pass on an exact known head. A scripted reconnect result is supporting regression evidence only and is never equivalent to the physical reconnect phase.
