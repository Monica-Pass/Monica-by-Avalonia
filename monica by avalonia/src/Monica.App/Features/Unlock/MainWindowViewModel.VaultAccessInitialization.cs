using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVaultAccessReady))]
    private bool _isVaultAccessInitializing = true;

    public bool IsVaultAccessReady => !IsVaultAccessInitializing;

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsVaultAccessInitializing = true;
        HasUnlockError = false;
        ClearStatusMessage();
        try
        {
            AppDiagnostics.Info("Initialize started");
            var settingsLoad = _settingsService.LoadAsync();
            var vaultInitialization = AppDiagnostics.MeasureAsync(
                "Vault metadata initialize",
                () => _vaultUnlockCoordinator.InitializeAsync());
            await Task.WhenAll(settingsLoad, vaultInitialization);
            ApplySettings(_settingsService.Current);
            var initialization = await vaultInitialization;
            _legacyVaultDetection = initialization.LegacyVaultDetection;
            RaiseLegacyVaultImportPrompt();
            if (_legacyVaultDetection.RequiresImport)
            {
                IsVaultInitialized = false;
                SetUnlockError("LegacyVaultImportRequired");
                return;
            }

            IsVaultInitialized = initialization.IsVaultInitialized;
            SetStatusMessage(IsVaultInitialized
                ? "VaultLocked"
                : "FirstRunCreateMasterPassword");
            AppDiagnostics.Info(
                $"Initialize completed. initialized={IsVaultInitialized}, " +
                $"legacyImportRequired={_legacyVaultDetection.RequiresImport}");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Initialize failed", ex);
            IsVaultInitialized = DefaultVaultDatabaseExists();
            ClearVaultAccessSecrets();
            SetUnlockError("VaultAccessInitializationFailed");
        }
        finally
        {
            IsVaultAccessInitializing = false;
        }
    }

    partial void OnIsVaultAccessInitializingChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginTitle));
        OnPropertyChanged(nameof(LoginDescription));
        OnPropertyChanged(nameof(LoginButtonText));
        RaiseCreateVaultPasswordGuidanceState();
        UnlockCommand.NotifyCanExecuteChanged();
    }
}
