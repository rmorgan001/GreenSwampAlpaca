# Async Behaviour Analysis: RightAscensionRate, DeclinationRate, Tracking

Updated: 2026-05-10 18:43

## Scope

This document captures the results of:

1. Step 1: Map exact timing windows and race surfaces for ASCOM client calls to `RightAscensionRate`, `DeclinationRate`, and `Tracking`.
2. Step 2: Propose a minimal design to make these setters complete only after queue execution (without changing FIFO command ordering).

Source log used for timing evidence:

- `c:\Users\Andy\Documents\GSServer\GSFastMonitorLog2026-05-10-18-28-43.txt`

---

## Step 1: Execution Flow and Timing Windows

## 1) What was measured

For each setter event in the log, the first corresponding apply event was measured:

- `set_RightAscensionRate` -> first `AxisSlew|Axis1|...`
- `set_DeclinationRate` -> first `AxisSlew|Axis2|...`
- `set_Tracking` -> first `SetTracking|...`

Measured windows from this capture:

- `set_RightAscensionRate` to `AxisSlew|Axis1`: ~0 ms
- `set_DeclinationRate` to `AxisSlew|Axis2`: ~7 ms
- `set_Tracking|True` to first `SetTracking|...`: ~1-2 ms

Representative evidence lines:

- RA call path in log: lines 165 -> 171, 205 -> 211, 423 -> 429, 463 -> 469
- Dec call path in log: lines 175 -> 184, 215 -> 232, 433 -> 442, 473 -> 490
- Tracking call path in log: lines 238 -> 250 and 496 -> 507

## 2) Code-path confirmation

ASCOM request layer executes setter and returns response after `operation.Invoke()`:

- `ASCOM.Alpaca.Razor/Controllers/BaseController.cs` (ExecuteRequest + Invoke)

Telescope driver setters update software state and dispatch mount calls:

- `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs`
  - `DeclinationRate` setter -> `_mount.RateDecOrg = value; _mount.SetRateDec(...)`
  - `RightAscensionRate` setter -> `_mount.RateRaOrg = value; _mount.SetRateRa(...)`
  - `Tracking` setter -> `_mount.ApplyTracking(value)`

Mount tracking/rate application enqueues queue commands:

- `GreenSwamp.Alpaca.MountControl/Mount.Lifecycle.cs`
  - `ApplyTracking(bool)` sets `Tracking = tracking` then calls `SetTracking()`
- `GreenSwamp.Alpaca.MountControl/Mount.Tracking.cs`
  - `SetTracking(...)` creates `SkyAxisSlew` / simulator tracking commands

Queue behaviour:

- `GreenSwamp.Alpaca.Mount.Commands/CommandBase.cs`
  - constructor enqueues command via `queue.AddCommand(this)`
- `GreenSwamp.Alpaca.Mount.Commands/CommandQueueBase.cs`
  - blocking wait exists only via `GetCommandResult(...)`
  - current rate/tracking setters do not use `GetCommandResult(...)`

## 3) Race surfaces identified

1. Setter returns before hardware apply completes.
2. Read-after-write can report software state before mount has physically applied queued command.
3. Tracking getter can report optimistic state because `Tracking` field is set before queued stop/start fully executes.
4. Calls can be accepted back-to-back before previous command execution finishes.

Note: FIFO ordering is still preserved by the queue. This is an apply-latency visibility issue, not a command reordering issue.

## Step 1 result

Assertion: True.

New ASCOM calls can be issued and accepted before prior rate/tracking changes have completed at hardware level.

---

## Step 2: Minimal Design to Provide Completion Semantics

## Design goal

Preserve current queue architecture and FIFO ordering, but provide a blocking completion path for ASCOM setters when required.

## Safest barrier location

Queue boundary in MountControl (not the HTTP controller):

- Wait on command completion using existing `GetCommandResult(...)` semantics in `CommandQueueBase`.
- This keeps waiting tied to actual queue execution outcome and existing timeout/error handling.

## Minimal implementation approach

1. Add blocking variants in Mount API:
   - `SetRateRaAndWait(...)`
   - `SetRateDecAndWait(...)`
   - `ApplyTrackingAndWait(bool tracking)`

2. Refactor `SetTracking(...)` to expose issued command handles when needed:
   - Return issued commands (or a small result object containing command refs per axis).
   - For SkyWatcher: capture created `SkyAxisSlew` command objects.
   - For simulator: capture `CmdAxisTracking` / `CmdRaDecRate` objects.

3. In blocking variants, call `Queue.GetCommandResult(command)` for each issued command:
   - Rate setters: wait for the changed axis command only.
   - Tracking false: wait for both zero-rate axis commands.
   - Tracking true: wait for whichever axis commands were issued by current mode.

4. Keep existing non-blocking methods unchanged for internal high-frequency flows.

5. Use existing queue timeout/failure reporting:
   - Surface timeout/exception back to ASCOM caller as operation failure.

## Why this is minimal and low risk

1. Reuses existing queue completion primitives already in production.
2. Avoids redesign of command processor or threading model.
3. Limits behavioural change to explicit blocking call path.
4. Preserves current optimized axis-specific command reduction.

## Suggested rollout

1. Implement blocking variants and wire Telescope driver setters to them.
2. Keep old non-blocking methods for internal callers.
3. Add monitor logs for queue wait duration per setter to quantify overhead.
4. Validate with GSFastMonitor traces: setter return should now occur after corresponding axis apply event.

## Step 2 result

A minimal, concrete design exists to make ASCOM setters complete after queue execution while preserving FIFO and minimizing architectural risk.

---

## Final combined conclusion

1. Step 1 confirms the async gap is real: setter acknowledgement can precede physical apply.
2. Step 2 provides a targeted fix: block at queue completion boundary using existing `GetCommandResult(...)`.
3. This addresses the concern without changing command ordering or removing current performance optimization work.
