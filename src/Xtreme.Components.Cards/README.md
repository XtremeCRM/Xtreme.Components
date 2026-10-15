# Xtreme.Components.Cards

Card controls for [Uno Platform](https://platform.uno). Part of
[Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components).

**Full guide:** [docs/cards.md](https://github.com/XtremeCRM/Xtreme.Components/blob/main/docs/cards.md) covers every option with samples.

## SectionCard

A boxed card for grouping related fields inside an editor or detail pane. It owns the card
chrome (fill, themed stroke, soft corner, subtly darker right/bottom edge) so every card
matches. Add `SectionCardTab` children (each with a `Header` and optional `Icon`) and the header
renders them as a top-pane `NavigationView` strip; add none and set `Body` (and optionally
`Title`) for a headerless card. The card stays live after load: tabs can be added or removed,
`Title` and tab headers/icons changed, and `SelectedIndex` / `SelectedTab` / `SelectionChanged`
track the selected tab.

```xml
<Page xmlns:cards="using:Xtreme.Components.Cards">
  <cards:SectionCard>
    <!-- Body is the content property, so tabs go inside SectionCard.Tabs. -->
    <cards:SectionCard.Tabs>
      <cards:SectionCardTab Header="Details">
        <StackPanel> ... </StackPanel>
      </cards:SectionCardTab>
      <cards:SectionCardTab Header="Notes">
        <cards:SectionCardTab.Icon><SymbolIcon Symbol="Comment" /></cards:SectionCardTab.Icon>
        <TextBox AcceptsReturn="True" />
      </cards:SectionCardTab>
    </cards:SectionCard.Tabs>
  </cards:SectionCard>
</Page>
```

Set `HeaderFit="Abbreviate"` and the tab headers shorten as the card narrows ("Contact Det..",
then icon only), measured against the strip rather than looked up by breakpoint, before any tab
moves into the overflow menu. The same rule is public as `LabelAbbreviation.Abbreviate`.

The title font size reads the `XcFontSizeTitle` resource (16 in Core's defaults) and falls
back to 14 only when no such resource exists.

## CardDockerControl

Arranges cards into panels across a column grid by dragging. Cards are ids plus a title and an
icon factory, so the control carries no app model; the host supplies the card bodies and
persists the resulting layout. Users drag cards between panels, drag whole panels by their top
handle, resize panels, and can do the moves from the keyboard (Ctrl+arrows, Delete to set aside).
Lowering `ColumnCount` parks panels in the last column and raising it sends them back. The tray
and slot strings and the drag colors are properties.

```csharp
var docker = new CardDockerControl { ColumnCount = 3 };
docker.Cards = new[]
{
    new CardDockerCard { Id = "address",  Title = "Address",  CreateIcon = () => new SymbolIcon(Symbol.Map) },
    new CardDockerCard { Id = "contacts", Title = "Contacts", CreateIcon = () => new SymbolIcon(Symbol.People) },
};
docker.Layout = savedLayout ?? new CardDockerLayout();      // panels, columns, tray
docker.LayoutChanged += (_, _) => Persist(docker.Layout);   // a user edit changed the layout
```

`Layout` is a value: every read returns a copy, and assigning stores a copy. `LayoutChanged` fires
only for user edits that change the arrangement, never for assignments.

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT license.
