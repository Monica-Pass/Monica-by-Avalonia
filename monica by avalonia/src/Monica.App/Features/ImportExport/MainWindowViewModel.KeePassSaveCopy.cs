using CommunityToolkit.Mvvm.Input;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// Writing the opened database to a file the person picks. This is the exit from a refused save: the
/// file the session came from changed while its own edits were being made, and neither overwriting
/// what someone else wrote nor throwing away the edits here is acceptable. A copy deliberately does
/// not rebind the session or clear the unsaved marker, because the file it opened still does not hold
/// these changes and the screen has to keep saying so.
/// </summary>
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task SaveKeePassVaultCopyAsync()
    {
        var session = _keePassVaultSession;
        if (session is null)
        {
            SetStatusFailure("KeePassPreviewRequired");
            return;
        }

        if (!CanUseFilePicker)
        {
            SetStatusFailure("KeePassCopyNeedsPicker");
            return;
        }

        if (!TryBeginKeePassOperation(out var cancellationToken))
        {
            return;
        }

        try
        {
            await WriteKeePassCopyAsync(session, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SetStatusNotice("KeePassImportCanceled");
        }
        catch (Exception error)
        {
            ReportImportExportFailure(
                "Saving a copy of the KeePass database failed",
                "KeePassSaveCopyFailed",
                error);
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
        var payload = await session.ExportAsync(cancellationToken);
        var savedName = await _fileSystemPickerService.SaveBinaryFileAsync(
            _localization.Get("KeePassSaveACopy"),
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
}
