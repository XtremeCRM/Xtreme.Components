# Xtreme.WinUI.TableView

[WinUI.TableView](https://github.com/w-ahmad/WinUI.TableView), the TableView/DataGrid control by
w-ahmad, built for **Uno Platform 7**.

The upstream `WinUI.TableView` package (through 1.5.1) is compiled against Uno.WinUI 6.5. Uno 7
cannot load libraries built for Uno 6.x, so an Uno 7 app cannot use it. This package is the same
code, compiled against Uno.WinUI 7. It changes only the few lines that used Uno-only APIs Uno 7
removed.

- Same assembly (`WinUI.TableView.dll`) and the same `WinUI.TableView` namespace, so your code and
  XAML stay as they are. Swap the package reference:

  ```xml
  <!-- before -->
  <PackageReference Include="WinUI.TableView" Version="1.5.1" />
  <!-- after -->
  <PackageReference Include="Xtreme.WinUI.TableView" Version="1.5.1-uno7.1" />
  ```

- Do not reference both packages: they ship the same assembly.
- The version is upstream's version plus `-uno7.N`. This package goes away once upstream publishes an
  Uno 7 build.
- Targets `net11.0` for Uno Platform heads (Skia renderer). It has no WinAppSDK build. For a native
  Windows (WinAppSDK) head, use the upstream package.

Based on upstream tag `v1.5.1`. The full list of changes is in
[UPSTREAM.md](https://github.com/XtremeCRM/Xtreme.Components/blob/main/src/Xtreme.WinUI.TableView/UPSTREAM.md).
MIT license; copyright w-ahmad. Documentation: https://w-ahmad.github.io/WinUI.TableView/
