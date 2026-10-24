# Expedition primary sharing implementation plan

> **For agentic workers:** Use superpowers:executing-plans for inline implementation; request a fresh whole-change review before merge.

**Goal:** Restore primary ship sharing across the actual pending dispatches from each origin.

**Architecture:** Forecast on a copy of the existing planner, then cap the one-flight composition by an integer primary allocation. Retain network and dispatch boundaries.

**Tech Stack:** C#, xUnit, .NET10 fork / .NET9 upstream, existing GitHub Actions.

**Spec:** ../specs/2026-10-24-expedition-primary-sharing-design.md

## Global constraints

No live game operations or running-client changes. Preserve escort-only sends, manual fleets, origin caps, priority and ambiguous-send protection. Go pin remains ddf29e10 in our release and e9f365f8 in the independent upstream PR. Our release is 3.5.1-beta2b.

## Review focus

- The planner marks a selected origin attempted before composition; its current send must still be included in the denominator.
- Forecasting must retain all cursor/round/local-success state without mutating it.
- Slot loss or an origin cap can shorten the forecast below the cycle's initial request.
- Integer sharing must neither overflow at long.MaxValue nor starve scarce escorts.
- A reserve must not modify the shared origin fleet or be subtracted twice.

## Task 1: Planner forecast and worker regression

Files: TBot/Workers/MissionPlanning.cs, TBot/Workers/ExpeditionsWorker.cs, TBot.Tests/ExpeditionsWorkerTests.cs, TBot.Tests/ExpeditionPrimarySharingTests.cs.

- [ ] Run clean baseline, restoring cached packages if needed.
- [ ] Add real-calculation worker tests for the observed 12/0/0 failure and boundary cases; confirm RED before production edits.
- [ ] Add `CountPendingDispatches(int originIndex, int requested)` to forecast after the current origin has been selected, including that selection and all subsequent simulated successes on a private planner copy.
- [ ] Make BuildExpeditionFleet accept the origin's pending count; preserve one-flight escort calculation and cap only PrimaryShip, using quotient/remainder sharing and an independent Ships copy.
- [ ] Bound requested forecast sends by current available expedition/general slots as well as the cycle's remaining request.
- [ ] Run focused and full .NET10 tests; commit the reviewed behavior separately from release metadata.

## Task 2: Independent upstream proposal

- [ ] Create a branch from exact Ronnie 521f43f3 and apply only the production/test changes, preserving .NET9, Go pin and release identity.
- [ ] Run focused and complete .NET9 tests; create a ready PR targeting fix/ogame-v13-tbot-3.4.7 and attach it to the chat.
- [ ] Obtain whole-change independent review of the fork and upstream adaptations; resolve material findings before release.

## Task 3: Our release and local handoff

- [ ] Advance fork-version.json revision to b and add public release notes, including the upstream proposal link and test evidence.
- [ ] Merge only our reviewed branch, run the existing manual release workflow for 3.5.1-beta2b and verify all CI jobs.
- [ ] Verify the source commit, Go pin, archive hashes and Windows package; publish our prerelease.
- [ ] Extract beside the current client; copy current explicit private configuration and profiles locally without copying live databases.
- [ ] Verify deployed binaries/metadata, unchanged old binaries/configuration and no new processes; provide manual restart path.
- [ ] Update the existing monitor's published baseline and new upstream PR while preserving notification/schedule settings.
