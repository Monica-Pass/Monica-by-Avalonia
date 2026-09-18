using Avalonia;
using FluentIcons.Common;

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

public enum VaultTreeRowKind
{
    Folder,
    Entry
}

/// A row in a tree that mixes folders with the entries hanging under them. The control stays
/// ignorant of the vault model: an entry arrives as presentation primitives, and the domain kind
/// lives on the concrete row type in Features/Vault.
///
/// Implementers must return a collision-free <see cref="IFolderTreeRow.Key"/> — password ids and
/// secure-item ids are both <c>long</c> and overlap, so a bare id is not a key. See
/// <c>VaultTreeKey</c>.
public interface IVaultTreeRow : IFolderTreeRow
{
    VaultTreeRowKind RowKind { get; }
    bool IsEntryRow { get; }
    Symbol EntrySymbol { get; }
    string EntryDetail { get; }
}

// Dropping one folder row onto another asks the host to reparent the source; whether the move is
// allowed is the host's call, since only it knows the real folder paths. Entries move through the
// context menu instead, so the control never raises this for an entry row.
public sealed record FolderMoveRequest(IFolderTreeRow Source, IFolderTreeRow Target);

public static class FolderTreeLayout
{
    public const int IndentStep = 16;

    public static Thickness IndentFor(int level) => new(Math.Max(0, level) * IndentStep, 0, 0, 0);
}

public static class VaultTreeRowExtensions
{
    /// Drag-and-drop reparenting is folder-to-folder only, so both ends of a drop have to pass this.
    public static bool IsEntryLeaf(this IFolderTreeRow row) => row is IVaultTreeRow { IsEntryRow: true };
}
