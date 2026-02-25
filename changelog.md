# Changelog

Generated: 2026-02-24  
Baseline comparison: `origin/main`

## Summary

- 5 tracked files changed versus remote.
- `386` insertions and `43` deletions.
- Focus areas: repurchase destination reliability, recurring diagnostics, delivery time multiplier targeting, UI repeat-once safety, and build output deployment path.
- Additional focus: recurring ASAP stability improvements (active delivery detection) and debug log spam reduction.

## File-Level Changes

### `AbsurdelyBetterDelivery.csproj`
- Updated post-build copy destination for the built DLL to the active Vortex mod path.

### `src/Patches/DeliveryApp_CreateDeliveryStatusDisplay_Patch.cs`
- Fixed delivery time multiplier application to target the exact `DeliveryInstance` passed into the patch.
- Prevents multiplier from being applied to the wrong delivery status entry.

### `src/Services/RepurchaseService.cs`
- Reworked destination handling to reduce misrouting and invalid fallback behavior.
- Added strict destination matching and explicit abort behavior when mapping cannot be resolved safely.
- Added destination code → dropdown index resolution aligned with game behavior (`GetPotentialDestinations` index mapping).
- Added robust destination property mapping with `PropertyName`/`Name` reflection fallback.
- Added duplicate dock occupancy safeguards for active deliveries.
- Expanded debug-mode diagnostics:
  - record/order context at execution start,
  - destination dropdown options and selected value,
  - destination property resolution details,
  - loading dock selection trace,
  - detailed order failure context (`canOrder`, `fitsInVehicle`, selected destination/dock).

### `src/Services/RecurringOrderService.cs`
- Added debug-mode trace points for ASAP recurring flow:
  - candidate scan count,
  - per-record skip reasons (cooldown, active delivery block, unavailable shop),
  - recurring execution context (record id, destination, dock, item count),
  - richer failure logs for recurring retries.
- Improved active delivery blocking logic for ASAP orders by using `DeliveryInstance` destination/dock data directly.
- Reduced recurring debug log spam:
  - failure cooldown skip logs are now throttled,
  - active-delivery-block skip logs are emitted on state change instead of every tick,
  - ASAP candidate count logs are emitted only when the count changes.

### `src/UI/DeliveryCardBuilder.cs`
- `RepeatOnce` now reorders from an immutable snapshot of the displayed card data.
- Avoids ordering from mutated live references when UI state has changed.
- Added explicit repeat-once debug log context (record id, destination, dock).

## Validation

- Debug build: successful.
- Release build: successful (`bin/Release/net6.0/AbsurdelyBetterDelivery.dll`).

## Notes

- Untracked local folder `.github/` exists in workspace and is not part of the tracked diff to `origin/main`.

---

## Commit Message (Suggested)

```text
fix: improve destination reliability and add richer debug diagnostics

- fix repeat-once safety by using immutable record snapshots
- align destination resolution with game dropdown index mapping
- improve destination property mapping for manor/hyland manor cases
- add debug-only logs for repurchase and recurring skip/failure reasons
- reduce recurring cooldown/active-block log spam with state-based throttling
- fix ASAP active-delivery detection to avoid false failure paths
- fix delivery-time multiplier targeting to the correct delivery instance
- update post-build copy path and regenerate release build
```

## End-User Changelog (Simplified)

- Fixed deliveries being sent to the wrong destination in repeat and recurring scenarios.
- Improved handling for Manor/Hyland Manor destination selection.
- Reduced silent failures: failed orders now provide clearer debug information when Debug Mode is enabled.
- Reduced debug log spam during recurring cooldown/blocked phases.
- Improved recurring ASAP behavior when a dock is currently occupied (clean skip instead of noisy fail loop).
- Improved repeat-once reliability so the clicked history card is used correctly.
- Fixed delivery time multiplier behavior to apply to the correct delivery.
- Included latest release build output.