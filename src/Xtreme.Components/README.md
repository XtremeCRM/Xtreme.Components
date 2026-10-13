# Xtreme.Components

The meta-package: one reference that pulls every
[Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components) family at the same version.

```bash
dotnet add package Xtreme.Components --prerelease
```

| Package | What it is |
|---|---|
| `Xtreme.Components.Core` | Theme tokens and the `ActualTheme`-driven surface helper. No controls. |
| `Xtreme.Components.Scheduler` | Day / Week / Month / Agenda / Planner scheduler with drag interaction. |
| `Xtreme.Components.Docking` | Dock manager: dockable panes, tab groups, auto-hide, float to an OS window. |
| `Xtreme.Components.Cards` | `SectionCard` frame with tab strip and `CardDockerControl`. |

Prefer the individual package when you need one family; this one is for apps that use them all.

App-agnostic, primitive-only Uno Platform controls targeting `net10.0`; the consuming head
supplies the renderer for Desktop, Android, iOS and WebAssembly. MIT licence.
