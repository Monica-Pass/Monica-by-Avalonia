using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// The three numbers a KeePass database carries about how much of an entry's past to keep, shown and
/// edited where the library is browsed. They are the file's own settings rather than this client's,
/// which is why the byte limit is asked for in bytes: a conversion to a friendlier unit would round a
/// number the file holds exactly, and applying what the screen showed would then change it.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _keePassPolicyMaxItemsText = "";

    [ObservableProperty]
    private string _keePassPolicyMaxSizeBytesText = "";

    [ObservableProperty]
    private string _keePassPolicyMaintenanceDaysText = "";

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
        KeePassPolicyMaxSizeBytesText = policy.MaxSizeBytes.ToString();
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
            KeePassPolicyMaxSizeBytesText,
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
            KeePassPolicyMaxSizeBytesText = applied.MaxSizeBytes.ToString();
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

    /// <summary>
    /// The floors are the ones the file can hold: a count or a size below -1 has no meaning to any
    /// KeePass client, and the age is stored as an unsigned number, so it has no negative spelling at
    /// all. A box that is not a plain integer returns null rather than a default.
    /// </summary>
    private static KeePassHistoryPolicy? ReadPolicy(
        string maxItemsText,
        string maxSizeText,
        string maintenanceDaysText)
    {
        if (!int.TryParse(maxItemsText.Trim(), out var maxItems) || maxItems < -1
            || !long.TryParse(maxSizeText.Trim(), out var maxSize) || maxSize < -1
            || !uint.TryParse(maintenanceDaysText.Trim(), out var maintenanceDays))
        {
            return null;
        }

        return new KeePassHistoryPolicy(maxItems, maxSize, maintenanceDays);
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
