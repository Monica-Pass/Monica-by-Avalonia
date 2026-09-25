using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Features.ImportExport;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeePassEditor))]
    private KeePassEntryEditorViewModel? _keePassEditorPublic;

    public bool HasKeePassEditor => KeePassEditorPublic is not null;

    public bool KeePassVaultIsDirty => _keePassVaultSession?.IsDirty == true;

    public bool CanEditKeePassEntry => _selectedKeePassTreeRow?.IsEntryRow == true;

    /// <summary>
    /// Stands next to the file summary while the opened database holds changes that are not on disk
    /// yet, so the difference between "记入改动" and "保存到文件" is visible instead of assumed.
    /// </summary>
    public string KeePassUnsavedChangesText => KeePassVaultIsDirty
        ? _localization.Get("KeePassUnsavedChanges")
        : "";

    [RelayCommand]
    private async Task EditKeePassEntryAsync()
    {
        var session = _keePassVaultSession;
        var entry = _selectedKeePassTreeRow?.Entry;
        if (session is null || entry is null)
        {
            SetStatusFailure("KeePassEntryRequired");
            return;
        }

        try
        {
            var detail = await session.ReadDetailAsync(entry.GroupUuid, entry.EntryUuid);
            if (detail is null)
            {
                SetStatusFailure("KeePassEntryGone");
                return;
            }

            KeePassEditorPublic = new KeePassEntryEditorViewModel(detail);
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Editing the KeePass entry failed", "KeePassEntryEditFailed", error);
        }
    }

    [RelayCommand]
    private void CancelKeePassEntryEdit() => KeePassEditorPublic = null;

    /// <summary>
    /// Writes the form into the opened database only. Nothing reaches the file here: the user sees
    /// the change in the tree and saves when they choose to, the way every KeePass client works.
    /// </summary>
    [RelayCommand]
    private async Task ApplyKeePassEntryEditAsync()
    {
        var session = _keePassVaultSession;
        var editor = KeePassEditorPublic;
        if (session is null || editor is null)
        {
            return;
        }

        try
        {
            var edit = editor.ToEdit();
            var updated = editor.IsDraft
                ? await session.CreateEntryAsync(editor.GroupUuid, edit)
                : await session.UpdateEntryAsync(edit);
            if (updated is null)
            {
                SetStatusFailure(editor.IsDraft ? "KeePassFolderGone" : "KeePassEntryGone");
                return;
            }

            if (editor.IsDraft)
            {
                _keePassOpenFolders.Add(editor.GroupUuid);
            }

            ShowKeePassEntryDetail(session, updated);
            await RebuildKeePassTreeAsync(session, CancellationToken.None);
            KeePassEditorPublic = null;
            RaiseKeePassWriteState();
            if (editor.IsDraft)
            {
                SetStatusNotice("KeePassEntryCreatedFormat", updated.Row.Title);
            }
            else
            {
                SetStatusMessage("KeePassChangeStaged");
            }
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Applying the KeePass edit failed", "KeePassEditApplyFailed", error);
        }
    }

    [RelayCommand]
    private async Task SaveKeePassVaultAsync()
    {
        var session = _keePassVaultSession;
        if (session is null)
        {
            SetStatusFailure("KeePassPreviewRequired");
            return;
        }

        if (!TryBeginKeePassOperation(out var cancellationToken))
        {
            return;
        }

        try
        {
            if (session.SourcePath is { } sourcePath)
            {
                var result = await session.SaveAsync(cancellationToken);
                SetStatusNotice("KeePassSavedFormat", result.FileBytes);
            }
            else
            {
                await WriteKeePassCopyAsync(session, cancellationToken);
            }

            RaiseKeePassWriteState();
        }
        catch (OperationCanceledException)
        {
            SetStatusNotice("KeePassImportCanceled");
        }
        catch (KeePassVaultException error)
        {
            SetStatusFailure(KeePassWriteFailureKey(error.Error));
        }
        catch (Exception error)
        {
            ReportImportExportFailure("Saving the KeePass database failed", "KeePassSaveFailed", error);
        }
        finally
        {
            EndKeePassOperation();
        }
    }

    /// <summary>
    /// A session opened from bytes has nowhere to save to, so the save command asks for a file and
    /// hands it the verified payload instead of failing on an error the user cannot act on.
    /// </summary>
    private async Task WriteKeePassCopyAsync(KeePassVaultSession session, CancellationToken cancellationToken)
    {
        if (!CanUseFilePicker)
        {
            SetStatusFailure("KeePassNoSourceFile");
            return;
        }

        var payload = await session.ExportAsync(cancellationToken);
        var savedName = await _fileSystemPickerService.SaveBinaryFileAsync(
            _localization.Get("KeePassSaveToFile"),
            session.SourceFileName,
            payload,
            KeePassFileTypes,
            cancellationToken);
        if (savedName is null)
        {
            SetStatusNotice("KeePassImportCanceled");
            return;
        }

        SetStatusNotice("KeePassSavedCopyFormat", savedName);
    }

    private void ShowKeePassEntryDetail(KeePassVaultSession session, KeePassEntryDetail detail)
    {
        _keePassEntryDetails?.Dispose();
        _keePassEntryDetails = new PasswordDetailViewModel(
            _localization,
            _clipboardService,
            _cryptoService,
            _totpService,
            CreatePasswordFromKeePass(session.DatabaseId, detail),
            [],
            null,
            null,
            [],
            [],
            secretsAlreadyPlaintext: true);
        KeePassEntryDetailsPublic = _keePassEntryDetails;
    }

    private void RaiseKeePassWriteState()
    {
        OnPropertyChanged(nameof(KeePassVaultIsDirty));
        OnPropertyChanged(nameof(KeePassUnsavedChangesText));
        OnPropertyChanged(nameof(KeePassPreviewSummaryText));
        RaiseKeePassManageState();
    }

    /// <summary>
    /// The row that is selected decides which of the structural commands are live, so every place
    /// that moves the selection has to raise them together - a stale pair leaves a delete button
    /// pointing at a row the tree no longer shows.
    /// </summary>
    private void RaiseKeePassManageState()
    {
        OnPropertyChanged(nameof(CanEditKeePassEntry));
        OnPropertyChanged(nameof(CanManageSelectedKeePassFolder));
        OnPropertyChanged(nameof(CanManageKeePassRows));
    }

    private static string KeePassWriteFailureKey(KeePassVaultError error) => error switch
    {
        KeePassVaultError.WriteFailed => "KeePassWriteFailed",
        KeePassVaultError.ConcurrentChange => "KeePassConcurrentChange",
        KeePassVaultError.ResourceLimitExceeded => "KeePassResourceLimitExceeded",
        KeePassVaultError.UnsupportedFormat => "KeePassUnsupportedFormat",
        _ => "KeePassUnlockFailed"
    };
}
