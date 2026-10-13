# Releasing

Every package carries the same version (lockstep SemVer). A release is one tag.

## One-time setup (nuget.org Trusted Publishing)

Done once by the nuget.org account owner; nothing is stored in the repository.

1. On nuget.org: **Account → Trusted Publishing → Add policy**: repository owner `XtremeCRM`,
   repository `Xtreme.Components`, workflow `publish.yml`, environment `nuget`.
2. On GitHub: **Settings → Environments → New environment** named `nuget` (add required
   reviewers if a manual gate is wanted).
3. On GitHub: **Settings → Variables → Repository variable** `NUGET_USER` = the nuget.org
   account name that owns the policy.
4. Optional: request the `Xtreme.Components` ID prefix reservation on nuget.org. Publishing
   does not depend on it; it only stops squatters.

## Cutting a release

1. Bump `<Version>` in `Directory.Build.props`.
2. Move the `[Unreleased]` notes in `CHANGELOG.md` into a `## [<version>] - <date>` section
   and update the compare links at the bottom.
3. Open a PR with those two changes; let CI go green; merge to `main`.
4. Tag the merge commit and push the tag:

   ```bash
   git tag v<version> <merge-sha>
   git push origin v<version>
   ```

`publish.yml` then: validates (tag on `main`, tag matches `<Version>`, CHANGELOG section
exists, CI green for that commit) → packs every project with `IsPackable` → attests build
provenance → exchanges the OIDC token for a short-lived NuGet key → pushes `.nupkg` and
`.snupkg` → creates the GitHub release with the CHANGELOG section as notes.

A `workflow_dispatch` run with `dry-run` left on validates and packs only, for checking a
release before tagging it.

## Pre-release policy

- `0.x-preview.N` until the Docking layout document and the Scheduler item model stop changing
  shape. Breaking changes are allowed but every one gets a **Breaking** entry in the CHANGELOG
  with a migration note.
- `1.0.0` adds `PublicAPI.Shipped.txt` baselines so later breaks are a visible diff.
