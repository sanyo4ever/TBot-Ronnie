# Configurable manual mode timeout implementation plan

> **For agentic workers:** Use superpowers:subagent-driven-development to implement and review each task.

**Goal:** Make proxy manual mode inactivity configurable, defaulting to 120 seconds, and publish our next fork release with a prepared offline local installation.

**Architecture:** Read `General.ManualModeTimeoutSeconds` at account startup, default missing values to 120, validate positive integer seconds, and pass the value to ogamed through `--manual-mode-timeout-seconds`. Use the same duration on activation and renewal by proxied game GET/POST. Retain it across ogamed restarts. Config edits require restarting TBot; automatic file reload does not restart ogamed.

**Tech stack:** Existing .NET 10 TBot, Go 1.23.1 ogamed-tbot, GitHub Actions seven-platform release.

**Spec:** User request on 2026-11-09: configurable manual-mode timeout, two minutes by default, our release and parallel local preparation. Existing configuration and release conventions in docs/FORK.md apply.

## Constraints and decisions

- Preserve inactivity renewal, manual off and queued automation semantics. Background proxy requests can renew the timer; this is not a maximum session duration.
- Manual mode holds the bot task queue, including automated defense checks, until released.
- Use a positive integer number of seconds (maximum Int32). Invalid explicit values must report a configuration error; omission remains compatible.
- Our repositories only; no upstream PRs. Base Go on the published 5e921560 pin, not unreviewed remote main.
- Keep the running beta2e client untouched. No live game calls, fleet sends or second client. Private configuration/state is transferred only into the new installation.
- Work on dedicated feature branches in the clean existing checkout; Go and C# agents own disjoint files.

## Tasks and review focus

- [x] Go: offline regression tests for default/custom timeout, invalid values, activation/GET/POST renewal and release; implement shared duration configuration. Avoid an unrelated wrapper refactor.
- [x] C#: offline tests for old config, custom/invalid settings and process argument forwarding; implement and retain the startup option on restart.
- [x] Independently review both changes and startup/restart wiring; run relevant and full available tests. Report environment or baseline failures honestly.
- [ ] Publish exact Go commit on our branch, pin it in TBot; set revision f, document behavior and checks, commit reviewed TBot change to our main.
- [ ] Run release workflow; verify all seven archives, source SHAs, versions and checksums, then publish prerelease 3.5.1-beta2f.
- [ ] Download published Windows x64 archive, verify SHA256, prepare sibling installation with current settings and safe state migration. Do not start it.
- [ ] Report release link, config setting, tests, path and manual restart instructions. Update paused monitoring baseline without enabling it.
