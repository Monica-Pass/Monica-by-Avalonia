using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly PlatformFilePickerFileType[] TotpQrImageFileTypes =
    [
        new("QR images", ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"])
    ];

    [RelayCommand(CanExecute = nameof(CanUseFilePicker))]
    private async Task ScanTotpQrAsync()
    {
        try
        {
            var file = await _fileSystemPickerService.OpenBinaryFileAsync(
                _localization.Get("TotpScanQr"),
                TotpQrImageFileTypes);
            if (file is null)
            {
                return;
            }

            string? payload;
            try
            {
                payload = TotpQrCodeDecoder.TryDecode(file.Content);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(file.Content);
            }

            var data = string.IsNullOrWhiteSpace(payload)
                ? null
                : TotpDataResolver.FromAuthenticatorKey(payload);
            if (data is null || string.IsNullOrWhiteSpace(data.Secret))
            {
                StatusMessage = _localization.Get("TotpQrInvalidImage");
                return;
            }

            var item = new SecureItem
            {
                ItemType = VaultItemType.Totp,
                Title = BuildScannedTotpTitle(data),
                Notes = data.AccountName,
                ItemData = TotpDataResolver.ToItemData(data),
                CreatedAt = DateTimeOffset.UtcNow
            };
            var editor = await _totpEditorDialogService.ShowAsync(item);
            if (editor is not null)
            {
                await CompleteTotpCreateAsync(editor);
            }
        }
        catch (Exception ex)
        {
            ReportImportExportFailure("Scanning authenticator QR image failed", "TotpQrDecodeFailed", ex);
        }
    }

    private async Task CompleteTotpCreateAsync(TotpEditorViewModel editor)
    {
        var item = editor.ApplyTo();
        RefreshTotpDisplay(item);
        await _repository.SaveSecureItemAsync(item);
        await LogOperationAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "CREATE",
            DeviceName = Environment.MachineName
        });
        TrackTotpSelection(item);
        TotpItems.Insert(0, item);
        SelectedTotpItem = item;
        TotpNarrowShowsList = false;
        RaiseTotpCountState(reconcileSelection: false);
        StatusMessage = _localization.Format("SavedTotpFormat", item.Title);
    }

    private static string BuildScannedTotpTitle(TotpData data) =>
        string.IsNullOrWhiteSpace(data.Issuer)
            ? data.AccountName
            : string.IsNullOrWhiteSpace(data.AccountName)
                ? data.Issuer
                : $"{data.Issuer}: {data.AccountName}";
}
