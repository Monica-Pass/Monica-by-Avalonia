using System.Collections.ObjectModel;
using Monica.Core.Bitwarden;

namespace Monica.App.ViewModels;

/// <summary>
/// The local rows the last synchronization offered to Bitwarden and Bitwarden could not take. Before this
/// list existed the count was computed, returned and dropped: the queue booked nothing, the pull wrote the
/// server's copy back over the row in that same round, and the screen said the two were in sync - so an
/// edit that never left the device was visible only as a conflict the user had not made. Measured against
/// Vaultwarden 1.37.3, the refused row comes back reading the server's title, which is why the rows are
/// copied out of the sync result instead of being judged again here, while the screen still shows them.
/// Restoring that conflict re-creates the same refusal rather than resolving it - the backup carries the
/// shape that was declined - so the wording on screen points at the shape, not at another sync.
/// A second opinion written against the same rules would be the
/// screen guessing, and one drifted row would be enough for it to lie.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private long? _bitwardenUnsyncableVaultId;

    public ObservableCollection<BitwardenUnsyncableChangeDisplayItem> BitwardenUnsyncableChanges { get; } = [];

    // The list belongs to one account's last attempt, so it is shown for that account and no other.
    // Matching on the id rather than clearing when the selection moves matters because the account list
    // reloads under almost every Bitwarden action - clearing on a null selection would retire the
    // standing warning a moment after it appeared, which is the silence this list exists to end.
    public bool HasBitwardenUnsyncableChanges =>
        BitwardenUnsyncableChanges.Count > 0 && SelectedBitwardenAccount?.Id == _bitwardenUnsyncableVaultId;

    /// <summary>
    /// Swaps the whole list for the one the finished synchronization produced. It is never read from storage:
    /// a refusal is a property of an attempt, and a row that has since been fixed - or that the user deleted,
    /// which needs no encoder at all - has to be gone from the screen without anything clearing it.
    /// </summary>
    private void ApplyBitwardenUnsyncableChanges(
        long vaultId,
        IReadOnlyList<BitwardenUnsyncableLocalChange> unsyncable)
    {
        var rows = unsyncable
            .Select(change => new BitwardenUnsyncableChangeDisplayItem(
                change.Title.Length == 0
                    ? _localization.Get("BitwardenUnsyncableUntitledEntry")
                    : change.Title,
                _localization.Get(change.IsPassword
                    ? "BitwardenUnsyncableKindLogin"
                    : "BitwardenUnsyncableKindSecureItem"),
                GetBitwardenPayloadRefusalText(change.Reason)))
            .ToList();
        if (rows.Count > 0)
        {
            // The log keeps codes and a count rather than the titles: the list is a standing warning about
            // the shape of entries, and a diagnostic file is no place for names the user typed.
            AppDiagnostics.Info(
                $"Bitwarden left {rows.Count} local change(s) unsent: " +
                string.Join(
                    ", ",
                    unsyncable
                        .GroupBy(change => change.Reason)
                        .Select(group => $"{group.Key}:{group.Count()}")));
        }

        _bitwardenUnsyncableVaultId = rows.Count == 0 ? null : vaultId;
        BitwardenUnsyncableChanges.Clear();
        foreach (var row in rows)
        {
            BitwardenUnsyncableChanges.Add(row);
        }

        OnPropertyChanged(nameof(HasBitwardenUnsyncableChanges));
    }

    private void ClearBitwardenUnsyncableChanges()
    {
        _bitwardenUnsyncableVaultId = null;
        BitwardenUnsyncableChanges.Clear();
        OnPropertyChanged(nameof(HasBitwardenUnsyncableChanges));
    }

    private string GetBitwardenPayloadRefusalText(BitwardenPayloadRefusal reason) =>
        _localization.Get(reason switch
        {
            BitwardenPayloadRefusal.MissingRemoteRevision => "BitwardenUnsyncableReasonNoRevision",
            BitwardenPayloadRefusal.HasAttachments => "BitwardenUnsyncableReasonAttachments",
            BitwardenPayloadRefusal.MissingTitle => "BitwardenUnsyncableReasonMissingTitle",
            BitwardenPayloadRefusal.PayloadTooLarge => "BitwardenUnsyncableReasonTooLarge",
            BitwardenPayloadRefusal.UnsupportedShape => "BitwardenUnsyncableReasonShape",
            _ => "BitwardenUnsyncableReasonContent"
        });
}

/// <summary>
/// One row of the refusal list: which entry stayed behind, what kind of entry it is, and in the user's own
/// language why Bitwarden would not take the change.
/// </summary>
public sealed record BitwardenUnsyncableChangeDisplayItem(
    string Title,
    string KindText,
    string ReasonText);
