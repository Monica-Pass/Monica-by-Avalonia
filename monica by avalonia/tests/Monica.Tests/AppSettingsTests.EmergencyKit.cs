using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    private const string ProbeUsername = "probe@example.com";
    private const string ProbePassword = "probe-plaintext-not-a-secret";
    private const string ProbeTotp = "JBSWY3DPEHPK3PXP";
    private const string KitPassphrase = "kit passphrase value";
    private const string KitFileName = "monica_emergency_probe.monica.enc.json";

    [Fact]
    public async Task Emergency_kit_export_seals_plaintext_behind_the_passphrase_only()
    {
        var (viewModel, picker, crypto) = CreateEmergencyKitFixture(out var repository);
        await SeedProbePasswordAsync(repository);
        await CreateProbeKitAsync(viewModel);

        Assert.NotEmpty(picker.SavedContent);
        Assert.DoesNotContain(ProbePassword, picker.SavedContent, StringComparison.Ordinal);
        Assert.DoesNotContain(ProbeUsername, picker.SavedContent, StringComparison.Ordinal);
        Assert.DoesNotContain(ProbeTotp, picker.SavedContent, StringComparison.Ordinal);
        Assert.DoesNotContain(KitPassphrase, picker.SavedContent, StringComparison.Ordinal);

        // The kit has to actually carry the vault, or the seal proves nothing.
        var unlocked = await crypto.DecryptAsync(picker.SavedContent, KitPassphrase);
        Assert.Contains(ProbePassword, unlocked, StringComparison.Ordinal);
        Assert.Contains(ProbeUsername, unlocked, StringComparison.Ordinal);

        Assert.Equal("", viewModel.EmergencyKitPassphrase);
        Assert.Equal("", viewModel.EmergencyKitPassphraseConfirm);
        Assert.False(viewModel.IsCreatingEmergencyKit);
        Assert.Equal(viewModel.L.Format("EmergencyKitCreatedFormat", KitFileName), viewModel.StatusMessage);
        Assert.Contains(KitFileName, viewModel.EmergencyKitStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Emergency_kit_round_trips_into_another_vault()
    {
        var (source, picker, _) = CreateEmergencyKitFixture(out var sourceRepository);
        await SeedProbePasswordAsync(sourceRepository);
        await CreateProbeKitAsync(source);

        var target = CreateKitRestoreTarget(picker.SavedContent, out _, out var targetRepository);
        target.EmergencyKitRestorePassphrase = KitPassphrase;
        await target.RestoreEmergencyKitCommand.ExecuteAsync(null);

        var restored = Assert.Single(await targetRepository.GetPasswordsAsync());
        Assert.Equal("Emergency probe", restored.Title);
        Assert.Equal(ProbeUsername, restored.Username);
        // The kit re-imports through the normal write path, so the secret is sealed again on the way in.
        Assert.NotEqual(ProbePassword, restored.Password);
        Assert.Equal("", target.EmergencyKitRestorePassphrase);
        Assert.False(target.IsRestoringEmergencyKit);
        Assert.Equal(
            target.L.Format("EmergencyKitRestoredFormat", KitFileName, 1, 0, 0),
            target.StatusMessage);
    }

    [Fact]
    public async Task Emergency_kit_restore_reports_progress_as_opening_not_encrypting()
    {
        var (source, picker, _) = CreateEmergencyKitFixture(out var sourceRepository);
        await SeedProbePasswordAsync(sourceRepository);
        await CreateProbeKitAsync(source);

        var target = CreateKitRestoreTarget(picker.SavedContent, out var targetPicker, out _);
        string? progressAtPickTime = null;
        targetPicker.OnOpenText = () => progressAtPickTime = target.StatusMessage;
        target.EmergencyKitRestorePassphrase = KitPassphrase;
        await target.RestoreEmergencyKitCommand.ExecuteAsync(null);

        Assert.Equal(target.L.Get("EmergencyKitRestoreInProgress"), progressAtPickTime);
        Assert.NotEqual(target.L.Get("EmergencyKitInProgress"), progressAtPickTime);
    }

    [Fact]
    public async Task Emergency_kit_restore_with_wrong_passphrase_imports_nothing_and_hides_the_reason()
    {
        var (source, picker, _) = CreateEmergencyKitFixture(out var sourceRepository);
        await SeedProbePasswordAsync(sourceRepository);
        await CreateProbeKitAsync(source);

        var target = CreateKitRestoreTarget(picker.SavedContent, out _, out var targetRepository);
        target.EmergencyKitRestorePassphrase = "wrong kit passphrase";
        await target.RestoreEmergencyKitCommand.ExecuteAsync(null);

        Assert.Empty(await targetRepository.GetPasswordsAsync());
        Assert.Equal(target.L.Get("EmergencyKitRestoreWrongPassphrase"), target.StatusMessage);
        // The crypto layer names its failure after the ciphertext check; none of that, nor the
        // passphrase itself, may reach the status line.
        Assert.DoesNotContain("ciphertext", target.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passphrase value", target.StatusMessage, StringComparison.Ordinal);
        Assert.Equal("", target.EmergencyKitRestorePassphrase);
    }

    [Theory]
    [InlineData("", "", "EmergencyKitPassphraseRequired")]
    [InlineData("short1", "short1", "EmergencyKitPassphraseTooShort")]
    [InlineData(KitPassphrase, "different passphrase", "EmergencyKitPassphrasesMismatch")]
    public async Task Emergency_kit_export_refuses_weak_input_without_writing(
        string passphrase,
        string confirmation,
        string expectedKey)
    {
        var (viewModel, picker, _) = CreateEmergencyKitFixture(out var repository);
        await SeedProbePasswordAsync(repository);

        viewModel.EmergencyKitPassphrase = passphrase;
        viewModel.EmergencyKitPassphraseConfirm = confirmation;
        await viewModel.CreateEmergencyKitCommand.ExecuteAsync(null);

        Assert.Equal("", picker.SavedContent);
        Assert.Equal(viewModel.L.Get(expectedKey), viewModel.StatusMessage);
        Assert.False(viewModel.IsCreatingEmergencyKit);
        Assert.Equal(viewModel.L.Get("EmergencyKitNeverExported"), viewModel.EmergencyKitStatusText);
    }

    [Fact]
    public async Task Emergency_kit_commands_refuse_while_the_vault_is_locked()
    {
        var (viewModel, picker, _) = CreateEmergencyKitFixture(out _);
        viewModel.IsUnlocked = false;

        viewModel.EmergencyKitPassphrase = KitPassphrase;
        viewModel.EmergencyKitPassphraseConfirm = KitPassphrase;
        await viewModel.CreateEmergencyKitCommand.ExecuteAsync(null);
        Assert.Equal(viewModel.L.Get("VaultLocked"), viewModel.StatusMessage);
        Assert.Equal("", picker.SavedContent);

        viewModel.EmergencyKitRestorePassphrase = KitPassphrase;
        await viewModel.RestoreEmergencyKitCommand.ExecuteAsync(null);
        Assert.Equal(viewModel.L.Get("VaultLocked"), viewModel.StatusMessage);
    }

    [Fact]
    public async Task Emergency_kit_metadata_survives_a_settings_reload()
    {
        var path = GetTempPath();
        var settings = new AppSettingsService(path);
        settings.Current.EmergencyKitLastExportedAtUtc = "2026-09-23T08:15:30.0000000+00:00";
        settings.Current.EmergencyKitLastFileName = KitFileName;
        await settings.SaveAsync();

        var reloaded = new AppSettingsService(path);
        await reloaded.LoadAsync();

        Assert.Equal(KitFileName, reloaded.Current.EmergencyKitLastFileName);
        Assert.Equal("2026-09-23T08:15:30.0000000+00:00", reloaded.Current.EmergencyKitLastExportedAtUtc);
    }

    private static async Task CreateProbeKitAsync(MainWindowViewModel source)
    {
        source.EmergencyKitPassphrase = KitPassphrase;
        source.EmergencyKitPassphraseConfirm = KitPassphrase;
        await source.CreateEmergencyKitCommand.ExecuteAsync(null);
    }

    private static MainWindowViewModel CreateKitRestoreTarget(
        string kitPayload,
        out CapturingFileSystemPickerService picker,
        out MonicaRepository repository)
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        picker = new CapturingFileSystemPickerService(
            new PlatformIntegrationService(),
            new PickedTextFile(KitFileName, kitPayload));
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: FilePickerIntegration(),
            fileSystemPickerService: picker,
            repository: repository,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        viewModel.IsUnlocked = true;
        return viewModel;
    }

    private static (MainWindowViewModel ViewModel, CapturingFileSystemPickerService Picker, WebDavBackupCryptoService Crypto)
        CreateEmergencyKitFixture(out MonicaRepository repository)
    {
        var factory = new SqliteConnectionFactory(TestTempPaths.CreateFilePath(".db"));
        repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var picker = new CapturingFileSystemPickerService(
            new PlatformIntegrationService(),
            new PickedTextFile(KitFileName, ""),
            KitFileName);
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: FilePickerIntegration(),
            fileSystemPickerService: picker,
            repository: repository,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        viewModel.IsUnlocked = true;
        return (viewModel, picker, new WebDavBackupCryptoService());
    }

    private static PlatformIntegrationService FilePickerIntegration() =>
        new("TestOS", [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);

    private static async Task SeedProbePasswordAsync(MonicaRepository repository) =>
        await repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Emergency probe",
            Username = ProbeUsername,
            Password = ProbePassword,
            Website = "https://probe.example.com",
            AuthenticatorKey = ProbeTotp
        });
}
