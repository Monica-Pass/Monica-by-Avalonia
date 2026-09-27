using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// The name a new file is offered under before the person renames it. The database name inside the
    /// file is derived from whatever they settle on, so the save dialog is the one place a new vault
    /// gets named - and this is the same spelling the platform service uses for "not named yet".
    /// </summary>
    private const string KeePassNewDatabaseFileName = "database.kdbx";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeePassOpenForm))]
    private bool _showKeePassCreateForm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateKeePassVault))]
    private string _keePassCreatePassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateKeePassVault))]
    [NotifyPropertyChangedFor(nameof(ShowsKeePassCreatePasswordMismatch))]
    [NotifyPropertyChangedFor(nameof(KeePassCreatePasswordErrorText))]
    private string _keePassCreateConfirmation = "";

    /// <summary>
    /// Only the form's own state: whether the two lines agree and neither is empty. Whether the
    /// machine can put a file on disk at all is asked where the command runs, because a picker that
    /// went away mid-session has to say so in the status bar rather than leave a button dimmed for a
    /// reason nobody can read.
    /// </summary>
    public bool CanCreateKeePassVault =>
        !string.IsNullOrWhiteSpace(KeePassCreatePassword) && KeePassCreatePasswordsMatch;

    public bool KeePassCreatePasswordsMatch => KeePassCreatePassword == KeePassCreateConfirmation;

    /// <summary>
    /// Says nothing until the second line has been typed, so the form does not scold halfway through
    /// the first one. An empty confirmation matches any password and stays quiet.
    /// </summary>
    public bool ShowsKeePassCreatePasswordMismatch =>
        KeePassCreateConfirmation.Length > 0 && !KeePassCreatePasswordsMatch;

    public string KeePassCreatePasswordErrorText =>
        ShowsKeePassCreatePasswordMismatch ? _localization.Get("KeePassCreatePasswordMismatch") : "";

    /// <summary>
    /// Opens the form only. Nothing here touches the database that may already be open: a new vault
    /// replaces it once one is actually written, and backing out of this form has to leave the
    /// unlocked file where it was.
    /// </summary>
    [RelayCommand]
    private void NewKeePassVault() => ShowKeePassCreateForm = true;

    [RelayCommand]
    private void CancelKeePassVaultCreate() => ClearKeePassCreateForm();

    /// <summary>
    /// Builds a new database at the place the person names and opens it right away, so the first thing
    /// they do after creating a vault is use it rather than find it. The bytes go out through the same
    /// atomic write every other database write uses; the location is asked for on its own because the
    /// payload is ours to make, and knowing where a file goes without writing it is what lets later
    /// saves land in place instead of asking again.
    /// </summary>
    [RelayCommand]
    private async Task CreateKeePassVaultAsync()
    {
        if (!CanUseFilePicker)
        {
            SetStatusFailure("KeePassCreateLocationUnavailable");
            return;
        }

        if (string.IsNullOrWhiteSpace(KeePassCreatePassword))
        {
            SetStatusFailure("KeePassCreatePasswordRequired");
            return;
        }

        if (!KeePassCreatePasswordsMatch)
        {
            SetStatusFailure("KeePassCreatePasswordMismatch");
            return;
        }

        if (KeePassVaultIsDirty)
        {
            SetStatusFailure("KeePassDiscardBeforeCreating");
            return;
        }

        if (!TryBeginKeePassOperation(out var cancellationToken))
        {
            return;
        }

        var password = KeePassCreatePassword;
        try
        {
            IsKeePassImportProgressIndeterminate = true;
            var target = await _fileSystemPickerService.PickSaveFileTargetAsync(
                _localization.Get("NewKeePassDatabase"),
                KeePassNewDatabaseFileName,
                KeePassFileTypes,
                cancellationToken);
            if (target is null)
            {
                SetStatusNotice("KeePassImportCanceled");
                return;
            }

            // A save dialog will happily name a file that already exists, and its own "replace it?"
            // prompt is not a question about a database - answering it the way the dialog expects would
            // empty a vault that unlocks. Creating refuses instead of trusting that prompt.
            if (string.IsNullOrWhiteSpace(target.FullPath))
            {
                SetStatusFailure("KeePassCreateNoLocalPath");
                return;
            }

            if (File.Exists(target.FullPath))
            {
                SetStatusFailure("KeePassCreateFileExists");
                return;
            }

            SetStatusMessage("KeePassCreateWriting");
            var session = await _keePassVaultService.CreateAsync(
                target.FileName,
                password,
                target.FullPath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Whatever was open held no unsaved work - the dirty guard above - and this database is
            // the one the tab is holding now, so the session is replaced rather than stacked.
            ClearKeePassImportPreview();
            _keePassPendingFile = null;
            KeePassSelectedFileName = "";
            _keePassVaultSession = session;
            RememberKeePassVault(session);
            _keePassOpenFolders.Add(session.RootGroupUuid);
            await RebuildKeePassTreeAsync(session, cancellationToken);
            OnPropertyChanged(nameof(HasKeePassImportPreview));
            OnPropertyChanged(nameof(KeePassPreviewSummaryText));
            RaiseKeePassWriteState();
            SetStatusNotice("KeePassCreatedFormat", target.FileName);
        }
        catch (OperationCanceledException)
        {
            SetStatusNotice("KeePassImportCanceled");
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Creating a KeePass database failed", "KeePassCreateFailed", error);
        }
        finally
        {
            password = "";
            ClearKeePassCreateForm();
            EndKeePassOperation();
        }
    }

    private void ClearKeePassCreateForm()
    {
        ShowKeePassCreateForm = false;
        KeePassCreatePassword = "";
        KeePassCreateConfirmation = "";
    }
}
