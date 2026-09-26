using Avalonia.Controls;
using Avalonia.VisualTree;
using Monica.App.Features.Sync;

namespace Monica.UiTests;

/// <summary>
/// The browse pane is a control of its own, so the names inside it are registered in that control's name
/// scope rather than the import tab's. Walking the visual tree is how the window's own smoke code reaches
/// them, and it carries more evidence than the name scope did: a control that sits under a collapsed
/// panel is declared and not shown, and this reports it as absent. That distinction is what caught the
/// new-entry form existing in the view model while the column hosting it was down.
/// </summary>
internal static class KeePassViewProbe
{
    internal static T InPane<T>(this SyncImportView view, string name)
        where T : Control =>
        view.TryInPane<T>(name) ?? throw new InvalidOperationException(
            $"no {typeof(T).Name} named '{name}' is realized on screen");

    internal static T? TryInPane<T>(this SyncImportView view, string name)
        where T : Control =>
        view.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
}
