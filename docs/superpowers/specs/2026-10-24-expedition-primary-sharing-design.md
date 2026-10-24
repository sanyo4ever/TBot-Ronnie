# Expedition primary ship sharing

Approved in the conversation on 2026-10-24. Restore automatic primary ship sharing while retaining Ronnie's dispatch ordering, origin caps and existing failure handling. The user's permission includes our release and an independent upstream PR; no further design approval is pending.

## Approach

Forecast the pending origin sequence using a private copy of ExpeditionDispatchPlanner. Count the chosen origin's pending dispatches, including the current one. Read only that origin's current ships and share its primary ships, after PrimaryToKeep, across its pending dispatches. Use integer division with the remainder allocated early, capped by the existing ideal fleet size. Preserve the existing one-flight escort selection and manual composition.

The alternatives were a frozen batch plan (loses adaptation to changing slots/origins) and dividing by all account slots (uses the wrong denominator for multiple origins). The chosen design retains the current dynamic planner and makes no additional network requests for the forecast.

## Boundaries

- Keep fair rounds, current active-load selection, random tie order and MaxExpeditionsPerOrigin.
- Keep the existing confirmed-failure fallback and ambiguous-dispatch stop; add no configuration repair, recovery loop or extra retry.
- Sending available escort with zero primary ships is intentional and must remain possible.
- Only undispatched ships can be rebalanced after an origin becomes unavailable.
- Do not mutate the shared origin Ships snapshot while applying a reserve.
- Do not alter Go, manual fleet counts, running processes or the current installation.
- Release 3.5.1-beta2b is based on Ronnie v3.5.1-beta2; deploy beside 3.5.1-beta2a with private configuration copied locally only.

## Verification

Exercise the real CalculationService in worker tests with fake game services: 12/3, remainders, fewer ships than fleets, zero primary, reserves, ideal cap, scarce escort, multiple origins, active limits, current-slot changes, manual mode, confirmed failures and ambiguous sends. Forecast tests prove no mutation and equivalence with actual planner dispatches, including pending selected origins and stale-snapshot protection. Run full .NET10 fork and .NET9 upstream suites, then the existing release CI and package verification. Never log in or send real fleets.
