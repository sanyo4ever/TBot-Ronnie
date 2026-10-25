# Highscore proxy navigation implementation plan

> **For agentic workers:** Use superpowers:executing-plans to implement and verify this plan.

**Goal:** Open the focused player ranking from legacy galaxy links through the OGame 13 browser proxy.

**Architecture:** Normalize only legacy GET `page=highscore` requests without an explicit component on confirmed v13+ servers, before the shared Go page-fetch pipeline. Preserve `searchRelId`, all other query values, existing request scheduling and old-server behavior. No C# algorithm changes.

**Tech stack:** Go 1.23, offline HTTP RoundTripper and Echo tests; existing .NET 10 and seven-platform release workflow.

**Spec:** The reported proxy URL uses `page=highscore`; our existing v13 API compatibility code uses `page=ingame&component=highscore`. Restore browser access without changing the focus parameter or introducing new game requests; live rendering remains unverified.

## Constraints and review focus

- No game login, live navigation, fleet sends, client API calls or process changes during validation.
- Preserve old/unknown versions, explicit components, modern URLs, POSTs and unrelated routes.
- Clone query values before adaptation; keep filters, encoding and repeated parameters intact.
- Exercise public, manual transaction and browser proxy paths with a fake upstream transport.
- Standalone Ronnie Go PR must apply to `release/v13-beta10`, independently of pending stability PR2. Our release retains all previous patches.

## Task 1: Go route adaptation

- [x] Reproduce legacy requests failing expected modern-route assertions before production changes.
- [x] Add narrow adaptation in `pkg/wrapper/ogame.go`; add self-contained `highscore_navigation_test.go`.
- [x] Run focused regressions, then full Go tests and build on our pin and Ronnie's release base.
- [x] Obtain independent review, commit and publish separate Ronnie proposal (ogame.mod PR3).

## Task 2: Fork release and local preparation

- [x] Publish the tested Go fork commit, update TBot gitlink and revision `d`, and document validation.
- [ ] Run C# and release policy tests; merge own PR after all CI platforms pass.
- [ ] Dispatch `3.5.1-beta2d`, verify seven packages and checksums before publishing.
- [ ] Prepare a separate local folder using the currently running client's configuration and consistent history snapshot; retain guarded first-start synchronization.
- [ ] Update monitoring baseline and hand over links and restart instructions. Do not start or stop the client.
