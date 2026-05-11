# Rate Command Deduplication Assessment

Timestamp: 2026-05-11 08:46
Requested by: Andy

## Baseline
- Build status before analysis: **SUCCESS (0 errors)**.

## Scope Reviewed
- `GreenSwamp.Alpaca.Server/TelescopeDriver/Telescope.cs`
  - `DeclinationRate` property setter (around lines 477-491)
  - `RightAscensionRate` property setter (around lines 776-791)
- `GreenSwamp.Alpaca.MountControl/Mount.cs`
  - `SetRateDec(double)` (around lines 657-672)
  - `SetRateRa(double)` (around lines 673-688)

## Current Behavior
1. ASCOM/Alpaca `PUT declinationrate` and `PUT rightascensionrate` always flow through the Telescope property setters.
2. Each setter currently:
   - validates capability/range/tracking mode,
   - updates `_mount.RateDecOrg` or `_mount.RateRaOrg`,
   - always calls `_mount.SetRateDec(...)` or `_mount.SetRateRa(...)`.
3. Mount-level `SetRateDec` / `SetRateRa` currently always execute hardware-facing rate actions (or queue commands in AltAz mode), even if the incoming rate is unchanged.
4. Result: repeated requests like `DeclinationRate = 0` followed by `DeclinationRate = 0` still trigger command processing and logging work.

## Assessment of Required Changes

### 1) Add no-change guard in Telescope driver setters (recommended primary change)
- In `Telescope.DeclinationRate.set` and `Telescope.RightAscensionRate.set`:
  - keep existing validation logic unchanged,
  - compare requested value with stored original value (`_mount.RateDecOrg` / `_mount.RateRaOrg`),
  - if unchanged, return early without calling mount rate methods.
- This directly uses the existing `RateRaOrg` / `RateDecOrg` fields as the de-dup cache in ASCOM units, matching your proposed re-purpose.

### 2) Optional defensive guard in Mount layer (recommended for robustness)
- In `Mount.SetRateDec(double)` and `Mount.SetRateRa(double)`:
  - compare new degree-rate against current internal rate (`RateDec` / `RateRa`),
  - return early if unchanged.
- This protects against redundant calls from any caller, not just Telescope.cs.

### 3) Comparison strategy
- Prefer epsilon comparison instead of exact `==` for doubles.
- Suggested approach:
  - Telescope layer: compare ASCOM values (`arcsec/s` for Dec, `sidereal-sec/s` for RA) with small epsilon.
  - Mount layer: compare converted degree rates with small epsilon.
- This avoids false positives caused by floating-point conversion noise.

## Behavioral Impact
- Positive:
  - Fewer redundant queue/hardware calls.
  - Less command churn and log noise.
  - No API contract change (setters still accept same values and validations).
- Neutral/expected:
  - Repeated identical writes become no-op operations.
- Risk:
  - If epsilon is too large, near-equal but intentional tiny rate adjustments could be skipped.

## Suggested Minimal Implementation Plan
1. Add early-return no-change check in `DeclinationRate` setter using `_mount.RateDecOrg`.
2. Add early-return no-change check in `RightAscensionRate` setter using `_mount.RateRaOrg`.
3. (Optional) Add guard in `Mount.SetRateDec` and `Mount.SetRateRa`.
4. Build and run targeted telescope rate tests (or issue repeated rate calls manually) to verify:
   - first call applies,
   - second identical call is skipped,
   - changed value still applies.

## Validation Scenarios
- `DeclinationRate: 0 -> 0` should not trigger `_mount.SetRateDec` on second call.
- `RightAscensionRate: 0 -> 0` should not trigger `_mount.SetRateRa` on second call.
- `DeclinationRate: 0 -> 0.1 -> 0.1` should trigger only once for `0.1`.
- `RightAscensionRate` while non-sidereal should still throw as today.

## Recommendation
Proceed with the Telescope-layer de-duplication first (smallest and safest change), using `RateRaOrg` and `RateDecOrg` as the canonical last-requested values. Optionally add Mount-layer guards as defense-in-depth.
