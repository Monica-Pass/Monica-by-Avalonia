using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Data;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IVaultUnlockCoordinator _vaultUnlockCoordinator;
    private LegacyVaultDetection _legacyVaultDetection = LegacyVaultDetection.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private bool _isUnlocked;

    [ObservableProperty]
    private bool _isVaultInitialized;

    [ObservableProperty]
    private string _masterPassword = "";

    [ObservableProperty]
    private string _confirmMasterPassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecoverableStatusMessage))]
    private bool _hasPendingLegacyBusinessData;

    public string LoginTitle => IsVaultInitialized
        ? _localization.UnlockMonica
        : _localization.CreateMonicaVault;

    public string LoginDescription => IsVaultInitialized
        ? _localization.UnlockDescription
        : _localization.CreateVaultDescription;

    public string LoginButtonText => IsVaultInitialized
        ? _localization.Unlock
        : _localization.CreateVault;

    public bool HasLegacyVaultImportPrompt => _legacyVaultDetection.RequiresImport;

    public string LegacyVaultImportPromptText => _legacyVaultDetection.RequiresImport
        ? _localization.Format("LegacyVaultImportPromptFormat", _legacyVaultDetection.DatabasePath)
        : "";

    [RelayCommand]
    public async Task InitializeAsync()
    {
        try
        {
            AppDiagnostics.Info("Initialize started");
            await _settingsService.LoadAsync();
            ApplySettings(_settingsService.Current);
            var initialization = await AppDiagnostics.MeasureAsync(
                "Vault metadata initialize",
                () => _vaultUnlockCoordinator.InitializeAsync());
            _legacyVaultDetection = initialization.LegacyVaultDetection;
            RaiseLegacyVaultImportPrompt();
            if (_legacyVaultDetection.RequiresImport)
            {
                IsVaultInitialized = false;
                StatusMessage = _localization.Get("LegacyVaultImportRequired");
                return;
            }

            IsVaultInitialized = initialization.IsVaultInitialized;
            StatusMessage = IsVaultInitialized
                ? _localization.Get("VaultLocked")
                : _localization.Get("FirstRunCreateMasterPassword");
            AppDiagnostics.Info(
                $"Initialize completed. initialized={IsVaultInitialized}, " +
                $"legacyImportRequired={_legacyVaultDetection.RequiresImport}");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Initialize failed", ex);
            IsVaultInitialized = DefaultVaultDatabaseExists();
            StatusMessage = _localization.Format("VaultMetadataLoadFailedFormat", ex.Message);
        }
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        HasPendingLegacyBusinessData = false;
        AppDiagnostics.Info(
            $"Unlock requested. initialized={IsVaultInitialized}, " +
            $"legacyImportRequired={_legacyVaultDetection.RequiresImport}");
        var result = await AppDiagnostics.MeasureAsync(
            "Unlock credential verification",
            () => _vaultUnlockCoordinator.UnlockOrCreateAsync(
                MasterPassword,
                ConfirmMasterPassword,
                _legacyVaultDetection));
        AppDiagnostics.Info($"Unlock result={result.Status}");

        switch (result.Status)
        {
            case VaultUnlockStatus.MissingPassword:
            case VaultUnlockStatus.LegacyImportRequired:
            case VaultUnlockStatus.PasswordTooShort:
            case VaultUnlockStatus.ConfirmationMismatch:
                IsVaultInitialized = result.IsVaultInitialized;
                StatusMessage = _localization.Get(result.MessageKey);
                return;
            case VaultUnlockStatus.WrongPassword:
                IsVaultInitialized = result.IsVaultInitialized;
                IsUnlocked = false;
                StatusMessage = _localization.Get(result.MessageKey);
                MasterPassword = "";
                ConfirmMasterPassword = "";
                return;
            case VaultUnlockStatus.Failed:
                IsVaultInitialized = result.IsVaultInitialized || DefaultVaultDatabaseExists();
                IsUnlocked = false;
                StatusMessage = _localization.Format(result.MessageKey, result.Error?.Message ?? "");
                return;
            case VaultUnlockStatus.CreatedAndUnlocked:
            case VaultUnlockStatus.Unlocked:
                IsVaultInitialized = result.IsVaultInitialized;
                IsUnlocked = true;
                HasPendingLegacyBusinessData = result.LegacyBusinessDataPending;
                MasterPassword = "";
                ConfirmMasterPassword = "";
                StatusMessage = $"{_localization.Get(result.MessageKey)}，正在加载保险库数据...";
                _ = LoadAfterUnlockAsync();
                return;
            default:
                IsUnlocked = false;
                StatusMessage = _localization.Format("UnlockFailedFormat", result.Status.ToString());
                return;
        }
    }

    partial void OnIsVaultInitializedChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginTitle));
        OnPropertyChanged(nameof(LoginDescription));
        OnPropertyChanged(nameof(LoginButtonText));
    }

    private void RaiseLegacyVaultImportPrompt()
    {
        OnPropertyChanged(nameof(HasLegacyVaultImportPrompt));
        OnPropertyChanged(nameof(LegacyVaultImportPromptText));
    }

    private static bool DefaultVaultDatabaseExists()
    {
        try
        {
            return File.Exists(MonicaAppDataPaths.GetDatabasePath());
        }
        catch
        {
            return false;
        }
    }
}
