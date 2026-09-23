using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

/// <summary>
/// The emergency kit is the recovery copy that outlives a forgotten master password: an encrypted
/// snapshot of the vault sealed with its own passphrase, which the app never stores. Restoring needs
/// a vault to re-seal into, so both directions run unlocked — the kit is what you prepare before the
/// password is gone and what you open into a freshly reset vault afterwards.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private static readonly PlatformFilePickerFileType[] EmergencyKitFileTypes =
    [
        new("Monica Emergency Kit", ["*.json"])
    ];

    [ObservableProperty]
    private string _emergencyKitPassphrase = "";

    [ObservableProperty]
    private string _emergencyKitPassphraseConfirm = "";

    [ObservableProperty]
    private string _emergencyKitRestorePassphrase = "";

    [ObservableProperty]
    private bool _isCreatingEmergencyKit;

    [ObservableProperty]
    private bool _isRestoringEmergencyKit;

    public string EmergencyKitTitle => _localization.Get("EmergencyKit");
    public string EmergencyKitDescription => _localization.Get("EmergencyKitDescription");
    public string EmergencyKitPassphraseText => _localization.Get("EmergencyKitPassphrase");
    public string EmergencyKitPassphraseConfirmText => _localization.Get("EmergencyKitPassphraseConfirm");
    public string EmergencyKitCreateActionText => _localization.Get("EmergencyKitCreateAction");
    public string EmergencyKitRestoreActionText => _localization.Get("EmergencyKitRestoreAction");
    public string EmergencyKitRestoreDescription => _localization.Get("EmergencyKitRestoreDescription");

    public string EmergencyKitStatusText
    {
        get
        {
            var settings = _settingsService.Current;
            if (string.IsNullOrWhiteSpace(settings.EmergencyKitLastExportedAtUtc)
                || string.IsNullOrWhiteSpace(settings.EmergencyKitLastFileName))
            {
                return _localization.Get("EmergencyKitNeverExported");
            }

            return _localization.Format(
                "EmergencyKitLastExportedFormat",
                FormatEmergencyKitTimestamp(settings.EmergencyKitLastExportedAtUtc),
                settings.EmergencyKitLastFileName);
        }
    }

    public bool CanRunEmergencyKit => IsUnlocked && !IsSecurityMaintenanceBusy;

    partial void OnIsCreatingEmergencyKitChanged(bool value) => RaiseSecurityMaintenanceState();
    partial void OnIsRestoringEmergencyKitChanged(bool value) => RaiseSecurityMaintenanceState();

    [RelayCommand]
    private async Task CreateEmergencyKitAsync()
    {
        if (!TryValidateEmergencyKitPassphrase(out var passphrase))
        {
            return;
        }

        if (!TryBeginSecurityMaintenance(() => IsCreatingEmergencyKit = true))
        {
            return;
        }

        SetStatusMessage("EmergencyKitInProgress");
        try
        {
            if (!await AuthorizeSensitiveExportAsync(grantFileExport: false))
            {
                return;
            }

            var json = await BuildMonicaJsonExportAsync(
                includePasswords: true,
                includeTotp: true,
                includeNotes: true,
                includeCards: true,
                includeDocuments: true,
                includeImages: true,
                includeCategories: true);
            var payload = await _webDavBackupCryptoService.EncryptAsync(json, passphrase);

            // The kit is worthless if it cannot be opened later, and the only honest moment to find
            // that out is before handing it over: a passphrase typo would otherwise be discovered in
            // the one situation the kit exists for.
            if (!string.Equals(await _webDavBackupCryptoService.DecryptAsync(payload, passphrase), json, StringComparison.Ordinal))
            {
                ReportSettingsFailure(
                    "Emergency kit self-check failed",
                    "EmergencyKitSelfCheckFailed",
                    "round trip mismatch");
                return;
            }

            var suggestedFileName = $"monica_emergency_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.monica.enc.json";
            var savedFileName = await _fileSystemPickerService.SaveTextFileAsync(
                _localization.Get("EmergencyKitCreateAction"),
                suggestedFileName,
                payload,
                EmergencyKitFileTypes);
            if (savedFileName is null)
            {
                ClearStatusMessage();
                return;
            }

            UpdateSettings(settings =>
            {
                settings.EmergencyKitLastExportedAtUtc = DateTimeOffset.UtcNow.ToString("O");
                settings.EmergencyKitLastFileName = savedFileName;
            });
            OnPropertyChanged(nameof(EmergencyKitStatusText));
            SetStatusNotice("EmergencyKitCreatedFormat", savedFileName);
        }
        catch (Exception ex)
        {
            ReportSettingsFailure("Emergency kit creation failed", "EmergencyKitCreateFailed", ex);
        }
        finally
        {
            EmergencyKitPassphrase = "";
            EmergencyKitPassphraseConfirm = "";
            EndSecurityMaintenance(() => IsCreatingEmergencyKit = false);
        }
    }

    [RelayCommand]
    private async Task RestoreEmergencyKitAsync()
    {
        if (!IsUnlocked)
        {
            SetStatusFailure("VaultLocked");
            return;
        }

        if (string.IsNullOrWhiteSpace(EmergencyKitRestorePassphrase))
        {
            SetStatusFailure("EmergencyKitPassphraseRequired");
            return;
        }

        if (!TryBeginSecurityMaintenance(() => IsRestoringEmergencyKit = true))
        {
            return;
        }

        var passphrase = EmergencyKitRestorePassphrase;
        SetStatusMessage("EmergencyKitRestoreInProgress");
        try
        {
            var file = await _fileSystemPickerService.OpenTextFileAsync(
                _localization.Get("EmergencyKitRestoreAction"),
                EmergencyKitFileTypes);
            if (file is null)
            {
                ClearStatusMessage();
                return;
            }

            var json = await _webDavBackupCryptoService.DecryptAsync(file.Content, passphrase);
            if (!await ConfirmRestoreEmergencyKitAsync(file.FileName))
            {
                return;
            }

            var result = await ImportMonicaJsonAsync(json);
            SetStatusNotice(
                "EmergencyKitRestoredFormat",
                file.FileName,
                result.Passwords,
                result.SecureItems,
                result.Categories);
        }
        catch (WebDavBackupCryptoException)
        {
            // Never report *why* the crypto layer refused: the distinguishing detail is the user's
            // own passphrase, and a wrong-passphrase failure is all the caller can act on.
            SetStatusFailure("EmergencyKitRestoreWrongPassphrase");
        }
        catch (Exception ex)
        {
            ReportSettingsFailure("Emergency kit restore failed", "EmergencyKitRestoreFailed", ex);
        }
        finally
        {
            EmergencyKitRestorePassphrase = "";
            EndSecurityMaintenance(() => IsRestoringEmergencyKit = false);
        }
    }

    private bool TryValidateEmergencyKitPassphrase(out string passphrase)
    {
        passphrase = EmergencyKitPassphrase;
        if (!IsUnlocked)
        {
            SetStatusFailure("VaultLocked");
            return false;
        }

        if (string.IsNullOrWhiteSpace(passphrase) || string.IsNullOrWhiteSpace(EmergencyKitPassphraseConfirm))
        {
            SetStatusFailure("EmergencyKitPassphraseRequired");
            return false;
        }

        if (!VaultMasterPasswordPolicy.MeetsMinimumLength(passphrase))
        {
            SetStatusFailure("EmergencyKitPassphraseTooShort");
            return false;
        }

        if (!string.Equals(passphrase, EmergencyKitPassphraseConfirm, StringComparison.Ordinal))
        {
            SetStatusFailure("EmergencyKitPassphrasesMismatch");
            return false;
        }

        return true;
    }

    private Task<bool> ConfirmRestoreEmergencyKitAsync(string fileName) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("EmergencyKitRestoreConfirmationTitle"),
            _localization.Format("EmergencyKitRestoreConfirmationMessageFormat", fileName),
            _localization.Get("RestoreItem"),
            _localization.Cancel);

    private string FormatEmergencyKitTimestamp(string exportedAtUtc)
    {
        if (!DateTimeOffset.TryParse(
                exportedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var exported))
        {
            return exportedAtUtc;
        }

        return exported.ToLocalTime().ToString("yyyy/MM/dd HH:mm", _localization.Culture);
    }
}
