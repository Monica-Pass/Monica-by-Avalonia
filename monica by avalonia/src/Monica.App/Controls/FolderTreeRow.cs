using Avalonia;

namespace Monica.App.Controls;

// Both folder rails already project flat, depth-tagged rows; the control only needs to read them.
public interface IFolderTreeRow
{
    string Key { get; }
    string Label { get; }
    Thickness Indent { get; }
    bool HasChildren { get; }
    bool IsExpanded { get; }
}

// Dropping one folder row onto another asks the host to reparent the source; whether the move is
// allowed is the host's call, since only it knows the real folder paths.
public sealed record FolderMoveRequest(IFolderTreeRow Source, IFolderTreeRow Target);

public static class FolderTreeLayout
{
    public const int IndentStep = 16;

    public static Thickness IndentFor(int level) => new(Math.Max(0, level) * IndentStep, 0, 0, 0);
}
