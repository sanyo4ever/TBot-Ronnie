# Expedition SecondaryShip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore configurable secondary ships without losing primary sharing, then deliver reviewed PR6 and release 3.5.1-beta2c.

**Architecture:** Keep the current planner. Extend only automatic fleet composition with origin-local secondary sharing and legacy ratio/minimum settings, before the existing feasibility check and dispatch.

**Tech Stack:** C#, xUnit, .NET10 fork / .NET9 upstream, existing Go pin and GitHub release workflow.

**Spec:** docs/superpowers/specs/2026-10-25-expedition-secondary-sharing-design.md

## Global Constraints

- No live game calls, client API, process restart, Go pin change or private configuration publication.
- Existing escort-only behavior, manual composition and planner policies remain.
- PR6 keeps upstream .NET9, Go e9f365f8 and original base; fork keeps .NET10, Go ddf29e10 and updates revision c.

## Review Focus

- Scarce/remainder secondary inventory must remain fairly distributed.
- Ratio overflow/fractional arithmetic must not overdraw ships.
- Same primary/secondary and overlapping escort types must not duplicate reserved inventory.
- Positive minimum must prevent scheduler dispatch when unmet; zero primary with zero minimum still allows escorts.
- Missing legacy settings and manual mode must remain compatible.

## Task 1: Worker restoration

Files: TBot/Workers/ExpeditionsWorker.cs; create TBot.Tests/ExpeditionSecondarySharingTests.cs, reusing PrimarySharingScenario.

- [ ] Baseline full suite passes (528 tests).
- [ ] Write scenario tests for the spec; run and record real failures from omitted secondary allocation.
- [ ] Restore secondary composition; keep helper logic in the worker, no API changes.
- [ ] Run focused and complete fork tests. Request one fresh-context review and fix material findings with tests.
- [ ] Commit the reviewed core patch.

## Task 2: Upstream and fork integration

- [ ] Cherry-pick core into the existing PR6 branch after verifying remote head; run full .NET9 suite.
- [ ] Push without force and update PR6 description/title to cover primary and secondary restoration.
- [ ] Update fork metadata/README/release notes, run version tests, open our PR and merge only after successful CI.

## Task 3: Release and handoff

- [ ] Dispatch existing workflow from exact merged main for 3.5.1-beta2c; verify tests and all seven archives, source pins and hashes before publishing.
- [ ] Publish prerelease, independently download public win64 asset and verify its hash.
- [ ] Prepare separate beta2c folder with CURRENT beta2b configs/profiles and consistent state backup; guarded first-launch importer finalizes history once after shutdown.
- [ ] Verify installed bytes, source nonmutation, config equality and import guard; update monitor baseline and provide restart instructions with links.
