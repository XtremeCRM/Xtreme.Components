# Architecture and packaging decisions

Originally the XU-880 decision record in XtremeCRM.Uno (`docs/reusable-controls-library.md`), copied here at extraction time. Paths that mention `ClientApps/Xtreme.Components` describe where the code lived before this repository existed.

## TL;DR

| Decision | Choice |
|---|---|
| Source | New **public** GitHub repo `XtremeCRM/Xtreme.Components`, MIT, fresh initial commit (no history carried over) |
| Packages | **One package per family**, all on **one lockstep version**; `Xtreme.Components` is a meta-package that pulls everything |
| First wave | Scheduler, Docking, Cards (CardDocker + SectionCard), **and** the zero-coupling Primitives |
| Feed | **nuget.org**, Trusted Publishing (OIDC) on a `v*` tag, `0.x-preview` until the API settles |
| Consumption by XtremeCRM | `PackageReference` pinned in `Directory.Packages.props`; one MSBuild property flips to a `ProjectReference` into a sibling checkout for same-day edits |
| Gallery | New Components-only gallery app (desktop + WASM) on GitHub Pages; `Xtreme.Harness` stays the internal workshop |
| Hard rule | No `ProjectReference` to any XtremeCRM assembly, no `ServiceLocator`, no app DTOs, no `IUiPreferenceService`. App behaviour enters through interfaces the consumer implements |

## Why these choices

**Per-family packages, one version.** The three families share no code today. One
assembly makes a scheduler consumer carry 4,300 lines of docking plus Win32 interop,
and a dock bug fix bumps the scheduler. Independent version lines were rejected
because they create a compatibility matrix nobody will maintain; lockstep keeps
"which versions go together" trivial, the way `Microsoft.Extensions.*` does it.
A `Core` package exists only for code that is genuinely shared (see
[Package map](#package-map)); it is not created speculatively.

**Public repo from the start.** The goal is sharing. A private monorepo means
`RepositoryUrl` and SourceLink point at something outsiders cannot read, the
gallery cannot be hosted from it, and every outside question becomes a copy-paste.
A public repo on the Free-plan org also gets unlimited Actions minutes, so the
pack/publish workflow costs nothing. The cost is a two-repo dev loop, mitigated by
the local switch below.

**Fresh initial commit.** The folder's 78 commits scanned clean, but the library
grew up inside a private CRM repo and the blame history is not worth a leak review
on every extracted file. The monorepo keeps the history; the public repo starts at
"extracted from XtremeCRM.Uno on <date>".

**nuget.org, not GitHub Packages.** GitHub Packages requires a token even to restore a
public package, which defeats "friendly and standard". nuget.org with Trusted
Publishing needs no stored API key. The `Xtreme.Components` id is unclaimed and no
one holds the `Xtreme.` prefix (checked 2026-10-13).

**Primitives in the first wave.** They are small, already decoupled, and the
`Themes` story they force (see [Theming](#theming)) is the same one the families
need, so solving it once is cheaper than twice.

**Separate gallery, not a split Harness.** Harness references `Xtreme.Shell` and
mocks the API with Bogus data; it is the workshop for Shell controls and should stay
that. A gallery that only references the packages is also the honest proof that the
packages work without the app.

## Repository layout

```
Xtreme.Components/                 (public repo)
  Directory.Build.props            version, NuGet metadata, SourceLink, deterministic build
  Directory.Packages.props         central package versions (Uno.Sdk pinned via global.json)
  global.json                      Uno.Sdk version
  src/
    Xtreme.Components.Core/        shared theme tokens + gesture primitives (only if needed)
    Xtreme.Components.Scheduler/
    Xtreme.Components.Docking/
    Xtreme.Components.Cards/
    Xtreme.Components.Primitives/
    Xtreme.Components/             meta-package (no code, depends on all of the above)
  samples/
    Xtreme.Components.Gallery/     desktop + browserwasm, Skia renderer; hosted on GitHub Pages
  tests/
    Xtreme.Components.Tests/       headless unit tests (models, layout solvers, serializers)
  docs/                            one page per family + this document
  build/
    nuget-readme.md                nuget.org cannot render repo-relative links
  .github/workflows/
    ci.yml                         build + test + pack 0.0.0-ci.<run> on every PR
    publish.yml                    tag v* -> validate -> pack -> attest -> nuget.org -> GitHub release
  CHANGELOG.md                     Keep a Changelog format, one section per version
  LICENSE                          MIT
```

The workflow discovers any `src/<Name>/<Name>.csproj` with `IsPackable != false`, so
adding a package never needs a workflow edit (the UnoDock pattern).

## Package map

```
Xtreme.Components (meta)
 ├── Xtreme.Components.Scheduler ──> Primitives ──> Core
 ├── Xtreme.Components.Docking   ─────────────────> Core
 ├── Xtreme.Components.Cards     ─────────────────> Core
 ├── Xtreme.Components.Primitives ────────────────> Core
 └── Xtreme.Components.TableView ──> Primitives (+ WinUI.TableView)
```

| Package | Contents | Notes |
|---|---|---|
| `Xtreme.Components.Core` | `Xc*` theme token dictionary (`Themes/Generic.xaml`) and the `ActualTheme`-driven surface-brush helper that is today's internal `DockTheme` | Justified by the census: CardDocking already calls `DockTheme`, and every family consumes the tokens. No controls live here |
| `Xtreme.Components.Primitives` | Input, layout and list helpers: masked text, time and date pickers, the multi-month date navigator (moved out of Scheduler), paging, panels, toast, action bar, chip picker, copy button, overlay glyph | Sits under Scheduler because the date navigator is a primitive the Scheduler uses |
| `Xtreme.Components.Scheduler` | Day / Week / Month / Agenda / Planner views, drag-move/resize, click-to-create, external drag-drop, per-resource time zones, recurrence indicators | Namespace `Xtreme.Components.Scheduler.*` already in place |
| `Xtreme.Components.Docking` | `DockHost`, `DockItem`, `DockItemHeader`, `DockSplitter`, auto-hide, layout document v5 serializer, `IDockPersistence`, `IDockPlatformServices` | OS-window tear-out ships as the interface plus a desktop reference implementation; consumers on WASM/mobile get in-surface behaviour |
| `Xtreme.Components.Cards` | `CardDockerControl` + models, `SectionCard` / `SectionCardTab` | `SectionCard` moves out of Shell; `GeneralInfoControl` and all card bodies stay in the app |
| `Xtreme.Components.TableView` | `RowFlagDecorator` + `RowFlag` | Separate because it depends on the third-party `WinUI.TableView` package; Primitives stays dependency-free |
| `Xtreme.Components` | No code; `PackageReference` to all of the above | The "give me everything" entry point |

Dependency direction is strictly downward. Families never reference each other;
anything two families want lives in `Core`, and controls the families build on live
in `Primitives`.

## Library conventions

Adopted from Uno.Toolkit, CommunityToolkit and UnoDock, trimmed to what we need:

- **`Sdk="Uno.Sdk"` library project**, `UnoSingleProject`, `GenerateLibraryLayout=true`,
  **`UnoFeatures` empty** (only an app head sets features).
- **`net10.0` only.** The Skia renderer consumes one assembly on every head. Adding
  platform TFMs pulls a platform runtime into the library and conflicts with the head
  (UNOB0017). Revisit only if a native-WinUI consumer appears; UnoDock dual-targets
  for that reason and we do not have it.
- **No `ServiceLocator`, no DI container reach.** Services arrive by constructor,
  dependency property, or an interface the consumer registers (`IDockPersistence`,
  `IDockPlatformServices`).
- **No app types.** No DTOs, no `IUiPreferenceService`, no `IXtremeClient`. If a
  control needs data it takes a model the library defines.
- **Templated controls use lightweight styling.** Every colour, brush and font token is
  a `ThemeResource` key the consumer can override in `App.xaml`; defaults ship in each
  package's `Themes/Generic.xaml`. Code-only palettes (`SchedulerPalette`, `DockTheme`)
  are migrated per family as follow-up work and do not gate packaging.
- **Namespaces match package ids**: `Xtreme.Components.<Family>.*`.
- **Public API is deliberate.** `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`
  via `Microsoft.CodeAnalysis.PublicApiAnalyzers`, so a breaking change is a visible
  diff, not a surprise at 1.0.
- **Nullable + ImplicitUsings on**, `LangVersion latest`, analyzers at default.
- **`README.md` per package** (packed), plus a `docs/<family>.md` with screenshots from
  the gallery.

### Theming

The Shell's `XtremeListResources.xaml` (77 keys) and `XtremeThemeResources.xaml`
(128 keys) are the app's design system and stay in the app. The library defines its
**own** token set with neutral names (`XcAccentBrush`, `XcSurfaceBrush`,
`XcFontSizeBody`, …) and defaults derived from the WinUI theme brushes, so a
control looks right in a bare Uno app. XtremeCRM maps its tokens onto them once in
`App.xaml` (`<SolidColorBrush x:Key="XcAccentBrush" Color="{ThemeResource XtremeAccent}" />`
and so on), and the existing `UiTextSizeService` keeps overriding font tokens at
runtime exactly as it does today. High-contrast overrides follow the same shape:
the library ships a `HighContrast` dictionary for its own keys; the app's two HC
files map onto them.

## Versioning and release

- **SemVer, lockstep.** `Directory.Build.props` owns `Version`; every package in a
  release carries the same number. Start at `0.1.0-preview.1`. `1.0.0` when the
  Docking layout document and the Scheduler item model stop changing shape.
- **CHANGELOG.md** in Keep a Changelog format. The publish workflow refuses a tag
  whose version has no CHANGELOG section.
- **CI on every PR**: build (Windows runner), test, pack `0.0.0-ci.<run>` as an
  artifact so a consumer can try a PR build.
- **Publish on tag `v*`**: validate (tag is on `main`, that SHA has a green CI run,
  CHANGELOG section present) → pack on `windows-latest` → build-provenance
  attestation → push to nuget.org via **Trusted Publishing** (OIDC, no stored key)
  → GitHub release with the packages attached.
- **Symbols**: `snupkg` + SourceLink, deterministic build, `EmbedUntrackedSources`.
- **Breaking changes** before 1.0 are allowed but every one gets a CHANGELOG
  "Breaking" entry and a migration note.

## Consuming a control (the outside developer's view)

```bash
dotnet add package Xtreme.Components.Docking --prerelease
```

```xml
<!-- App.xaml: optional, override any token -->
<SolidColorBrush x:Key="XcAccentBrush" Color="#0A84FF" />
```

```xml
<Page xmlns:dock="using:Xtreme.Components.Docking">
  <dock:DockHost PersistenceKey="Main">
    <dock:DockHost.Document> ... </dock:DockHost.Document>
    <dock:DockItem Title="Explorer" Edge="Left"> ... </dock:DockItem>
  </dock:DockHost>
</Page>
```

Optional seams, each an interface with a no-op default so nothing is required:

| Seam | Interface | What the app supplies |
|---|---|---|
| Layout persistence | `IDockPersistence` | Where the layout document is saved (local settings, server, nothing) |
| OS windows | `IDockPlatformServices` | Float-to-window, drag tracking, window pool; desktop reference implementation ships in the package, other heads fall back to in-surface |
| Card layout persistence | `ICardLayoutPersistence` | Same shape as docking |

The gallery app is the living reference: every control page shows the XAML and the
seam registrations side by side.

## How XtremeCRM consumes it

Shell and Harness switch from `ProjectReference` to a `PackageReference` pinned in
`ClientApps/*/Directory.Packages.props`. For same-day work on a control and its
consumer, one property flips the reference:

```xml
<!-- ClientApps/Directory.Build.props (shared) -->
<PropertyGroup>
  <XtremeComponentsLocalRoot Condition="'$(XtremeComponentsLocalRoot)' == ''">$(MSBuildThisFileDirectory)..\..\Xtreme.Components\src\</XtremeComponentsLocalRoot>
  <UseLocalXtremeComponents Condition="'$(UseLocalXtremeComponents)' == '' and Exists('$(XtremeComponentsLocalRoot)')">true</UseLocalXtremeComponents>
</PropertyGroup>
```

```xml
<!-- Xtreme.Shell.csproj -->
<ItemGroup Condition="'$(UseLocalXtremeComponents)' != 'true'">
  <PackageReference Include="Xtreme.Components.Scheduler" />
  <PackageReference Include="Xtreme.Components.Docking" />
  <PackageReference Include="Xtreme.Components.Cards" />
  <PackageReference Include="Xtreme.Components.Primitives" />
</ItemGroup>
<ItemGroup Condition="'$(UseLocalXtremeComponents)' == 'true'">
  <ProjectReference Include="$(XtremeComponentsLocalRoot)Xtreme.Components.Scheduler\Xtreme.Components.Scheduler.csproj" />
  ...
</ItemGroup>
```

Rules of the loop:

- A sibling checkout at `source\repos\Xtreme.Components` turns the local switch on
  automatically; CI never has one, so CI always restores the pinned package.
- A Shell change that needs a library change ships in two PRs: library first
  (tag, publish preview), then Shell bumps the pin. The local switch is for
  developing them together, not for merging them together.
- Hot Reload and the Harness keep working under the local switch because it is a
  plain `ProjectReference`.
- Worktrees: the sibling path is resolved from `ClientApps/`, so every XtremeCRM
  worktree sees the same local checkout. Point `XtremeComponentsLocalRoot` at a
  different checkout per silo if two features need different library branches.

## Control census and wave plan

Coupling check run 2026-10-13 against `ClientApps/Xtreme.Shell` and
`ClientApps/Xtreme.Components`. Findings that shape the packages:

- **Scheduler and Docking share nothing.** No type crosses those folders; the
  `Utilities/` folder is Scheduler-only.
- **CardDocking depends on Docking's internal `DockTheme`** (the `ActualTheme`-driven
  `Surface` / `SurfaceOutline` brushes, seven call sites). That is the one real
  shared piece, so it moves to `Core` as the theme-surface helper rather than making
  a Cards consumer pull Docking.
- **Shell's global usings are not a hidden trap.** No candidate relies on the
  `Xtreme.Shell.Models` / `Presentation` globals; every Shell type arrives by an
  explicit `using`, so what is listed below is the whole coupling.
- **Two Shell helpers are behind most of the coupling:** `ThemeResourceLookup`
  (reaches `ServiceLocator` for `IUiAppThemeService`) and `ContentDialogStyleHelper`.
  Neither can move; the controls that use them get a `{ThemeResource}` style or a
  resolver delegate instead.
- **Four converters are defined in `Xtreme.App/App.xaml`, not Shell**
  (`BoolToVisibilityConverter`, `ColorToBrushConverter`, `MultiplyConverter`,
  `XtremeListViewItemStyle`). The library ships its own copies.
- **`XtremeDateTimePicker` uses the Scheduler's `MultiMonthDateNavigator`.** The
  navigator is a primitive, so it moves to Primitives and Scheduler depends on
  Primitives; the alternative (picker stays in Shell) loses the most reusable
  date control for a one-class move.

### Wave 1: extract as-is or with a token swap

| Control | Package | What it takes |
|---|---|---|
| MaskedTextBox + InputMask | Primitives | nothing |
| TypeableTimeBox | Primitives | nothing |
| CompactCalendarDatePicker | Primitives | nothing |
| ColumnFlowPanel, WrapPanel | Primitives | nothing |
| ExpanderClipFix | Primitives | nothing |
| ToastControl | Primitives | nothing (`HubToastHost` stays in Shell) |
| EditorActionBar | Primitives | delete one unused `using` |
| SectionCard + SectionCardTab | Cards | nothing; its font lookup already falls back |
| PagingControl | Primitives | 8 uses of `XtremeFontSizeBody` → `XcFontSizeBody` token |
| MonthYearPicker | Primitives | 2 uses of `XtremeFontSizeBody` → token |
| CopyButton | Primitives | bring the 19-line `ClipboardHelper`, 1 token |
| MultiMonthDateNavigator | Primitives (from Scheduler) | move; Scheduler takes a `PackageReference` to Primitives |

### Wave 2: needs a seam first

| Control | Package | What it takes |
|---|---|---|
| XtremeDateTimePicker | Primitives | 5 Shell theme keys → tokens; depends on TypeableTimeBox and the moved navigator |
| CriteriaPanel | Primitives | replace `ThemeResourceLookup.Track/GetBrush` with a `{ThemeResource}` style |
| ChipPickerControl + ChipPickerDialog | Primitives | move `ChipVM`, ship 3 converters, replace `ContentDialogStyleHelper` with a dialog style the app can override, drop design-time xmlns; 5 uses of `XtremeExcludedBrush` → token |
| OverlayGlyphIcon | Primitives | pluggable geometry source (the app keeps its invoice/pawprint paths), size math without `MultiplyConverter`, style into the package's `Generic.xaml` |
| RowFlagDecorator + RowFlag | **`Xtreme.Components.TableView`** (new, separate) | brush-resolver delegate instead of `ThemeResourceLookup`; isolated because it drags the `WinUI.TableView` package with it |

### Stays in the app

| Control | Why |
|---|---|
| ChatBubbleControl | model is three DTOs, avatar loader goes through `ServiceLocator`, 9 chat-palette keys; a DTO-free rewrite is a product decision, not an extraction |
| OptInStatusDisplay | bound to the CRM `OptInStatusEnum`; too domain-specific for a public library |
| AvatarDiskCache | not a control; hard-codes the app's signed-URL `tenant=` / `v=` convention |
| DockBlade, SearchResultsGrid, LookupSuggestBox, JumpToButton, OptInEditor, ReportActionBar, avatar helpers | `ServiceLocator`, `IUiPreferenceService`, `IXtremeClient`, DTOs |
| GeneralInfoControl, CardSelectorControl, GeneralInfoLayoutEditor, every card body | business logic; only the frame (`SectionCard`) and the docker are library material |

Final dependency graph after both waves:

```
Xtreme.Components (meta)
 ├── Xtreme.Components.Scheduler ──> Primitives ──> Core
 ├── Xtreme.Components.Docking   ─────────────────> Core
 ├── Xtreme.Components.Cards     ─────────────────> Core
 ├── Xtreme.Components.Primitives ────────────────> Core
 └── Xtreme.Components.TableView ──> Primitives (+ WinUI.TableView)
Core = Xc* theme tokens + ActualTheme surface helper (today's DockTheme). No controls.
```

## Gallery and docs

- `samples/Xtreme.Components.Gallery`: Uno single project, desktop + browserwasm,
  Skia renderer, references the `src/` projects directly. One page per control,
  XAML shown next to the live control, seam registrations visible in `App.xaml.cs`.
- WASM build published to GitHub Pages by `ci.yml` on `main`; the README links it.
- The gallery doubles as the in-host test bed (UnoDock runs 3,000+ tests in its
  gallery). Headless tests for models and solvers live in `tests/`.
- Docs are markdown in `docs/`, one page per family, screenshots captured from the
  gallery. No separate docs site until the page count justifies one.

## UnoDock cross-check

[UnoDock](https://github.com/wieslawsoltes/UnoDock) (MIT, Sept 2026) is a reference
to read, not a dependency to take: single author, two preview tags, no user issues
yet. Its packaging model (Core ← library ← optional themes, one `dotnet add package`,
gallery as test host, tag-driven Trusted Publishing) is what this document adopts.

Technical takeaways for Docking, ticketed separately:

- HWND: `Uno.UI.Xaml.WindowHelper.GetNativeWindow(window) as Win32NativeWindow` is
  the supported way to get the handle. Our `GetForegroundWindow` at drag-detect works
  but is a workaround.
- Floating-window move loop: a `SetWindowSubclass` hook on
  `WM_ENTERSIZEMOVE` / `WM_MOVING` / `WM_EXITSIZEMOVE` gives a definite release
  instead of our `sawIdle` gate.
- They do not attempt drag translucency, window pooling or auto-hide animation; those
  stay our differentiators and need no change.
- Their in-surface floating mode on WASM/mobile is the shape we need for XU-448.

## Follow-up tickets

One ticket per package or piece of infrastructure, filed 2026-10-13 under epic XU-95
and linked to XU-880. Blocking chain: 881 → 882 → 883, 882 → 884 → 885, 884 → 886.

1. **XU-881 Repo bootstrap**: create `XtremeCRM/Xtreme.Components` (public, MIT),
   layout above, `Directory.Build.props` metadata, `ci.yml`, `publish.yml` with
   Trusted Publishing, CHANGELOG, nuget prefix request for `Xtreme.Components`.
2. **XU-882 Extract Scheduler, Docking, Cards + Core** from
   `ClientApps/Xtreme.Components` into `src/` projects plus the meta-package;
   `DockTheme` becomes Core's public surface helper; `SectionCard` rides along into
   Cards; fresh initial commit; first `0.1.0-preview.1` tag.
3. **XU-883 XtremeCRM consumes by package**: `PackageReference` + local switch in
   Shell and Harness, delete `ClientApps/Xtreme.Components`, pin in
   `Directory.Packages.props`.
4. **XU-884 Primitives wave 1**: the twelve as-is controls plus the
   `MultiMonthDateNavigator` move; Scheduler takes its Primitives dependency; Shell
   keeps type-forwarding shims for one release if the namespace rename touches too
   many files.
5. **XU-885 Primitives wave 2**: `XtremeDateTimePicker`, `CriteriaPanel`,
   `ChipPicker`, `OverlayGlyphIcon`, each behind its seam (token swap,
   `{ThemeResource}` style, dialog style, pluggable geometry).
6. **XU-886 `Xtreme.Components.TableView`**: `RowFlagDecorator` with a brush-resolver
   delegate.
7. **XU-887 Theme tokens**: `Core` `Xc*` dictionary, per-family migration off
   `SchedulerPalette` and the code palettes, XtremeCRM `App.xaml` mapping,
   high-contrast dictionary.
8. **XU-888 Gallery app** + GitHub Pages publish.
9. **XU-889 Docking follow-ups from UnoDock**: `WindowHelper` HWND,
   `WM_EXITSIZEMOVE` hook.
10. **XU-890 Public API baseline**: `PublicApiAnalyzers` files for every package
    before 1.0.

## Open questions

- nuget.org prefix reservation for `Xtreme.` may be declined (short prefixes often
  are). Publishing does not depend on it; it only blocks squatters.
- `ChatBubbleControl` is the one heavy control with obvious outside appeal. A
  DTO-free rewrite with a pluggable avatar source is possible but is a product
  decision; not ticketed until someone asks for it.
