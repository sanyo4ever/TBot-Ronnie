# Fleet fuel rejection implementation plan

**Goal:** Allow a later expedition cycle to send after ogamed explicitly rejects a launch for insufficient deuterium, without weakening uncertain-outcome protection.

**Scope:** The user's observed legacy ogamed response is HTTP 500 with `Status: error`, `Code: 500`, `Message: Not enough deuterium! (140026)`, and null `Result`. The pinned Go backend formats this message directly from a checkTarget/sendFleet errors entry. Keep this compatibility fix in C#; do not classify arbitrary HTTP 500 responses as rejections. Other/localized messages remain conservative until supported by evidence.

**Implementation:** Add explicit failure metadata to OgamedException when decoding the complete known envelope. FleetScheduler returns GenericError for that metadata and HTTP 500, preserving the existing HTTP 400 path, instead of recording an ambiguous dispatch. No inline POST retries. Expeditions retain their existing later-cycle retry scheduling. No live game actions, process changes, or config changes.

- [x] Verify clean branch and baseline: 561 offline C# tests pass on a5429e7.
- [x] Add an HTTP-boundary regression: fuel rejection followed by a later successful expedition must make exactly one POST per call.
- [x] Cover malformed/unknown/conflicting envelopes and lost responses: remain ambiguous and send no second POST.
- [x] Observe the new regression fail before modifying production code (expected GenericError 0, actual AmbiguousDispatch -5).
- [x] Implement the narrowly verified rejection classification; preserve the original error message in logs.
- [x] Run dispatch tests (51), full C# solution tests/build (588), release-policy tests (15).
- [x] Obtain an independent review and fix its finding: raw JSON must have explicit fields, strict types, no duplicates, and no contradictory extras. The additional envelope/syntax regressions failed before their fixes; final independent review approved with 51/51 dispatch cases.
- [ ] Commit and publish our own 3.5.1-beta2e, verify all release packages, and prepare a separate stopped local client.

Existing Windows Application Control blocks some local Go test executables. Go source and gitlink are unchanged; report this limitation without bypassing the policy and require release CI to run the complete Go suite. The user subsequently authorized our code and release only: do not create upstream PRs. Keep the running 3.5.1-beta2d process and its configuration untouched; prepare a sibling client for manual restart after release verification.
