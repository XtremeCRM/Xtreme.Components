# Xtreme.Components.TableView

Helpers for [WinUI.TableView](https://github.com/w-ahmad/WinUI.TableView) on
[Uno Platform](https://platform.uno). Part of
[Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components). Kept separate from
`Xtreme.Components.Primitives` because it carries the `WinUI.TableView` dependency.

**Full guide:** [docs/tableview.md](https://github.com/XtremeCRM/Xtreme.Components/blob/main/docs/tableview.md) covers every option with samples.

## RowFlagDecorator

Frames a flagged row in a 2px colored frame and pins a small caps tag ("NEW", "CHANGED",
"AGED OUT") to its bottom-left corner. Recycle-safe and theme-aware: every realized container is
painted or cleared on each pass, and a theme switch repaints the row it holds at that moment. The
frame is an overlay, so the row's own `BorderBrush` / `BorderThickness` and size are left alone.

```csharp
using Xtreme.Components.TableView;

RowFlagDecorator.Attach(EventTypesTable, item => item switch
{
    EventTypeRow { IsNew: true }     => new RowFlag("NEW",     "XcFlagNewBrush",     "Added in this session"),
    EventTypeRow { IsChanged: true } => new RowFlag("CHANGED", "XcFlagChangedBrush"),
    _ => null,
});

// after a staged edit changes a row's flag without the list re-binding:
RowFlagDecorator.Refresh(EventTypesTable);
```

`RowFlag.BrushKey` names a brush resource. By default it resolves for the row's own
`ActualTheme` through `ComponentHost.TryGetBrush(key, theme)` (the app's and its merged
dictionaries' `ThemeDictionaries`, then application scope). Pass a `BrushResolver` to `Attach`
to resolve keys your own way, or a `RowFlagOptions` to also set the frame thickness and the tag's
text size, color, padding and corners.

```csharp
RowFlagDecorator.Attach(EventTypesTable, FlagOf, new RowFlagOptions { FrameThickness = 3, TagFontSize = 10 });
RowFlagDecorator.Detach(EventTypesTable);   // stop decorating; realized rows are cleared
```

Attaching again to the same table replaces the earlier registration.

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT license.
