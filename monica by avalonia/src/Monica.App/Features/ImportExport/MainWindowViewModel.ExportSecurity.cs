namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _hasAuthorizedExportPreview;

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
            _hasAuthorizedExportPreview = true;
        }

        return authorized;
    }

    private async Task<bool> AuthorizeFileExportAsync()
    {
        if (_hasAuthorizedExportPreview)
        {
            _hasAuthorizedExportPreview = false;
            return true;
        }

        var authorized = await AuthorizeSensitiveExportAsync(grantFileExport: false);
        return authorized;
    }
}
