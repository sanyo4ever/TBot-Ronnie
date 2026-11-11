# Support redirect and beta2g release plan

**Goal:** Prevent an official support SSO redirect from causing repeated login and blocking automated game reads; publish our tested release and prepare it locally without starting either program.

**Design:** Retain strict session-redirect classification. Adapt only GET `page=ingame&component=support`, HTTP 302/303, to the existing browser-side HTML navigation contract when Location is absolute HTTPS, exact `ogame.support.gameforge.com`, `/index.php`, no userinfo and no nonstandard port. Encode JavaScript with JSON encoding and fallback HTML with HTML escaping. Do not follow the support destination in ogamed. Other redirects and state-changing action retry rules remain unchanged.

**Evidence:** Offline diagnostic on Go `238a478d2a70080ed6827b0ab5b3a30791e8f3f5` reproduced expected support SSO being classified as ErrNotLogged. The incident log shows support 302/relogin/backoff and dependent 60-second read timeouts; its actual historical Location was not recorded, so equivalence to that exact response remains unconfirmed.

**Scope:** Go `pkg/wrapper/ogame.go`, new support redirect helper/tests; TBot gitlink, version metadata and release documentation. No configuration changes, live game actions, client start, upstream PR, or unrelated refactor. Preserve our previous release fixes and the current remote main history.

- [x] Write offline public-wrapper regressions; observe expected failure before changing production code.
- [x] Implement the narrow adapter and suppress false bad-status logging only for accepted support redirects.
- [x] Verify valid SSO/no relogin/queue release, manual mode, old 200 response, unsafe URL and caller rejection, real session redirects and existing discovery no-replay cases.
- [x] Run full Go tests/build, release-policy tests and C# CI checks; independently review the final patch.
- [ ] Publish Go to our main, pin exact SHA in TBot, set version `3.5.1-beta2g` (`3.5.1-beta2.g` package version), commit release notes and publish our TBot main.
- [ ] Run the release workflow; verify all seven archives, source pins, versions and SHA256 before publishing the draft prerelease.
- [ ] Prepare the new Windows folder alongside beta2f, privately preserve current configuration/profiles/state, verify the launcher and hashes, and leave both programs stopped.

**Review focus:** Strict URL allowlist; no SSO-key logging; no automatic external fetch; real logout behavior; no retry of state-changing fleet/discovery requests; no private data in packages; preserve user state and exact released binaries locally.
