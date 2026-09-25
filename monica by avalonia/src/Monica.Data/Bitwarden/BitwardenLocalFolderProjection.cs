using Monica.Core.Bitwarden;
using Monica.Core.Models;

namespace Monica.Data.Bitwarden;

/// <summary>
/// Which remote folder this row would be reported to live in. The library page moves an entry by rewriting
/// its local category, so the category carries the user's intent and <see cref="PasswordEntry.BitwardenFolderId"/>
/// is only the folder the server last confirmed. Anything that asks whether a row has drifted, and anything
/// that builds the payload for it, has to read the location through this one function: a judge holding the
/// stale column and an encoder holding the category disagree by construction, and a move between folders is
/// then either invisible to the drift scan forever or uploaded as a difference nobody decided.
/// </summary>
internal static class BitwardenLocalFolderProjection
{
    /// <summary>
    /// The live folders of one vault, seen from the local category each was created for. The pull makes that
    /// binding for every folder it shows, so a category absent from it is one the server has no counterpart
    /// for rather than one whose folder went away.
    /// </summary>
    public static IReadOnlyDictionary<long, string> BoundCategories(
        IReadOnlyList<BitwardenStoredRemoteFolder> folders)
    {
        var byCategory = new Dictionary<long, string>();
        foreach (var folder in folders.Where(folder => !folder.IsDeleted && folder.LocalCategoryId is not null))
        {
            // Two folders bound to one category is a state the pull cannot create; keeping the folder the
            // snapshot lists first means the row is left where the last synchronization put it rather than
            // moved by whichever binding a dictionary happens to enumerate first.
            byCategory.TryAdd(folder.LocalCategoryId!.Value, folder.RemoteFolderId);
        }

        return byCategory;
    }

    public static PasswordEntry Project(
        PasswordEntry entry,
        IReadOnlyDictionary<long, string> boundCategories)
    {
        var folderId = FolderIdOf(entry.CategoryId, boundCategories);
        if (string.Equals(folderId, entry.BitwardenFolderId, StringComparison.Ordinal))
        {
            return entry;
        }

        var projected = entry.CreateDetachedCopy();
        projected.BitwardenFolderId = folderId;
        return projected;
    }

    public static SecureItem Project(
        SecureItem item,
        IReadOnlyDictionary<long, string> boundCategories)
    {
        var folderId = FolderIdOf(item.CategoryId, boundCategories);
        if (string.Equals(folderId, item.BitwardenFolderId, StringComparison.Ordinal))
        {
            return item;
        }

        var projected = item.CreateDetachedCopy();
        projected.BitwardenFolderId = folderId;
        return projected;
    }

    /// <summary>
    /// The remote location is read out of the local tree alone: a category bound to one of this vault's
    /// folders answers that folder, and everything else - the root, or a folder the server has no
    /// counterpart for - answers root. Falling back to the folder column looked protective and measured
    /// worse: against a real Vaultwarden, a move the last synchronization had pushed leaves that column
    /// empty (only a pull writes it, and a pull that agrees with the server rewrites nothing), so the
    /// fallback reported root anyway while the code read as if it were keeping the entry filed. An answer
    /// that depends on a column nobody can be sure was stamped is not a rule; this one is a function of the
    /// tree the user is looking at, so the same tree always owes the same upload and no move is ever
    /// undone by the next synchronization.
    /// </summary>
    private static string? FolderIdOf(
        long? categoryId,
        IReadOnlyDictionary<long, string> boundCategories) =>
        categoryId is not null && boundCategories.TryGetValue(categoryId.Value, out var folderId)
            ? folderId
            : null;
}
