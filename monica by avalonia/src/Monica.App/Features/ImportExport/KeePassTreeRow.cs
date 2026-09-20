using Avalonia;
using FluentIcons.Common;
using Monica.App.Controls;
using Monica.Platform.Services;

namespace Monica.App.Features.ImportExport;

/// A row in the KeePass browse tree. The tree mixes folders with entries, so each row carries
/// either a group or an entry payload. The key is prefixed to avoid collisions between group
/// UUIDs and entry UUIDs (both are hex strings and could overlap).
public sealed record KeePassTreeRow : IVaultTreeRow
{
    public required KeePassTreeRowKind Kind { get; init; }

    public required KeePassGroupRow? Group { get; init; }

    public required KeePassEntryRow? Entry { get; init; }

    public string Key => Kind == KeePassTreeRowKind.Folder
        ? $"kp-folder:{Group?.Uuid}"
        : $"kp-entry:{Entry?.EntryUuid}";

    public string Label => Kind == KeePassTreeRowKind.Folder
        ? Group?.Name ?? ""
        : Entry?.Title ?? "";

    public Thickness Indent { get; init; }

    public bool HasChildren => Kind == KeePassTreeRowKind.Folder;

    public bool IsExpanded { get; init; }

    public VaultTreeRowKind RowKind => Kind == KeePassTreeRowKind.Folder
        ? VaultTreeRowKind.Folder
        : VaultTreeRowKind.Entry;

    public bool IsEntryRow => Kind == KeePassTreeRowKind.Entry;

    public Symbol EntrySymbol => Kind == KeePassTreeRowKind.Folder
        ? Symbol.Folder
        : Symbol.Key;

    public string EntryDetail => Kind == KeePassTreeRowKind.Entry
        ? Entry?.UserName ?? ""
        : "";

    public bool IsBatchable => false;

    public bool IsSelected { get; set; }

    public bool CanCopyUsername => Kind == KeePassTreeRowKind.Entry &&
        !string.IsNullOrWhiteSpace(Entry?.UserName);

    public bool CanCopySecret => Kind == KeePassTreeRowKind.Entry;

    public bool CanCopyCode => false;
}

public enum KeePassTreeRowKind
{
    Folder,
    Entry
}
