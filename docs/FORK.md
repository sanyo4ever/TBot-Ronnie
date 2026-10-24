# Maintaining our TBot fork

The independent maintenance line is `main` in [sanyo4ever/TBot-Ronnie](https://github.com/sanyo4ever/TBot-Ronnie) and [sanyo4ever/ogame.mod](https://github.com/sanyo4ever/ogame.mod). Ronnie is the primary upstream source for this release line. The reviewed TBot base is [ronnie32/TBot v3.5.1-beta2](https://github.com/ronnie32/TBot/releases/tag/v3.5.1-beta2), commit `521f43f3bb56aa01f847a6b56a89e46b774e0016` on `fix/ogame-v13-tbot-3.4.7`. Its reviewed Go gitlink is `e9f365f803e15f937916ef152462f231b8676512` on [ronnie32/ogame.mod](https://github.com/ronnie32/ogame.mod), branch `release/v13-beta10`.

Integrate Ronnie's history with our previous maintenance history and preserve applicable fixes; do not reset our main to another fork's branch. The new TBot repository is a direct GitHub fork of Ronnie. The former sanyo4ever/TBot belongs to a different GitHub fork network; its maintenance history is retained here, and it is now the legacy line. Changes proposed to Ronnie must remain compatible with his .NET9 projects and exclude our fork version/CI policy. Our independent fork builds use .NET10 and the checked-in `global.json`; the reviewed Go baseline is Go1.23.1 and CI reads its version from `ogame/go.mod`.

## Development

```sh
git clone --recurse-submodules --branch main https://github.com/sanyo4ever/TBot-Ronnie.git
cd TBot-Ronnie
git switch -c fix/short-description
python3 -m unittest discover -s tools/tests -v
dotnet test TBot.sln -c Release
cd ogame
go test -count=1 ./...
go build ./...
```

Open fork changes against **our main**, review the diff and wait for CI before merging. The regression tests use synthetic fixtures and temporary local files. No live game credentials are required. CI tests both projects and builds seven self-contained ZIP packages: Windows x64/x86, Linux x64/ARMv7/ARM64, and macOS x64/ARM64.

For backend fixes, review and merge the Go change first, then update TBot's `ogame` gitlink to that exact published commit. The submodule URL and suggested branch are ours; the checked-in SHA determines every build. Do not use `git submodule update --remote` in release CI. Keep the existing Go module path and MIT attribution.

```sh
git submodule sync --recursive
git -C ogame fetch origin main
# Replace REVIEWED_GO_COMMIT with the exact reviewed, published commit.
git -C ogame checkout --detach REVIEWED_GO_COMMIT
git add ogame
git commit -m "build: update pinned ogamed backend"
```

The bounded system-discovery route is a paired C#/Go contract: `/bot/planets/:planetID/send-system-discovery-bounded`, with `galaxy`, `system`, a positive `maxFleets` bound and explicit reserved `positions`. Package TBot with its pinned backend; an older backend may lack this route. A route failure must not fall back to an unbounded action. Missing or ambiguous movement evidence does not prove a send, and partial outcomes must retain their uncertainty. Keep the legacy route available only according to its separately reviewed compatibility behavior.

## Version policy

The public version is the reviewed author release followed by one lowercase fork letter. `fork-version.json` records the upstream repository, release/commit, source branch/commit and our revision. The current release identity is `3.5.1-beta2a`. The CLI rejects requested labels that contradict this checked-in policy.

For this beta-derived release, NuGet Version is `3.5.1-beta2.a`; InformationalVersion, console title, GitHub tag and ZIP names use `3.5.1-beta2a`; numeric AssemblyVersion and FileVersion inputs are `3.5.1`. The .NET informational attribute can also include the built commit after `+`; the title omits that build metadata. Existing stable-base behavior remains `3.4.6b` publicly and `3.4.6-b` for NuGet.

CI obtains all metadata from `tools/release_version.py` and passes it to both the C# regression test build and publication. Ordinary CI uses an identifiable `-ci.RUN` public suffix and creates no release tag. The title tests exercise the compiled application's actual informational metadata, including our fork identity.

When Ronnie publishes a new release, review its actual release branch, tag, Go gitlink and merged changes before importing anything. After integration and verification, update the recorded upstream version/commit and restart the letter at `a`. Preserve old Git history and tags; do not rewrite main or automatically adopt upstream changes.

## Creating our releases

1. Commit the intended TBot and Go changes on our main. Require green tests, independent integration review and all seven packages. Check that `fork-version.json`, project metadata, exact Go gitlink and `docs/releases/VERSION.md` agree.
2. Open [Actions / Fork CI and release](https://github.com/sanyo4ever/TBot-Ronnie/actions/workflows/build.yml), choose **Run workflow**, select **main**, and enter `3.5.1-beta2a`. A manual release request from any other branch fails validation.
3. The workflow runs version-policy, C# and Go tests, verifies the pinned Go checkout, and builds all seven packages. Only after every package succeeds does it atomically create a new tag at the built TBot commit, then create a draft release. Existing tags fail instead of being overwritten. If draft creation fails after tag reservation, inspect the run/tag before retrying with a new revision.
4. A beta-derived public label contains an upstream prerelease suffix; the workflow passes `--prerelease` explicitly so the draft remains a GitHub prerelease. Review the draft, exact source SHAs, all ZIPs and `SHA256SUMS` before publishing it from [our Releases page](https://github.com/sanyo4ever/TBot-Ronnie/releases).

Every ZIP includes `BUILD-INFO.txt`, exact TBot and Go source SHAs, Go gitlink, public/NuGet/numeric versions, platform and toolchain versions, plus `fork-version.json`. Ordinary pushes, PRs and upstream tags do not create releases. Build success and synthetic tests do not certify current live-game behavior. Do not describe a planned check as completed in release notes.

Local installation is a separate offline preparation step. Keep the previous folder for rollback, verify archive hashes, and copy private settings/profiles privately. Review new defaults conservatively without silently enabling optional actions. The operator stops the old client and starts the new one manually; do not start a second client for the same account. Never include private settings or credentials in source commits or release artifacts.

## Upstream updates

Track [Ronnie's TBot release branch](https://github.com/ronnie32/TBot/tree/fix/ogame-v13-tbot-3.4.7), release tags and the Go gitlink, then the corresponding [Go release branch](https://github.com/ronnie32/ogame.mod/tree/release/v13-beta10). Do not substitute Ronnie's stale Go master. Continue checking official OGame notices and original upstream changes for compatibility/security fixes.

Review incoming diffs in integration branches and resolve overlaps with our maintenance fixes before merging into our main. Existing original-upstream proposals remain independent. Ronnie can review and merge our PRs on his schedule; our tested fork releases do not require those merges. No automatic upstream merge or update of a running client is enabled.
