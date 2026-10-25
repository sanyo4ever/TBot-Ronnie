# Restore SecondaryShip in automatic expeditions

The user explicitly requested restoring the existing SecondaryShip settings, retaining primary sharing, tests, an update to Ronnie PR6, our next release and a side-by-side local installation. This is a narrow restoration with existing setting semantics, not a new dispatch policy.

## Approach

Restore secondary allocation in BuildExpeditionFleet, after the actual primary count is known. Reuse the selected origin's existing pending-dispatch forecast. Share its remaining secondary inventory by integer ceiling division; cap additions by floor(actual primary count * SecondaryToPrimaryRatio). Retain MinSecondaryToSend as a minimum for these additional secondary ships: if unmet, skip the origin through the current empty-fleet path, without invoking the scheduler. Missing/Null SecondaryShip disables the feature. ManualShips bypasses automatic composition.

Alternatives rejected: fixed manual composition loses automatic sharing; using total account slots ignores origin fairness and limits; restoring the old entire worker would undo current safety fixes. Both primary and secondary inventory remain local to the origin. PrimaryToKeep continues to affect only primary stock. Same-type primary/secondary configuration must not duplicate primary or consume its reserve. Invalid/nonmovable secondary or invalid ratio must fail preparation with a clear message. Default missing ratio/minimum values are the public defaults 2/0. Count any already selected escort against stock before adding secondary ships, so an overlapping escort type cannot cause overdraw. Preserve existing escort-only behavior when no primary is available and the secondary minimum is zero.

## Constraints and validation

No Go change, new requests, retry policy, configuration repair or client process changes. Existing fair rounds, origin caps, live-slot checks and confirmed/ambiguous failure behavior remain. Use decimal ratio arithmetic with saturation at Int64.MaxValue before conversion; no float rounding or overflow-driven positive dispatch.

Tests use the existing offline worker scenario and real CalculationService. Cover sufficient/scarce/remainder secondary stock, ratio cap/fractions/zero, minimum rejection and other-origin fallback, reserve/nonmutation, zero primary, disabled/missing fields, same-type settings, overlapping escorts, manual mode, multiple origins/caps and reduced slots. Verify all tests on .NET10 fork and unchanged .NET9 upstream base. Review the complete patch before publication. Reuse the previously tested one-time final history synchronization for local handoff after the user stops beta2b.
