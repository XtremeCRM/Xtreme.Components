# Xtreme.Components.Cards

Card controls for [Uno Platform](https://platform.uno). Part of
[Xtreme.Components](https://github.com/XtremeCRM/Xtreme.Components).

**Full guide:** [docs/cards.md](https://github.com/XtremeCRM/Xtreme.Components/blob/main/docs/cards.md) covers every option with samples.

## SectionCard

A boxed card for grouping related fields inside an editor or detail pane. It owns the card
chrome (fill, themed stroke, soft corner, subtly darker right/bottom edge) so every card
matches. Add `SectionCardTab` children and the header renders them as a top-pane
`NavigationView` strip; add none and set `Body` for a headerless card.

```xml
<Page xmlns:cards="using:Xtreme.Components.Cards">
  <cards:SectionCard>
    <!-- Body is the content property, so tabs go inside SectionCard.Tabs. -->
    <cards:SectionCard.Tabs>
      <cards:SectionCardTab Header="Details">
        <StackPanel> ... </StackPanel>
      </cards:SectionCardTab>
      <cards:SectionCardTab Header="Notes">
        <TextBox AcceptsReturn="True" />
      </cards:SectionCardTab>
    </cards:SectionCard.Tabs>
  </cards:SectionCard>
</Page>
```

The title font size reads the `XcFontSizeTitle` resource (16 in Core's defaults) and falls
back to 14 only when no such resource exists.

## CardDockerControl

Arranges cards into panels across a column grid by dragging. Cards are ids plus a title and an
icon factory, so the control carries no app model; the host supplies the card bodies and
persists the resulting layout.

```csharp
var docker = new CardDockerControl { ColumnCount = 3 };
docker.Cards = new[]
{
    new CardDockerCard { Id = "address",  Title = "Address",  CreateIcon = () => new SymbolIcon(Symbol.Map) },
    new CardDockerCard { Id = "contacts", Title = "Contacts", CreateIcon = () => new SymbolIcon(Symbol.People) },
};
docker.Layout = savedLayout ?? new CardDockerLayout();      // panels, columns, tray
docker.LayoutChanged += (_, _) => Persist(docker.Layout);   // the user dragged something
```

Targets `net10.0` only; the consuming Uno head supplies the renderer. MIT licence.
