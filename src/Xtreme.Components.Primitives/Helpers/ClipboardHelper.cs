namespace Xtreme.Components.Primitives;

/// <summary>
/// One place to put text on the clipboard — the share-link copy buttons (MainPage location
/// flyout, EventEditor, SchedulerHost) all route here so platform quirks (e.g. WASM
/// user-activation requirements) get fixed once.
/// </summary>
public static class ClipboardHelper
{
    public static void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }
}
