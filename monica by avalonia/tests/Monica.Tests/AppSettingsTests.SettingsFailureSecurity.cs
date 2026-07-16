using System.Reflection;
using Monica.App.Services;
using Monica.Core.Services;
using Monica.Data.Repositories;
using Monica.Data.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public void Settings_failure_messages_are_actionable_in_english_and_chinese()
    {
        var localization = new LocalizationService();
        var keys = new[]
        {
            "GitHubRepositoryOpenFailed",
            "ClearVaultDataFailed",
            "ChangeMasterPasswordFailed",
            "ResetMasterPasswordFailed",
            "SecurityQuestionsSaveFailed",
            "SettingsSaveFailed"
        };

        Assert.All(keys, key => Assert.Contains("try again", localization.Get(key), StringComparison.OrdinalIgnoreCase));

        localization.SetLanguage("zh-CN");

        Assert.All(keys, key => Assert.Contains("重试", localization.Get(key), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_failure_changing_master_password_keeps_raw_exception_details_out_of_status()
    {
        const string rawFailure = @"SQLITE_CANTOPEN C:\Users\joyins\Private\monica.db";
        var viewModel = CreateViewModel(
            GetTempPath(),
            masterPasswordMaintenanceService: new ThrowingMasterPasswordMaintenanceService(rawFailure));
        await viewModel.InitializeAsync();
        viewModel.IsUnlocked = true;
        viewModel.CurrentMasterPassword = "old password";
        viewModel.NewMasterPassword = "new password";
        viewModel.ConfirmNewMasterPassword = "new password";

        await viewModel.ChangeMasterPasswordCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ChangeMasterPasswordFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("joyins", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_failure_result_changing_master_password_keeps_raw_service_details_out_of_status()
    {
        const string rawFailure = @"Failed to decrypt C:\Users\joyins\Private\monica.db";
        var maintenance = new CapturingMasterPasswordMaintenanceService(
            MasterPasswordMaintenanceResult.Failure(rawFailure));
        var viewModel = CreateViewModel(GetTempPath(), masterPasswordMaintenanceService: maintenance);
        await viewModel.InitializeAsync();
        viewModel.IsUnlocked = true;
        viewModel.CurrentMasterPassword = "old password";
        viewModel.NewMasterPassword = "new password";
        viewModel.ConfirmNewMasterPassword = "new password";

        await viewModel.ChangeMasterPasswordCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ChangeMasterPasswordFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("monica.db", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_validation_wrong_master_password_uses_typed_failure_reason_not_service_text()
    {
        const string diagnosticDetail = "凭据校验失败 / private diagnostic detail";
        var maintenance = new CapturingMasterPasswordMaintenanceService(
            MasterPasswordMaintenanceResult.Failure(
                diagnosticDetail,
                MasterPasswordMaintenanceFailureReason.CurrentPasswordIncorrect));
        var viewModel = CreateViewModel(GetTempPath(), masterPasswordMaintenanceService: maintenance);
        await viewModel.InitializeAsync();
        viewModel.IsUnlocked = true;
        viewModel.CurrentMasterPassword = "wrong password";
        viewModel.NewMasterPassword = "new password";
        viewModel.ConfirmNewMasterPassword = "new password";

        await viewModel.ChangeMasterPasswordCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("WrongMasterPassword"), viewModel.StatusMessage);
        Assert.DoesNotContain(diagnosticDetail, viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_failure_resetting_master_password_keeps_raw_exception_details_out_of_status()
    {
        const string rawFailure = @"Key rotation failed for C:\Users\joyins\Private\monica.db";
        var viewModel = CreateViewModel(
            GetTempPath(),
            masterPasswordMaintenanceService: new ThrowingMasterPasswordMaintenanceService(rawFailure));
        await ConfigureSecurityRecoveryAsync(viewModel);

        await viewModel.ResetMasterPasswordWithSecurityQuestionsCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ResetMasterPasswordFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("joyins", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_failure_result_resetting_master_password_keeps_raw_service_details_out_of_status()
    {
        const string rawFailure = @"Failed to save re-encrypted vault data: C:\Users\joyins\Private\monica.db";
        var maintenance = new CapturingMasterPasswordMaintenanceService(
            MasterPasswordMaintenanceResult.Failure(rawFailure));
        var viewModel = CreateViewModel(GetTempPath(), masterPasswordMaintenanceService: maintenance);
        await ConfigureSecurityRecoveryAsync(viewModel);

        await viewModel.ResetMasterPasswordWithSecurityQuestionsCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ResetMasterPasswordFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("monica.db", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_failure_opening_repository_keeps_raw_platform_details_out_of_status()
    {
        const string rawFailure = @"Access denied launching C:\Users\joyins\Private\browser.exe";
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.ExternalLinks, "External links work.")
        ]);
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            externalLinkService: new ThrowingSettingsExternalLinkService(rawFailure));
        await viewModel.InitializeAsync();

        await viewModel.OpenGitHubRepositoryCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("GitHubRepositoryOpenFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("browser.exe", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_failure_saving_preferences_keeps_raw_storage_details_out_of_status()
    {
        const string rawFailure = @"Access denied writing C:\Users\joyins\Private\settings.json";
        var settings = new ThrowingSettingsPersistenceService(rawFailure);
        var viewModel = CreateViewModel(GetTempPath(), settingsService: settings);
        await viewModel.InitializeAsync();

        viewModel.StartupSection = "Notes";
        await settings.SaveAttempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForStatusAsync(viewModel, viewModel.L.Get("SettingsSaveFailed"));

        Assert.Equal(viewModel.L.Get("SettingsSaveFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.json", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_failure_clearing_vault_keeps_raw_database_details_out_of_status()
    {
        const string rawFailure = @"SQLITE_BUSY C:\Users\joyins\Private\monica.db";
        var repository = ThrowingClearVaultRepositoryProxy.Create(rawFailure);
        var viewModel = CreateViewModel(
            GetTempPath(),
            repository: repository,
            confirmationDialogService: new ApprovingConfirmationDialogService());
        viewModel.IsUnlocked = true;

        await viewModel.ClearVaultDataCommand.ExecuteAsync("all");

        Assert.Equal(viewModel.L.Get("ClearVaultDataFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("monica.db", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_validation_saving_duplicate_security_questions_is_localized_and_actionable()
    {
        var viewModel = CreateViewModel(GetTempPath());
        await viewModel.InitializeAsync();
        viewModel.SecurityRecoveryEnabled = true;
        viewModel.SecurityQuestion1Id = 11;
        viewModel.SecurityQuestion1Answer = "Tiga";
        viewModel.SecurityQuestion2Id = 11;
        viewModel.SecurityQuestion2Answer = "Ultraman";

        await viewModel.SaveSecurityQuestionsCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("SecurityQuestionsMustDiffer"), viewModel.StatusMessage);
        Assert.Contains("different", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsSavingSecurityQuestions);
    }

    private static async Task ConfigureSecurityRecoveryAsync(Monica.App.ViewModels.MainWindowViewModel viewModel)
    {
        await viewModel.InitializeAsync();
        viewModel.IsUnlocked = true;
        viewModel.SecurityRecoveryEnabled = true;
        viewModel.SecurityQuestion1Id = 11;
        viewModel.SecurityQuestion1Answer = "Tiga";
        viewModel.SecurityQuestion2Id = 1;
        viewModel.SecurityQuestion2Answer = "Monica";
        await viewModel.SaveSecurityQuestionsCommand.ExecuteAsync(null);
        viewModel.SecurityRecoveryAnswer1 = "tiga";
        viewModel.SecurityRecoveryAnswer2 = "monica";
        viewModel.RecoveryNewMasterPassword = "new password";
        viewModel.RecoveryConfirmNewMasterPassword = "new password";
    }

    private static async Task WaitForStatusAsync(
        Monica.App.ViewModels.MainWindowViewModel viewModel,
        string expectedStatus)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (string.Equals(viewModel.StatusMessage, expectedStatus, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private sealed class ThrowingMasterPasswordMaintenanceService(string message) : IMasterPasswordMaintenanceService
    {
        public Task<MasterPasswordMaintenanceResult> ChangeMasterPasswordAsync(
            string currentPassword,
            string newPassword,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MasterPasswordMaintenanceResult>(new IOException(message));

        public Task<MasterPasswordMaintenanceResult> ResetMasterPasswordFromUnlockedVaultAsync(
            string newPassword,
            CancellationToken cancellationToken = default) =>
            Task.FromException<MasterPasswordMaintenanceResult>(new IOException(message));
    }

    private sealed class ThrowingSettingsExternalLinkService(string message) : IExternalLinkService
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.ExternalLinks,
            "Test external links are available.");

        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException(message));
    }

    private sealed class ThrowingSettingsPersistenceService(string message) : IAppSettingsService
    {
        public DesktopAppSettings Current { get; } = new();
        public TaskCompletionSource SaveAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SaveAttempted.TrySetResult();
            return Task.FromException(new IOException(message));
        }

        public Task ClearSensitiveCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public IReadOnlyDictionary<string, bool> GetFeatureToggles() => Current.FeatureToggles;

        public bool IsFeatureEnabled(string featureKey) => Current.FeatureToggles.GetValueOrDefault(featureKey);

        public void SetFeatureEnabled(string featureKey, bool isEnabled) =>
            Current.FeatureToggles[featureKey] = isEnabled;
    }

    private class ThrowingClearVaultRepositoryProxy : DispatchProxy
    {
        private string _message = "";

        public static IMonicaRepository Create(string message)
        {
            var repository = Create<IMonicaRepository, ThrowingClearVaultRepositoryProxy>();
            ((ThrowingClearVaultRepositoryProxy)(object)repository)._message = message;
            return repository;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IMonicaRepository.ClearVaultDataAsync))
            {
                return Task.FromException(new IOException(_message));
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
