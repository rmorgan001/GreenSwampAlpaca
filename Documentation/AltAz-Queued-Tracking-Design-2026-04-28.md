# AltAz Queued Tracking — Design Document

**Last updated: 2026-04-28 08:02**

---

## 1. Purpose and Scope

This document describes the queued-tracking architecture used for AltAz-mode telescope
mounts in the GreenSwamp Alpaca solution. It covers:

- The problem solved by introducing a command queue
- The architectural pattern (single-consumer channel)
- Every command type and its contract
- The `SkyPredictor` coordinate-propagation model
- How and where commands are enqueued across the three main use cases:
  post-slew tracking, pulse guiding, and ASCOM rate-offset commands
  (`RightAscensionRate` / `DeclinationRate`)
- Lifecycle management (start / stop)
- The `TrackingSnapshot` read model consumed by the Blazor UI and ASCOM
  read-back properties
- Thread-safety guarantees and the design decisions (D1–D8) that govern them

**Files covered:**

| File | Role |
|------|------|
| `GreenSwamp.Alpaca.MountControl/TrackingCommands.cs` | Command record definitions and `ITrackingCommand` marker interface |
| `GreenSwamp.Alpaca.MountControl/TrackingCommandProcessor.cs` | `TrackingCommandProcessor` — channel owner and single consumer |
| `GreenSwamp.Alpaca.MountControl/SkyPredictor.cs` | RA/Dec propagation model |
| `GreenSwamp.Alpaca.MountControl/Mount.cs` (partial) | Timer wiring, `SetRateRa`, `SetRateDec`, `PulseGuideAltAz`, `AbortSlewAsync` |
| `GreenSwamp.Alpaca.MountControl/Mount.Tracking.cs` (partial) | `SetTracking`, `ApplyTracking`, `SkyGetRate` |
| `GreenSwamp.Alpaca.MountControl/Mount.Operations.cs` (partial) | `AbortSlew`, slew boundaries |
| `GreenSwamp.Alpaca.MountControl/SlewController.cs` | `SendSlewBoundaryAck` — slew/boundary synchronisation |
| `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs` | ASCOM command entry points (`RightAscensionRate`, `DeclinationRate`, `PulseGuide`) |
| `GreenSwamp.Alpaca.MountControl/Interfaces/ITrackingController.cs` | Public interface contract |

---

## 2. Background — Why a Queue?

Before the queue was introduced, AltAz tracking state (`_altAzTrackingTimer`,
`SkyPredictor`, `Tracking`) was written from multiple concurrent threads:

- The multimedia timer callback (every ~100 ms on a thread-pool thread)
- ASCOM property setters (`RightAscensionRate`, `DeclinationRate`) from an ASP.NET
  request thread
- Pulse-guide `Task.Run` bodies
- Slew completion callbacks

This produced nine documented race conditions (RC-1 through RC-9), including
torn `SkyPredictor` state (RA updated but Dec not yet), tick-storms from the
timer callback, and rate changes being silently overwritten mid-slew. The full
analysis is in `AltAz-Tracking-Race-Condition-Analysis-2026-04-26.md`.

The queue eliminates all nine races by routing every *write* to `SkyPredictor`
and every start/stop of the `MediaTimer` through a single consumer task.

---

## 3. Architectural Pattern

### 3.1 Single-Consumer Channel (Producer–Consumer)

```
Any thread                 Channel<ITrackingCommand>        Consumer task
(ASCOM setter,  ──Post──►  UnboundedChannel                 ──► Process(cmd)
 timer callback,           SingleReader = true               runs on one thread
 slew code,                SingleWriter = false
 pulse guide)
```

The channel is `System.Threading.Channels.Channel<ITrackingCommand>` configured
as **unbounded, single-reader, multi-writer**. This means:

- Writers (`Post`, `PostTick`) never block; they return immediately.
- Only one thread — the consumer task — ever calls `SkyPredictor.Set`,
  `StartAltAzTrackingTimerInternal`, `StopAltAzTrackingTimerInternal`, or
  `SetTracking`.
- All ordering between producers is preserved by channel FIFO semantics.

### 3.2 Marker Interface

```csharp
internal interface ITrackingCommand { }
```

All command records implement this interface. The consumer dispatches on the
concrete type via a `switch` expression (`Process` method in
`TrackingCommandProcessor`). Adding a new command type requires only:

1. A new `sealed record` in `TrackingCommands.cs`.
2. A new `case` in `TrackingCommandProcessor.Process`.

### 3.3 Immutable Snapshot Read Model

```csharp
internal sealed record TrackingSnapshot(
	bool Tracking,
	TrackingMode Mode,
	double RateRa,
	double RateDec,
	DateTimeOffset PublishedAt);
```

After every command that changes observable state the consumer calls
`PublishSnapshot`, which atomically replaces the `volatile TrackingSnapshot?
_lastSnapshot` field. Readers (Blazor UI, ASCOM `RightAscensionRate` get,
`DeclinationRate` get) access `LastSnapshot` without locks and without risk of
torn reads (design decision D5).

---

## 4. `ITrackingController` Interface

`ITrackingController` (in `GreenSwamp.Alpaca.MountControl.Interfaces`) defines
the public contract that the Blazor UI and higher-level code use to control
tracking without depending on the concrete `Mount` class:

| Member | Description |
|--------|-------------|
| `bool Tracking { get; set; }` | Whether tracking is currently enabled |
| `DriveRate TrackingRate { get; set; }` | Sidereal / Lunar / Solar / King base rate |
| `double RightAscensionRate { get; set; }` | RA offset rate (arcsec/sec) |
| `double DeclinationRate { get; set; }` | Dec offset rate (arcsec/sec) |
| `void StartTracking()` | Enable tracking at current rate |
| `void StopTracking()` | Disable tracking |
| `void SetTrackingRate(DriveRate)` | Change base drive rate |
| `void SetTrackingOffsets(double, double)` | Set custom RA/Dec rate offsets |
| `bool CanSetTracking` | Capability flag |
| `bool CanSetDeclinationRate` | Capability flag |
| `bool CanSetRightAscensionRate` | Capability flag |

The concrete `Mount` class implements `ITrackingController` as part of the
wider `IMountController` interface.

---

## 5. Command Reference

All commands are `internal sealed record` types in
`GreenSwamp.Alpaca.MountControl`.

### 5.1 `TrackingStateCommand(bool Tracking)`

**Purpose:** Apply the current tracking state and mode; start or stop the
AltAz timer as required.

**Consumer action:** Calls `_mount.ApplyTracking(ts.Tracking)` then publishes
a snapshot.

**Enqueued by:**

| Call site | Trigger |
|-----------|---------|
| Post-slew restore | After a GoTo or AltAz slew completes and tracking should resume |
| `Tracking` property setter | When an ASCOM client sets `Telescope.Tracking` |
| `AbortSlew` / `AbortSlewAsync` | After axes stop, to restore the pre-abort tracking state |

### 5.2 `TimerTickCommand`

**Purpose:** Deliver a timer tick from the multimedia timer callback to the
consumer without blocking the callback thread.

**Consumer action:** Clears `_tickPending`, then calls `_mount.SetTracking()`
if the mount is running and tracking. This translates the current predicted RA/Dec
into Alt/Az hardware rates and issues `SkyAxisSlew` commands to the hardware queue.

**Enqueued by:** `Mount.AltAzTrackingTimerTick` → `_trackingProcessor.PostTick()`.
`PostTick` uses `Interlocked.CompareExchange` to ensure at most one
`TimerTickCommand` is queued at any time (D5 — prevents tick storms).

### 5.3 `RateChangeCommand(double RateRa, double RateDec)`

**Purpose:** Atomically update both RA and Dec rate offsets on the predictor
and issue a new `SetTracking` call (D1/D2).

**Consumer action:**
1. If tracking is already active: calls `SkyPredictor.GetRaDecAtTime(now)` to
   propagate the current predictor position forward to *now*, then re-seeds
   the predictor with the new rates.
2. If tracking is not yet active: seeds the predictor from the current
   mount position (`RightAscensionXForm`, `DeclinationXForm`) with the new rates.
3. Calls `_mount.SetTracking()` to push the updated Alt/Az rates to hardware.
4. Publishes a snapshot.

**Writer-side merge (D2):** Before posting, the caller always carries the
*other* axis's current value so the consumer always applies both axes together:

```csharp
// In Mount.SetRateDec:
_trackingProcessor.Post(new RateChangeCommand(RateRa, degrees));   // merge current RateRa

// In Mount.SetRateRa:
_trackingProcessor.Post(new RateChangeCommand(degrees, RateDec));  // merge current RateDec
```

**Enqueued by:** `Mount.SetRateDec` and `Mount.SetRateRa` (AltAz mode only).

### 5.4 `PulseGuideCommand(int Axis, double GuideRate, int DurationMs)`

**Purpose:** Route the `SkyPredictor` coordinate adjustment for a single
pulse-guide axis through the queue, so the predictor write cannot race the
timer tick (D4/D8).

**Consumer action (`ApplyPulseGuide`):**

| Axis | Predictor adjustment |
|------|---------------------|
| 0 (RA) | `Ra -= DurationMs × 0.001 × GuideRate / SiderealRate` |
| 1 (Dec) | `Dec += DurationMs × GuideRate × 0.001` |

The *hardware* pulse (`pulseGoTo` action) runs on the `Task.Run` thread in
`PulseGuideAltAz`; only the two `SkyPredictor.Set` calls are serialised through
the queue.

**Enqueued by:** `Mount.PulseGuideAltAz` — after the preceding
`SlewBoundaryCommand` ACK has been received (ensuring the timer is stopped
before the predictor is written).

### 5.5 `SeedAndEnableCommand(double Ra, double Dec, double RateRa, double RateDec)`

**Purpose:** Re-seed the predictor with known coordinates and immediately
re-enable tracking. Used by sync paths S7/S8 (Option A, D6).

**Consumer action:**
1. `_mount.SkyPredictor.Set(Ra, Dec, RateRa, RateDec)` — authoritative seed.
2. `_mount.ApplyTracking(true)` — start the timer and issue hardware rates.
3. Publishes a snapshot.

### 5.6 `SlewBoundaryCommand(TaskCompletionSource Ack)`

**Purpose:** Mark a slew or pulse-guide boundary. The consumer stops the
AltAz timer and signals the ACK, making it safe for the calling thread to write
`SkyPredictor` directly. (Option A, D6.)

**Consumer action:**
1. `_mount.StopAltAzTrackingTimerInternal()` — timer stopped while consumer owns it.
2. `sb.Ack.TrySetResult()` — unblocks the calling thread.

The caller (`SendSlewBoundaryAck` in `SlewController`, or `PulseGuideAltAz` in
`Mount.cs`) blocks for up to 500 ms on `ack.Task.Wait(500)`. This synchronous
contract (Q1/D7) guarantees the timer is stopped before hardware slew commands
are issued.

**Enqueued by:**
- `SlewController.SendSlewBoundaryAck` — called at the start of every GoTo slew.
- `Mount.PulseGuideAltAz` — called before each axis's `PulseGuideCommand`.

### 5.7 `StopTrackingCommand`

**Purpose:** Unconditional, immediate stop: timer stopped, tracking off,
predictor reset, hardware axes stopped. Designed so that any
`RateChangeCommand` already in the channel cannot re-arm tracking after the
abort returns.

**Consumer action:**
1. `_mount.SkyPredictor.Reset()`
2. `_mount.Tracking = false`; `_mount.TrackingMode = TrackingMode.Off`
3. `_mount.SetTracking()` (issues zero-rate hardware commands)
4. Publishes a snapshot.

**Enqueued by:** `AbortSlewAsync` and `AbortSlew` in the AltAz abort path.

### 5.8 `ResumeTrackingCommand`

**Purpose:** Unconditionally re-apply current tracking rates and restart the
AltAz timer after a pulse guide completes. Unlike `TrackingStateCommand`, this
bypasses `ApplyTracking`'s early-exit guard (`if (tracking == Tracking) return`),
which would suppress `SetTracking()` / `SkyAxisSlew` on SkyWatcher hardware
when `Tracking` was never set to `false` during the pulse.

**Consumer action:** If mount is running and tracking, calls `_mount.SetTracking()`.
Publishes a snapshot.

**Enqueued by:** `Mount.PulseGuideAltAz` — after the hardware pulse action
completes and the cancellation token has not been set.

---

## 6. `SkyPredictor` — Coordinate Propagation

`SkyPredictor` maintains a time-stamped RA/Dec position plus rate offsets. All
writes are serialised through the consumer task.

| Method | Effect |
|--------|--------|
| `Set(ra, dec, raRate, decRate)` | Full seed: sets all four fields and `ReferenceTime = now` |
| `Set(ra, dec)` | Position-only update: preserves current rates |
| `Reset()` | All fields set to defaults (`NaN` for coords, 0 for rates) |
| `GetRaDecAtTime(t)` | Returns propagated `[ra, dec]` at time `t` without mutating state |
| `GetRaDecAtTime(t, out ra, out dec)` | Overload; incorporates tracking-rate deviation from sidereal |
| `SetRaDecNow()` | Advances `Ra`/`Dec` to the current time, updates `ReferenceTime`; called internally on rate-property writes |

**Propagation formula:**

```
Ra(t)  = Ra(t0) + (t - t0) × RateRa  / 15     [hours]
Dec(t) = Dec(t0) + (t - t0) × RateDec          [degrees]
```

`SkyPredictor` is a `public class` owned per-`Mount` instance. It is
constructed in `Mount`'s constructor with a `Func<double> trackingRateProvider`
delegate (`() => SkyServer.CurrentTrackingRate(this)`) so that the tracking rate
can be queried without coupling the predictor to the static `SkyServer`.

---

## 7. Use Case Walkthroughs

### 7.1 Post-Slew Tracking Restore

```
SlewController
  └─ SendSlewBoundaryAck()
	   ├─ Post(SlewBoundaryCommand(ack))    ← stops timer, ACKs
	   └─ ack.Task.Wait(500)
  [slew movement completes]
  └─ Post(TrackingStateCommand(true))       ← re-enables tracking
	   └─ Consumer: ApplyTracking(true)
			├─ SkyPredictor.Set(ra, dec, rateRa, rateDec)
			├─ StartAltAzTrackingTimerInternal()
			└─ SetTracking() → SkyAxisSlew to hardware
  └─ PublishSnapshot()
```

At every subsequent timer tick:

```
MediaTimer callback
  └─ PostTick()                             ← Interlocked guard; at most 1 queued
Consumer
  └─ TimerTickCommand
	   └─ SetTracking()
			└─ SkyServer.SetAltAzTrackingRates(Predictor, mount)
			└─ SkyAxisSlew(axis1, altRate)
			└─ SkyAxisSlew(axis2, azRate)
```

### 7.2 Pulse Guiding (AltAz Mode)

```
Telescope.PulseGuide(direction, duration)         ← ASCOM entry point
  └─ Mount.PulseGuide(direction, duration, 0)
	   └─ PulseGuideAltAz(axis, guideRate, duration, pulseGoTo, token)
			[Task.Run]:
			├─ Post(SlewBoundaryCommand(ack))       ← stops timer
			├─ ack.Task.Wait(500)
			├─ Post(PulseGuideCommand(axis, rate, duration))
			│     └─ Consumer: ApplyPulseGuide → SkyPredictor.Set(adjusted coords)
			├─ pulseGoTo(token)                     ← hardware SkyAxisSlew on Task.Run
			└─ Post(ResumeTrackingCommand)           ← unconditional re-arm
				  └─ Consumer: SetTracking()
					   └─ SkyAxisSlew (normal tracking rates restored)
```

For simultaneous dual-axis pulse guides the second axis checks `_isPulseGuidingRa`
/ `_isPulseGuidingDec`; if the other axis is already guiding it cancels the
other CTS rather than posting a second `SlewBoundaryCommand` (which would
deadlock because the timer is already stopped).

### 7.3 ASCOM Rate Offset (`RightAscensionRate` / `DeclinationRate`)

```
Telescope.RightAscensionRate.set(value)           ← ASCOM client call
  └─ _mount.RateRaOrg = value
  └─ _mount.SetRateRa(Conversions.ArcSec2Deg(Conversions.SideSec2ArcSec(value)))
	   ├─ RateRa = degrees                         ← field updated immediately
	   └─ Post(RateChangeCommand(degrees, RateDec)) ← writer-side merge
			└─ Consumer (RateChangeCommand):
				 ├─ If Tracking: GetRaDecAtTime(now) → propagate predictor
				 │                SkyPredictor.Set(propagated ra, dec, newRa, Dec)
				 └─ Else:        SkyPredictor.Set(RightAscensionXForm, DeclinationXForm,
				 │                                 newRa, RateDec)
				 └─ SetTracking() → SkyAxisSlew (new Alt/Az rates to hardware)
				 └─ PublishSnapshot()
```

`DeclinationRate` follows the same path, merging `RateRa` into the
`RateChangeCommand` so that both axes are always applied atomically.

---

## 8. Lifecycle

### 8.1 Start (`MountConnect`)

```csharp
_trackingProcessor = new TrackingCommandProcessor(this);
_trackingProcessor.Start(cancellationToken);
```

`Start` launches the consumer on a dedicated `LongRunning` thread via
`Task.Factory.StartNew`. This isolates the consumer from the thread-pool and
prevents starvation under load.

### 8.2 Stop (`MountDisconnect`)

```csharp
await _trackingProcessor.StopAsync();
```

`StopAsync` calls `_channel.Writer.TryComplete()`, which signals the
`ReadAllAsync` enumerator to complete after draining remaining items. The
consumer task is awaited; `OperationCanceledException` is swallowed.

---

## 9. Thread-Safety Summary

| State written | Written by | Guarded by |
|---------------|-----------|------------|
| `SkyPredictor.*` | Consumer task only | Single-reader channel |
| `_altAzTrackingTimer` start/stop | Consumer task only | Single-reader channel |
| `Mount.Tracking` / `TrackingMode` | Consumer task only (for AltAz) | Single-reader channel |
| `_lastSnapshot` | Consumer task only | `volatile` write |
| `_tickPending` | Any thread / consumer | `Interlocked.CompareExchange` |
| `RateRa`, `RateDec` fields | ASCOM setter thread | Written before `Post`; consumer reads after |

The `AbortSlew` / `AbortSlewAsync` paths bypass the queue and call
`ApplyTracking(false)` directly. This is intentional (design note in code):
the abort must take effect synchronously and must not be delayed by any
`RateChangeCommand` ahead of it in the channel. The subsequent
`Post(new StopTrackingCommand())` drains any queued rate-change commands that
might otherwise re-arm tracking.

---

## 10. Design Decisions Reference

The following shorthand labels appear in code comments and cross-reference the
race-condition analysis document.

| Label | Decision |
|-------|----------|
| D1 | `SetTracking()` posted immediately on `RateChangeCommand` — no deferred batch |
| D2 | Writer-side merge: both axes carried in every `RateChangeCommand` |
| D4 | `SkyPredictor.Set` in pulse guide routed through queue, not direct write |
| D5 | Timer callback returns immediately; `_tickPending` prevents tick storms |
| D6 | `SlewBoundaryCommand` / Option A: timer stopped before caller writes predictor |
| D7 | 500 ms timeout on `ack.Task.Wait` — matches position-update timeout |
| D8 | Hardware pulse action stays on `Task.Run`; only predictor write queued |

---

## 11. Sequence Diagram — Normal Tracking Cycle

```
Timer          PostTick()         Channel         Consumer task        SkyAxisSlew
  │               │                  │                  │                   │
  │──fire────────►│                  │                  │                   │
  │               │──Interlocked──►  │                  │                   │
  │               │  (if 0→1)        │                  │                   │
  │               │──TryWrite────►TimerTickCommand       │                   │
  │               │                  │──ReadAllAsync──►  │                   │
  │               │                  │                  │─ Interlocked 1→0   │
  │               │                  │                  │─ SetTracking()     │
  │               │                  │                  │──SkyAxisSlew Alt──►│
  │               │                  │                  │──SkyAxisSlew Az───►│
  │               │                  │                  │─ (no snapshot pub) │
```

---

*Document generated from source review of commit on branch `master`,*
*repository `https://github.com/Principia4834/GreenSwampAlpaca`.*
