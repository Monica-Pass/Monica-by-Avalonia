using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;
using QRCoder;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public void Authenticator_qr_decoder_reads_otpauth_payload()
    {
        const string payload = "otpauth://totp/GitHub:dev%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";
        var bytes = BuildQrPng(payload);
        try
        {
            Assert.Equal(payload, TotpQrCodeDecoder.TryDecode(bytes));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        }
    }

    [Fact]
    public async Task Authenticator_scan_imports_selected_qr_image_and_clears_image_bytes()
    {
        const string payload = "otpauth://totp/GitHub:dev%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";
        var bytes = BuildQrPng(payload);
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var picker = new CapturingFileSystemPickerService(
            integration,
            null,
            openBinaryFile: new PickedBinaryFile("github.png", bytes));
        var editor = new AcceptingTotpEditorDialogService();
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: picker,
            totpEditorDialogService: editor);

        await viewModel.ScanTotpQrCommand.ExecuteAsync(null);

        var imported = Assert.Single(viewModel.TotpItems);
        var data = TotpDataResolver.ParseStoredItemData(imported.ItemData);
        Assert.NotNull(data);
        Assert.Equal("GitHub", data.Issuer);
        Assert.Equal("dev@example.com", data.AccountName);
        Assert.Equal("JBSWY3DPEHPK3PXP", data.Secret);
        Assert.Equal("QR images", Assert.Single(picker.OpenFileTypes).Name);
        Assert.Contains("*.png", Assert.Single(picker.OpenFileTypes).Patterns);
        Assert.All(bytes, value => Assert.Equal(0, value));
        Assert.NotNull(editor.Source);
    }

    [Fact]
    public void Hotp_presentation_does_not_expose_totp_countdown()
    {
        var item = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Counter service",
            ItemData = TotpDataResolver.ToItemData(new TotpData(
                "JBSWY3DPEHPK3PXP",
                "Example",
                "counter@example.com",
                OtpType: "HOTP",
                Counter: 7))
        };
        var viewModel = CreateViewModel(GetTempPath());

        viewModel.RefreshTotpDisplay(item);

        Assert.NotEqual("------", item.TotpCode);
        Assert.Equal("", item.TotpTimeRemaining);
        Assert.Equal(100, item.TotpProgress);
    }

    [Fact]
    public async Task Hotp_next_code_increments_and_persists_counter_for_unbound_item()
    {
        var item = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Counter service",
            ItemData = TotpDataResolver.ToItemData(new TotpData(
                "JBSWY3DPEHPK3PXP",
                "Example",
                "counter@example.com",
                OtpType: "HOTP",
                Counter: 7))
        };
        var viewModel = CreateViewModel(GetTempPath());
        viewModel.TotpItems.Add(item);

        Assert.True(viewModel.AdvanceTotpCommand.CanExecute(item));
        await viewModel.AdvanceTotpCommand.ExecuteAsync(item);

        var data = TotpDataResolver.ParseStoredItemData(item.ItemData);
        Assert.NotNull(data);
        Assert.Equal(8, data.Counter);
        Assert.Equal(viewModel.L.Format("GeneratedNextTotpFormat", item.Title), viewModel.StatusMessage);
    }

    [Fact]
    public void Hotp_next_code_is_disabled_for_password_bound_item()
    {
        var item = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Bound counter",
            BoundPasswordId = 42,
            ItemData = TotpDataResolver.ToItemData(new TotpData(
                "JBSWY3DPEHPK3PXP",
                "Example",
                "counter@example.com",
                OtpType: "HOTP",
                Counter: 7))
        };
        var viewModel = CreateViewModel(GetTempPath());

        Assert.False(viewModel.AdvanceTotpCommand.CanExecute(item));
    }

    private static byte[] BuildQrPng(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        return qrCode.GetGraphic(8);
    }

    private sealed class AcceptingTotpEditorDialogService : ITotpEditorDialogService
    {
        public SecureItem? Source { get; private set; }

        public Task<TotpEditorViewModel?> ShowAsync(SecureItem? item, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Source = item;
            return Task.FromResult<TotpEditorViewModel?>(
                item is null ? null : new TotpEditorViewModel(new LocalizationService(), item));
        }
    }
}
