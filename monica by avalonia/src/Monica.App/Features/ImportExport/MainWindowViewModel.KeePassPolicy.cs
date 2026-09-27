using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// The three numbers a KeePass database carries about how much of an entry's past to keep, shown and
/// edited where the library is browsed. They are the file's own settings rather than this client's, and
/// the size one is asked for in megabytes because that is the unit every other client of this file
/// spells it in. <see cref="KeePassHistorySizeUnits"/> keeps the conversion honest: what the box shows
/// is a truncated view of a byte count, so a box nobody edited gives the file its own number back.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _keePassPolicyMaxItemsText = "";

    [ObservableProperty]
    private string _keePassPolicyMaxSizeMbText = "";

    [ObservableProperty]
    private string _keePassPolicyMaintenanceDaysText = "";

    /// <summary>
    /// The size limit as the open database holds it, in the bytes the file actually carries. The box
    /// above shows megabytes, so this is what a re-applied box must not silently round.
    /// </summary>
    private long? _keePassPolicyMaxSizeBytes;

    /// <summary>
    /// The rail is also where a person reads the library's own settings, so it stays up with a database
    /// open even while no entry is selected - previously it only existed to show one.
    /// </summary>
    public bool ShowsKeePassRail => ShowsKeePassDetailColumn || HasKeePassImportPreview;

    /// <summary>
    /// Puts the policy the session read into the three boxes. Called on open and after an apply, so the
    /// numbers on screen are the ones the database holds now rather than what was typed at it.
    /// </summary>
    private async Task LoadKeePassHistoryPolicyAsync(KeePassVaultSession session)
    {
        var policy = await session.ReadHistoryPolicyAsync();
        KeePassPolicyMaxItemsText = policy.MaxItems.ToString();
        SetKeePassPolicyMaxSize(policy.MaxSizeBytes);
        KeePassPolicyMaintenanceDaysText = policy.MaintenanceDays.ToString();
        OnPropertyChanged(nameof(ShowsKeePassRail));
    }

    /// <summary>
    /// Writes what the three boxes say into the open database. Refusing a box it cannot parse is the
    /// point: the alternative is reading "-1" out of a half-typed field and quietly telling the file to
    /// keep every version forever.
    /// </summary>
    [RelayCommand]
    private async Task ApplyKeePassHistoryPolicyAsync()
    {
        var session = _keePassVaultSession;
        if (session is null)
        {
            SetStatusFailure("KeePassEntryRequired");
            return;
        }

        var policy = ReadPolicy(
            KeePassPolicyMaxItemsText,
            KeePassPolicyMaxSizeMbText,
            _keePassPolicyMaxSizeBytes,
            KeePassPolicyMaintenanceDaysText);
        if (policy is null)
        {
            SetStatusFailure("KeePassPolicyInvalid");
            await LoadKeePassHistoryPolicyAsync(session);
            return;
        }

        try
        {
            var applied = await session.ApplyHistoryPolicyAsync(policy);
            KeePassPolicyMaxItemsText = applied.MaxItems.ToString();
            SetKeePassPolicyMaxSize(applied.MaxSizeBytes);
            KeePassPolicyMaintenanceDaysText = applied.MaintenanceDays.ToString();
            RaiseKeePassWriteState();
            SetStatusNotice("KeePassPolicyApplied");
        }
        catch (Exception error)
        {
            ReportImportExportFailure(
                "Applying the KeePass history policy failed",
                "KeePassPolicyFailed",
                error);
        }
    }

    private void SetKeePassPolicyMaxSize(long bytes)
    {
        _keePassPolicyMaxSizeBytes = bytes;
        KeePassPolicyMaxSizeMbText = KeePassHistorySizeUnits.ToDisplayMegabytes(bytes);
    }

    /// <summary>
    /// The floors are the ones the file can hold: a count below -1 has no meaning to any KeePass client,
    /// the size is capped at what the other clients of this file can carry, and the age is stored as an
    /// unsigned number, so it has no negative spelling at all. A box that is not a plain integer returns
    /// null rather than a default.
    /// </summary>
    private static KeePassHistoryPolicy? ReadPolicy(
        string maxItemsText,
        string maxSizeMbText,
        long? fileMaxSizeBytes,
        string maintenanceDaysText)
    {
        if (!int.TryParse(maxItemsText.Trim(), out var maxItems) || maxItems < -1
            || !KeePassHistorySizeUnits.TryParseMegabytes(
                maxSizeMbText,
                fileMaxSizeBytes,
                out var maxSizeBytes)
            || !uint.TryParse(maintenanceDaysText.Trim(), out var maintenanceDays))
        {
            return null;
        }

        return new KeePassHistoryPolicy(maxItems, maxSizeBytes, maintenanceDays);
    }

    /// <summary>
    /// Opening and closing a database decides both whether the rail is up and whether what is in the
    /// library can be managed, so the two are raised together - a rail left up over a database that is
    /// gone is a form pointing at nothing.
    /// </summary>
    private void RaiseKeePassRail()
    {
        OnPropertyChanged(nameof(HasKeePassImportPreview));
        OnPropertyChanged(nameof(ShowsKeePassRail));
    }
}
