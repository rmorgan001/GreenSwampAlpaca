# SimPrecisionGoTo RA Axis Convergence — Improvement Analysis

**Report generated:** 2026-05-27 15:39 | **Last updated:** 2026-05-27 16:27
**Author:** GitHub Copilot (analysis requested by Andy)  
**Scope:** GEM and Polar alignment modes, RaDec slews only  
**File under analysis:** `GreenSwamp.Alpaca.MountControl\Mount.Motion.cs` — `SimPrecisionGoto()`

---

## 1. Current Implementation Summary

```
SimPrecisionGoto() — key behaviour (GEM / Polar, RaDec slew)
```

| Aspect | Current behaviour |
|--------|-------------------|
| **Prediction horizon** | Fixed `deltaTime = 75 ms` (initial), then replaced by `loopTimer.Elapsed.Milliseconds` each iteration |
| **RA axis target** | `simTarget[0] + 0.125 × deltaDegree[0]` — a constant fractional step toward target |
| **Dec axis target** | `simTarget[1] + 0.05  × deltaDegree[1]` — a constant fractional step toward target |
| **Target drift during slew** | **Not modelled** for GEM/Polar — the sky moves at the sidereal rate (~15.041″/s) while the axis is in motion, but `simTarget` is computed once at the start of each loop iteration and is not adjusted for sidereal drift over the slew execution window |
| **Sidereal rate access** | `SkySettings.SiderealRate = 15.0410671786691 °/hr` available in `SkySettings`; also exposed via `SkyPredictor._trackingRateProvider()` |
| **Max iterations** | 5 (hard limit) |
| **Loop settle wait** | Up to 3 000 ms per iteration |

### What the 0.125 scalar does

The factor `0.125` damps the commanded step to 1/8th of the measured error. This is a form of **proportional-only closed-loop control** with a fixed gain of 0.125. It provides stability but:

- Does not account for the fact that the sky **keeps moving** during the slew execution — so each iteration arrives at a point that is already stale by the time the axis settles.
- `deltaTime` (used as the next-iteration prediction horizon) is fed back as `loopTimer.Elapsed.Milliseconds`, which is the **measured past duration**, not a projection of the next iteration's duration. The two are identical only if iteration times are perfectly consistent.

---

## 2. Root Cause of RA Convergence Lag

For GEM and Polar modes the RA axis tracks sidereal motion. During the precision slew loop:

```
Elapsed per precision iteration ≈ 20–3 000 ms (poll interval + settle)
Sidereal RA drift per second    ≈ 15.041 arcseconds/s  = 0.004178°/s
Drift over a 100 ms iteration   ≈ 0.000418° ≈ 1.5″
Drift over a 500 ms iteration   ≈ 0.00209°  ≈ 7.5″
```

Precision of 2 encoder steps at 9 024 000 steps/rev ≈ `0.000080°` ≈ `0.29″`

So if the iteration takes just 200 ms the sky has drifted ~3″, which is already ~10× the target precision. The mount must then execute a second or third corrective iteration purely to catch up with sidereal drift — iterations that would be unnecessary if the commanded target had been projected forward.

---

## 3. Proposed Algorithms (RA axis, GEM/Polar only)

### Algorithm A — Sidereal-Drift Feedforward on Axis Position (Recommended — minimal change)

**Concept:** Before issuing the `CmdAxisGoToTarget` for Axis 1, advance the target by the expected sidereal drift over the estimated slew duration.

```
raTargetDeg_corrected = simTarget[0]
					  + SiderealRate_deg_per_ms × estimatedSlew_ms
```

where `SiderealRate_deg_per_ms = 15.0410671786691 / 3600000.0`.

The estimated slew duration can be seeded from `deltaTime` (the previous measured iteration time).

**Why it converges faster:**  
The axis is commanded to where the sky *will be* when the axis arrives, rather than where the sky *was* when the command was issued. The residual error on settling is then dominated by mechanical backlash and quantisation, not sidereal drift.

**Asymmetry consideration for GEM:**  
In GEM mode the RA axis direction (East vs West of meridian) determines whether drift adds or subtracts. The sign of the required correction is:

```
sign = (mountSide == MountSide.West) ? +1.0 : -1.0;
```

For Polar (fork) mount the sign is constant (+1 for Northern hemisphere, assuming standard mount orientation).

**Code sketch:**

```csharp
// GEM / Polar sidereal drift correction for RA axis
if (Settings.AlignmentMode != AlignmentMode.AltAz
	&& slewType == SlewType.SlewRaDec
	&& !axis1AtTarget)
{
	const double siderealDegPerMs = 15.0410671786691 / 3_600_000.0;
	double driftDeg = siderealDegPerMs * deltaTime;           // deltaTime now in ms
	double driftSign = (Settings.SideOfPier == PierSide.East) ? -1.0 : +1.0; // verify convention
	double raTargetCorrected = simTarget[0] + driftSign * driftDeg + 0.125 * deltaDegree[0];
	_ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1, raTargetCorrected);
}
```

**Expected gain:** Should reduce from typically 3–4 iterations to 1–2 iterations for targets at low-to-mid declinations.

---

### Algorithm B — Exponential Moving Average (EMA) of Iteration Duration

**Concept:** Replace the naive `deltaTime = loopTimer.Elapsed.Milliseconds` feedback with a smoothed estimate:

```
deltaTime_next = α × loopTimer.Elapsed.Milliseconds + (1 - α) × deltaTime_prev
```

where `α ∈ [0.3, 0.5]` is recommended (faster adaptation than a simple average, less noisy than raw measurement).

**Why it matters:**  
`loopTimer.Elapsed.Milliseconds` returns the *integer millisecond* portion of the elapsed time (`0–999`). This is not the same as `TotalMilliseconds`. If an iteration takes 1 050 ms, `Elapsed.Milliseconds` returns `50`, not `1050`. This is a latent bug that causes the predictor to badly underestimate slew time and therefore undercompensate for drift.

**Fix:** Replace:
```csharp
deltaTime = loopTimer.Elapsed.Milliseconds;
```
with:
```csharp
deltaTime = loopTimer.Elapsed.TotalMilliseconds;
```
and apply EMA smoothing.

**Expected gain:** More accurate sidereal drift compensation, especially for long-settling iterations (>1 s). Also fixes the integer truncation latent bug.

---

### Algorithm C — Adaptive Proportional Gain (Variable P Gain)

**Concept:** Rather than a fixed scalar of `0.125` on Axis 1, use a gain that reduces as the residual error shrinks:

```
gain = Clamp(|deltaDegree[0]| / threshold, g_min, g_max)
```

For example:
- When `|deltaDegree[0]| > 1.0°` → gain = 1.0 (large move, go straight to target)
- When `|deltaDegree[0]| < 0.01°` → gain = 0.25 (fine correction, gentle approach)

This avoids the 0.125 factor under-driving large residuals and over-damping tiny ones. Combined with Algorithm A (sidereal feedforward) the gain becomes responsible purely for removing the mechanical error component.

**Code sketch:**

```csharp
double gain1 = Math.Clamp(Math.Abs(deltaDegree[0]) / gotoPrecision[0], 0.25, 1.0);
_ = new CmdAxisGoToTarget(SimQueue!.NewId, SimQueue, Axis.Axis1,
		simTarget[0] + driftCorrection + gain1 * deltaDegree[0]);
```

---

### Algorithm D — Measured Slew-Time Model (Learning Feedforward)

**Concept:** Maintain a running estimate of how long Axis 1 takes to settle for a given commanded displacement. After each iteration record:

```
(|commanded displacement in degrees|, observed settle time in ms)
```

Use a simple linear fit: `settleMs ≈ k₀ + k₁ × |displacement_deg|`.

On the next iteration use the fitted model to predict settle time → drive the sidereal drift correction in Algorithm A more accurately than raw `deltaTime` feedback.

**Scope:** This is most valuable when the precision loop is called many times in a session (e.g. tracking with periodic corrections). For a one-off precision slew it adds no value. However, if `SimPulseGoto` is also modified to share the model, it pays dividends there too.

---

### Algorithm E — Single-Shot Prediction with Sidereal Integration (Most Aggressive)

**Concept:** Replace the iterative approach with a single well-predicted command. At the moment the command is issued, integrate the sidereal rate forward by the expected settle time:

```
t_arrive = t_now + estimatedSettleMs
ra_arrive = ra_now + SiderealRate_deg_per_ms × (t_arrive - t_now)
```

Issue `CmdAxisGoToTarget(Axis.Axis1, ra_arrive)` once. If the mount's settle time model is accurate (Algorithm D), this can converge in a **single iteration** for tracking targets at sidereal rate.

The residual second iteration then only deals with mechanical scatter (backlash, resonance), not sidereal drift.

---

## 4. Comparison Matrix

| Algorithm | Complexity | Iterations saved (est.) | Fixes latent bug | Notes |
|-----------|-----------|------------------------|-----------------|-------|
| **A — Sidereal drift feedforward** | Low | 1–2 | No (standalone) | ✅ Implemented (commit ccc5e67) |
| **B — EMA + TotalMilliseconds fix** | Very Low | 0–1 | **Yes** | ✅ Implemented (commit ccc5e67) |
| **C — Adaptive P gain** | Low | 0–1 | No | Complements A |
| **D — Learned settle-time model** | Medium | 0–1 (accumulated) | No | Valuable for session-long accuracy |
| **E — Single-shot integration** | Medium | 1–2 | No (needs D first) | Best long-term solution |

---

## 5. Recommended Implementation Order

1. ✅ **DONE — Algorithm B bug fix:** `loopTimer.Elapsed.Milliseconds` → `loopTimer.Elapsed.TotalMilliseconds`. Also fixed `SimPulseGoto` with the same bug. Also fixed the `deltaTime` seed from `75 * 0.001 = 0.075` ms to `75.0` ms, and added EMA smoothing (`α = 0.4`).

2. ✅ **DONE — Algorithm A feedforward:** Sidereal drift feedforward added to `Axis1` command for GEM and Polar RaDec slews. Uses `Settings.SiderealRate / 3_600_000.0` (degrees/ms). Sign is hemisphere-only (`+1` NH, `−1` SH). See Section 8 for the sign derivation.

3. **Medium term (Algorithm C):** Replace the fixed `0.125` gain with an adaptive one once A and B are proven stable.

4. **Long term (Algorithms D + E):** Introduce a lightweight per-session settle-time model so single-shot prediction becomes viable.

---

## 6. Key Values from Codebase

| Symbol | Value | Source |
|--------|-------|--------|
| `SiderealRate` | `15.0410671786691` arcsec/s | `SkySettings.cs:81`, `TrackingCommandProcessor.cs:48`, `SkyPredictor.cs:44` |
| `SiderealRate_deg_per_ms` | `15.0410671786691 / 3_600_000.0 = 4.178×10⁻⁶ °/ms` | derived |
| Precision threshold (2 steps at 9 024 000 steps/rev) | `≈ 0.000080°` ≈ `0.29″` | `Mount.Motion.cs:142` |
| Current initial `deltaTime` | `75 ms` | `Mount.Motion.cs:144` |
| Current RA gain scalar | `0.125` | `Mount.Motion.cs:179` |
| Current Dec gain scalar | `0.05` | `Mount.Motion.cs:182` |
| Max precision iterations | `5` | `Mount.Motion.cs:151` |
| Max iteration wait | `3 000 ms` | `Mount.Motion.cs:187` |

---

## 7. Notes on GEM vs Polar Differences

- **GEM (GermanPolar):** ~~Sidereal drift direction is pier-side-dependent.~~ **Corrected 2026-05-27 16:27:** The sign is **hemisphere-only** (`+1` NH, `−1` SH), identical to the Polar case. The East/West pier position changes the through-pole offset applied to `axis0` in `RaDecToAxesXyCore`, but does not invert the *rate of change* of `axis0` with respect to HA (sidereal time). `AxesAppToMount` applies the same hemisphere-dependent transform regardless of pier side. See Section 8 for the full derivation (Polar case; GEM follows the same logic).

- **Polar (Fork):** See detailed analysis in Section 8 below.

- **AltAz:** Already handled by `SkyPredictor.GetRaDecAtTime()` in the existing code — not in scope for this report.

---

## 8. Polar Mount — Normal vs ThroughPole Sign Analysis

**Reviewed 2026-05-27 15:39** following challenge that a Polar mount can occupy two distinct physical positions (`PierSideUI.Normal` and `PierSideUI.ThroughPole`) pointing at the same celestial coordinates, and the correction sign between these positions must be verified.

### The two physical positions

In app code, both positions arrive via the same path through `RaDecToAxesXyCore` (`Axes.cs`):

```csharp
// Convert RA to Hour Angle in degrees
axes[0] = 15.0 * (lst - axes[0]);     // HA in degrees
axes[0] = Range.Range360(axes[0]);

// Through-pole detection and adjustment
if (axes[0] > 180.0)
{
    axes[0] += 180;          // ThroughPole branch
    axes[1] = 180 - axes[1]; // Dec axis flipped
}
axes = Range.RangeAxesXy(axes); // axis0 → [0, 180)
```

This means:

| Physical position | HA range | App-space axis0 (before AxesAppToMount) |
|---|---|---|
| **Normal** | HA ∈ [0°, 180°] | `axis0 = HA` |
| **ThroughPole** | HA ∈ (180°, 360°] | `axis0 = HA + 180` mod 360, ranged → `HA − 180` |

In both cases, as time advances and HA increases at the sidereal rate, **app-space axis0 increases at the same rate** (δ axis0 / δ HA = +1). The ThroughPole branch adds a fixed 180° offset to HA before ranging — it does not invert the axis0 derivative with respect to time.

### Through AxesAppToMount

`AxesAppToMount` (`Axes.cs:79`) receives only the already-ranged app-space value and has no visibility of whether the original HA was in the Normal or ThroughPole half. The transforms it applies are:

| Mount / Hemisphere / PolarMode | Transform on axis0 | Encoder sign vs sidereal drift |
|---|---|---|
| Simulator, NH | `a[0] = a[0]` | **+** — same for Normal & ThroughPole |
| Simulator, SH | `a[0] = -a[0]` | **−** — same for Normal & ThroughPole |
| SkyWatcher, PolarMode.Right, NH | `a[0] = a[0]` | **+** — same for Normal & ThroughPole |
| SkyWatcher, PolarMode.Right, SH | `a[0] = -a[0]` | **−** — same for Normal & ThroughPole |
| SkyWatcher, PolarMode.Left, NH | `a[0] = a[0]` | **+** — same for Normal & ThroughPole |
| SkyWatcher, PolarMode.Left, SH | `a[0] = 180 − a[0]` | **−** — same for Normal & ThroughPole |

### Conclusion (confirmed correct)

The original report conclusion stands: **the correction sign does not change between Normal and ThroughPole positions for a Polar mount.**

The sign of the sidereal drift correction in mount encoder space is determined solely by:
1. **Hemisphere** (Latitude ≥ 0 vs < 0) — the primary determinant.
2. **PolarMode** (Left vs Right) for SkyWatcher SH only — affects the specific transform applied but still produces a consistent sign per configuration.

It is **not** affected by which of the two physical positions (Normal / ThroughPole) the mount is currently occupying, because the ThroughPole path in `RaDecToAxesXyCore` shifts the axis0 value by a constant 180° offset but does not invert the rate of change of axis0 with respect to sidereal time.

For the feedforward implementation, the sign determination logic is therefore:

```csharp
// True for NH simulator, SkyWatcher PolarMode.Right NH, SkyWatcher PolarMode.Left NH
// False (negate) for all SH configurations
bool positiveSign = Settings.Latitude >= 0;
double driftSign = positiveSign ? +1.0 : -1.0;
```

No check against `PierSideUI.Normal` / `PierSideUI.ThroughPole` is required.

---

*End of report*
