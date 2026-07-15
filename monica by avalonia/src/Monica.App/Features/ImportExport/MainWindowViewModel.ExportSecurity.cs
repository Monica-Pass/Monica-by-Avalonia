namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan ExportPreviewAuthorizationLifetime = TimeSpan.FromMinutes(2);
    private DateTimeOffset? _exportPreviewAuthorizationExpiresAt;

    private async Task<bool> AuthorizeSensitiveExportAsync(bool grantFileExport = true)
    {
        if (_vaultSessionService.IsExplicitlyLocked)
        {
            StatusMessage = _localization.Get("VaultLocked");
            return false;
        }

        var authorized = await _exportAuthorizationService.AuthorizeAsync(RequirePasswordBeforeExport);
        if (!authorized)
        {
            StatusMessage = _localization.Get("ExportAuthorizationFailed");
        }
        else if (grantFileExport)
        {
            _exportPreviewAuthorizationExpiresAt = DateTimeOffset.UtcNow.Add(ExportPreviewAuthorizationLifetime);
        }

        return authorized;
    }

    private async Task<bool> AuthorizeFileExportAsync()
    {
        if (_exportPreviewAuthorizationExpiresAt is { } expiresAt && expiresAt >= DateTimeOffset.UtcNow)
        {
            _exportPreviewAuthorizationExpiresAt = null;
            return true;
        }

        _exportPreviewAuthorizationExpiresAt = null;

        var authorized = await AuthorizeSensitiveExportAsync(grantFileExport: false);
        return authorized;
    }

    private void ClearSensitiveExportPreviews()
    {
        ExportPreview = "";
        ExportCsvPreview = "";
        ExportNoteCsvPreview = "";
        ExportTotpCsvPreview = "";
        ExportAegisPreview = "";
        _exportPreviewAuthorizationExpiresAt = null;
    }

    private void ClearSensitiveImportBuffers()
    {
        ImportJsonText = "";
        ImportCsvText = "";
        ImportNoteCsvText = "";
        ImportAegisJsonText = "";
        AegisImportPassword = "";
        IsAegisImportPasswordRequired = false;
        ImportTotpCsvText = "";
        ClearKeePassImportState(cancelActiveOperation: true);
    }
}
