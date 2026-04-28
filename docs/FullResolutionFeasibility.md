# Full-Resolution Advanced Command Set — Feasibility & Impact Report

**Author:** GitHub Copilot (review requested by Andy)
**Date:** 2026-04-28 16:07
**Scope:** GreenSwampAlpaca solution — read-only analysis; no code was changed.

---

## 1. Background

SkyWatcher mounts that support the **Advanced Command Set** (firmware ≥ 3.22.xx) expose a
higher-resolution encoder count than the **Standard Command Set**.  The ratio between the two
counts is stored per-axis in:

```csharp
// Commands.cs  line 58
private readonly int[] _resolutionFactor = [1, 1];
```

For an EQ6 (and similar) this factor is **4**, meaning the advanced count is four times the
standard count.  At present the code divides every advanced-command position and speed back down
by `_resolutionFactor` before the rest of the application sees it, so the pipeline is
unaffected.  The proposal is to **remove that division**, propagating the full native resolution
through the pipeline.

---

## 2. How `_resolutionFactor` Is Used Today

`_resolutionFactor` appears in the following methods inside `Commands.cs`.  Each one is examined
below.

### 2.1 `GetResolutionFactors()` (lines 282–340)

Calculates the factor from the ratio of `:X0002` (advanced, full resolution) to `:a`
(standard, reduced resolution).

```csharp
if (revOld[0] > 0) { _resolutionFactor[0] = (int)(revNew[0] / revOld[0]); }
```

For EQ6: `revNew[0]` ≈ 4 × `revOld[0]`  →  `_resolutionFactor[0] = 4`.

### 2.2 `GetStepsPerRevolution()` (lines 346–414)

When using the advanced set the raw 32-bit count is divided by `_resolutionFactor`:

```csharp
var gearRatio = String32ToInt(response, true, _resolutionFactor[(int)axis]);
```

This makes `_axisGearRatios` / `_factorRadToStep` / `_factorStepToRad` appear identical to the
standard-command values.  **This is the primary normalisation point.**

### 2.3 `GetAxisPosition()`, `GetAxisPositionNaN()`, `GetAxisStepsNaN()`, `GetStartupPosition()` (lines 695–866)

All use `String32ToInt(response, true, _resolutionFactor[…])` to divide position readings back
to standard-resolution steps before converting to radians.

### 2.4 `GetLastSlewSpeed()` (line 665)

```csharp
iSpeed = String32ToInt(response, true, _resolutionFactor[(int)axis]);
```

Divides the advanced slew-speed reading to match the `:i` standard scale.

### 2.5 `GetPecPeriod()` (line 958)

```csharp
pecPeriod = String32ToInt(response, true, _resolutionFactor[(int)axis]);
```

Normalises the PEC worm-period count.

### 2.6 `GetHomePosition()` (line 889)

```csharp
return Convert.ToInt32(position / _resolutionFactor[(int)axis]);
```

Reduces the home sensor position to the standard range.

### 2.7 `GetAxisPositionCounter()` (lines 842–866)

Uses `res = raw ? 1 : _resolutionFactor[…]` so callers that pass `raw = true` already bypass
division.

### 2.8 `SetAxisPosition()` (lines 1018–1037)

Multiplies **back up** by `_resolutionFactor` when writing the position to the mount:

```csharp
newStepIndex *= _resolutionFactor[(int)axis];
```

### 2.9 `AxisSlew_Advanced()` (lines 1374–1398) and `AxisSlewTo_Advanced()` (lines 1406–1434)

Both multiply computed steps by `_resolutionFactor` before sending the `:X02…` / `:X04…`
commands to the mount.  They contain an instructive comment:

```csharp
var irateInSteps = AngleToStep(axis, rateInRadian * 1024);
irateInSteps *= _resolutionFactor[(int)axis];
// var irateInSteps = AngleToStep(axis, rateInRadian * 1024 * _resolutionFactor[(int)axis]);
```

The commented-out alternative would achieve the same result if `AngleToStep` was using
full-resolution `_factorRadToStep`.

### 2.10 `String32ToInt()` (lines 2003–2025)

This is a helper used everywhere above.  The `divFactor` parameter is passed `_resolutionFactor`
to perform the normalisation in one place.

---

## 3. Architecture Summary

The current data flow is:

```
Mount hardware (full res)
	→ ":X" read → String32ToInt( ÷ resolutionFactor ) → standard-resolution steps
	→ _factorStepToRad (calibrated at standard res)
	→ radians
	→ Mount.cs ConvertStepsToDegrees / SetSteps
	→ UI / ASCOM API (degrees, RA/Dec)
```

For the **write** path:

```
Degrees / radians
	→ AngleToStep (using _factorRadToStep at standard res)
	→ × resolutionFactor
	→ ":X" write to mount (full res)
```

The `_resolutionFactor` is the translation layer that keeps the two paths balanced.

---

## 4. Proposed Change

Remove the normalisation division so that `_factorRadToStep` / `_factorStepToRad` are
calibrated at **full** resolution.  The entire pipeline upstream would then naturally
work at 4× (EQ6) resolution without needing any per-call `× resolutionFactor` correction.

---

## 5. Files and Methods That Would Change

### 5.1 `Commands.cs` (GreenSwamp.Alpaca.Mount.SkyWatcher)

This file is the heart of the change.  All modifications stay inside the SkyWatcher driver
project and are invisible to callers.

| Method | Required Change |
|--------|----------------|
| `GetStepsPerRevolution()` | Remove `_resolutionFactor` from `String32ToInt` call; calibrate `_factorRadToStep`/`_factorStepToRad` at full res |
| `GetResolutionFactors()` | Factor is still needed to detect *whether* advanced res is available; value retained for diagnostics |
| `GetAxisPosition()` | Remove `_resolutionFactor` divisor; position already in full-res steps, conversion via updated `_factorStepToRad` is correct |
| `GetAxisPositionNaN()` | Same as above |
| `GetAxisStepsNaN()` | Same — returns raw full-res step count directly |
| `GetStartupPosition()` | Same |
| `GetAxisPositionCounter()` | `res` logic changes; `raw=true` already bypasses, default path no longer divides |
| `GetLastSlewSpeed()` | Remove `_resolutionFactor` divisor |
| `GetPecPeriod()` | Remove `_resolutionFactor` divisor — worm period is now in full-res steps; ensure PEC downstream is aware |
| `GetHomePosition()` | Remove `/ _resolutionFactor`; home position in full-res steps |
| `SetAxisPosition()` | Remove `*= _resolutionFactor` — steps already at full res |
| `AxisSlew_Advanced()` | Remove explicit `*= _resolutionFactor`; `AngleToStep` now returns full-res steps naturally |
| `AxisSlewTo_Advanced()` | Same |

### 5.2 `SkyWatcher.cs` (GreenSwamp.Alpaca.Mount.SkyWatcher)

`SetStepsPerSecond()` (line 1360–1364) calls `GetStepsPerRevolution()` which returns
`_axisGearRatios`.  If those ratios are now full-resolution the `_stepsPerSecond` values
(used for backlash-to-duration conversions in pulse guiding) will automatically scale correctly
— **no code change needed** there, but the **numerical values** in `_stepsPerSecond` will be
4× larger.

`AxisMoveSteps()` (line 620): the `movingSteps` argument is already in the *normalised*
resolution; callers compute this from `GetAxisPositionCounter()`.  If `GetAxisPositionCounter()`
returns full-res steps, `movingSteps` will also be full-res and `SetGotoTargetIncrement()` /
`SetGotoTargetIncrement()` will send the correct full-res value — no explicit change needed.

`AxisGoToTarget()` (line 697): calls `AngleToStep()` to compute `movingSteps`.  Once
`_factorRadToStep` is full-res, `AngleToStep()` returns full-res automatically.

### 5.3 `Mount.cs` (GreenSwamp.Alpaca.MountControl)

`_factorStep[]` (line 58) stores `_factorStepToRad` copied from `SkyWatcher` during
`MountConnect()` (via `SkyTasks(MountTaskName.GetFactorStep, …)`).  With full-resolution
calibration this array is 4× smaller (radians per step is smaller when there are more steps).

`ConvertStepsToDegrees()` (line 1450):

```csharp
case MountType.SkyWatcher:
	degrees = Principles.Units.Rad2Deg1(steps * _factorStep[axis]);
	break;
```

`steps` comes from `_steps[]` which is populated by `ReceiveSteps()` from `SkyWatcher.UpdateSteps()`
→ `GetAxisStepsNaN()`.  If `GetAxisStepsNaN()` returns full-res steps and `_factorStep` is
full-res, the multiplication yields the same angle in degrees.  **No code change, but both sides
must change together and atomically.**

`_stepsPerRevolution[]` (line 59): used for PEC calculations (`_pecBinSteps`, `_wormTeethCount`).
All values will be 4× larger but the ratios are preserved; PEC bin calculation is consistent.

`_stepsWormPerRevolution[]` (line 61): loaded via `StepsWormPerRevolution` task (calls
`GetPecPeriod()`).  If `GetPecPeriod()` returns full-res steps the worm period is 4× larger —
consistent with `_stepsPerRevolution`.

`GetRawSteps(int axis)` (line 1527): calls `SkyGetAxisPositionCounter` → `GetAxisPositionCounter()`.
Returns full-res steps automatically once the driver change is in place.

### 5.4 `SkyServer.Core.cs` (GreenSwamp.Alpaca.MountControl)

No changes to logic.  The `Steps` property and `SetSteps()` pipeline consume whatever the driver
provides in step units; conversion to degrees uses `_factorStep` which scales accordingly.

### 5.5 Other Projects

No changes are anticipated in:
- `GreenSwamp.Alpaca.Server` (Blazor UI)
- `GreenSwamp.Alpaca.Shared`
- `GreenSwamp.Alpaca.Settings`
- `GreenSwamp.Alpaca.Simulator` (unaffected — standard command set only)
- `GreenSwamp.Alpaca.MountControl.Tests`
- `ASCOM.Alpaca.Razor`

---

## 6. Compatibility With Standard-Command-Set Mounts

When `SupportAdvancedCommandSet` is `false` or `AllowAdvancedCommandSet` is `false`:

- `GetStepsPerRevolution()` takes the `else` branch and calls `:a` — reads standard-resolution
  directly.  `_resolutionFactor` remains `1`.
- All subsequent branches use `StringToLong()` (standard) rather than `String32ToInt()` (advanced).
- The full-resolution path is entirely behind `if (SupportAdvancedCommandSet && AllowAdvancedCommandSet)`.

**Conclusion: standard-command-set mounts are completely unaffected.**

---

## 7. Precision and Numerical Impact

For an EQ6 (factor = 4):

| Quantity | Standard | Full Res | Notes |
|----------|----------|----------|-------|
| Steps per revolution | ~11,957,119 | ~47,828,476 | Fits comfortably in `int` (max ~2.1 × 10⁹) |
| `_factorRadToStep` | ~1,903,428 | ~7,613,713 | Both fit in `double` without precision loss |
| `_factorStepToRad` | ~5.25 × 10⁻⁷ | ~1.31 × 10⁻⁷ | Well within `double` precision |
| Arc-seconds per step | ~0.1082″ | ~0.0270″ | The 4× resolution gain |
| `AxisSlew_Advanced` integer rate | 64-bit, no overflow | 64-bit, no overflow | ×4 head-room in `long` |

### `AngleToStep` return type

`AngleToStep()` currently returns `int` (line 1988).  Full-resolution step counts for a
360° slew on EQ6 would be ~47.8 M which is well within `int.MaxValue` (~2.1 × 10⁹).
For mounts with higher factors or future designs this could become a concern; changing to
`long` would be a simple, safe improvement.

### `String32ToInt` parser

Returns `int`.  The raw 32-bit hex value from `:X0002` for a full-revolution EQ6 would be
~47.8 M — within `int` range.  No change required for EQ6 and similar.  For mounts with
factor ≥ ~45 the value could overflow; flagging this for investigation before enabling on
untested models.

---

## 8. PEC Considerations

PEC uses `_stepsWormPerRevolution` and `_stepsPerRevolution` to calculate `_pecBinSteps`.
Both scale proportionally so the bin width in motor steps is correct.  The PEC data stored
in the mount firmware is at full hardware resolution, so this is actually an improvement.

The `:s` / `:X000E` PEC period commands would return full-res steps; the rest of the PEC
pipeline in `Mount.Pec.cs` operates in *normalised* step units that it receives from
`_stepsWormPerRevolution` — so the only change needed is the already-covered
`GetPecPeriod()` normalisation removal.

---

## 9. `GetHomePosition()` Edge Cases

The current code translates sentinel values (0, 0x00FFFFFF for standard; `Int32.MinValue`,
`Int32.MaxValue` for advanced) into very large magic numbers that the rest of the code treats
as "not set":

```csharp
case MinSteps:   return 100000000000;
case MaxSteps:   return 200000000000;
```

These magic numbers are already larger than any full-res step count, so the sentinel
comparison is performed *before* the division is applied.  Removing the division has no
effect on these sentinel returns.  **No risk here.**

---

## 10. `_breakSteps` Considerations

`_breakSteps` in `Commands.cs` is set to a fixed value of `3500` in `LoadMountDefaults()`
and is used by `AxisGoToTarget()` in `SkyWatcher.cs` as the ramp-down increment for
standard GoTo.  This value is in **standard-resolution** steps.  With full resolution it
should be scaled by `_resolutionFactor` (e.g., 14,000 for EQ6).

`_lowSpeedGotoMargin` in `Commands.cs` is similarly computed:

```csharp
_lowSpeedGotoMargin[…] = (long)(640 * Constant.SiderealRate * _factorRadToStep[…]);
```

Once `_factorRadToStep` is full-res this automatically scales — **no explicit change needed**.

`_breakSteps` is the one manually-set constant that would need to change or be made
resolution-aware.

---

## 11. Summary of Required Changes

| # | Location | Change | Risk |
|---|----------|--------|------|
| 1 | `Commands.GetStepsPerRevolution()` | Remove `_resolutionFactor` divisor from `String32ToInt`; calibrate factors at full res | Low — contained within driver |
| 2 | `Commands.GetAxisPosition()` et al. | Remove `_resolutionFactor` divisor from all `j`/`X0003` reads | Low |
| 3 | `Commands.GetLastSlewSpeed()` | Remove `_resolutionFactor` divisor | Low |
| 4 | `Commands.GetPecPeriod()` | Remove `_resolutionFactor` divisor | Medium — PEC timing |
| 5 | `Commands.GetHomePosition()` | Remove `/ _resolutionFactor` | Low — sentinels preserved |
| 6 | `Commands.SetAxisPosition()` | Remove `*= _resolutionFactor` | Low |
| 7 | `Commands.AxisSlew_Advanced()` | Remove explicit `*= _resolutionFactor` | Low |
| 8 | `Commands.AxisSlewTo_Advanced()` | Remove explicit `*= _resolutionFactor` | Low |
| 9 | `Commands.LoadMountDefaults()` | Scale `_breakSteps` by `_resolutionFactor` | Medium — GoTo ramp behaviour |
| 10 | `Commands.AngleToStep()` | Consider widening return type to `long` | Low / optional |
| 11 | No upstream changes required | `Mount.cs`, `SkyServer.Core.cs`, UI are all unaffected | — |

---

## 12. Recommended Implementation Strategy

1. **Feature flag first.**  Add a `bool UseFullResolution` property to `Commands` (defaulting
   to `false`) controlled via `AllowAdvancedCommandSet` or a new setting.  Apply changes
   conditionally so that full-resolution can be toggled at runtime without reconnecting.

2. **Change only `GetStepsPerRevolution()` first** (item 1 above) and verify that
   `_factorRadToStep` / `_factorStepToRad` are 4× different from the standard values in the
   monitor log.

3. **Remove the compensating multiplications** in `AxisSlew_Advanced()` and
   `AxisSlewTo_Advanced()` (items 7 and 8) — the mount should receive the same wire values
   as before because `AngleToStep()` now produces full-res steps.

4. **Update `_breakSteps`** (item 9) — multiply by `_resolutionFactor` in
   `LoadMountDefaults()`.

5. **Update read-back methods** (items 2–6) so all reported step counts are consistent.

6. **Regression test** on an EQ6 (or simulator with factor=4 injected) by verifying that
   RA/Dec reported position is identical before and after.

7. **Enable PEC** testing last, as it is the most sensitive to absolute step counts.

---

## 13. Risk Assessment

| Risk | Severity | Likelihood | Notes |
|------|----------|------------|-------|
| Integer overflow in `AngleToStep` / `String32ToInt` | Low | Low | EQ6 full-res is ~47 M, well inside `int` |
| GoTo accuracy regression if `_breakSteps` not scaled | Medium | High | **Must** be addressed in same change set |
| PEC period mismatch if `GetPecPeriod` not updated | Medium | High | PEC would run 4× faster or slower |
| Pulse-guide accuracy change | Low | Low | `_stepsPerSecond` auto-scales via `GetStepsPerRevolution` |
| Standard-command-set mounts broken | None | None | Guarded by `if (SupportAdvancedCommandSet && AllowAdvancedCommandSet)` |
| `GetHomePosition` sentinel error | None | None | Checked before division |

---

## 14. Verdict

The change is **feasible** and has **low overall risk**, provided:

- Items 1–9 in Section 11 are implemented together as an atomic commit.
- A feature flag is used for the initial release.
- Real-hardware testing is performed on the EQ6 (factor = 4) and at least one
  factor = 1 mount (to confirm no regression).

The entire change is **contained within `Commands.cs`** (about 12 targeted edits) with
automatic cascading to the rest of the application through the existing calibration factors.
No changes are required above the SkyWatcher driver layer.

---

*End of report — 2026-04-28 16:07*
