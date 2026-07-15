namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnSelectedSyncPageChanged(string value)
    {
        if (!IsWorkspacePageSelected(value, "Import"))
        {
            ClearSensitiveImportBuffers();
        }

        if (!IsWorkspacePageSelected(value, "Export"))
        {
            ClearSensitiveExportPreviews();
        }
    }

    partial void OnWebDavEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.WebDavEnabled = value);
        RaiseSyncPageState();
        RefreshVaultSources();
        RefreshMdbxVaultState();
        RaiseShellStatus();
    }

    partial void OnWebDavServerUrlChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavServerUrl = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }

    partial void OnWebDavUsernameChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavUsername = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }

    partial void OnWebDavPasswordChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavPassword = value);
        RaiseSyncPageState();
    }

    partial void OnWebDavRemotePathChanged(string value)
    {
        UpdateSettings(settings => settings.WebDavRemotePath = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }

    partial void OnWebDavSyncOnStartupChanged(bool value)
    {
        UpdateSettings(settings => settings.WebDavSyncOnStartup = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }

    partial void OnWebDavSyncAfterChangesChanged(bool value)
    {
        UpdateSettings(settings => settings.WebDavSyncAfterChanges = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }

    partial void OnIsLoadingWebDavBackupsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWebDavBusy));
        RaiseSyncPageState();
    }

    partial void OnIsRunningWebDavBackupChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWebDavBusy));
        RaiseSyncPageState();
    }

    partial void OnWebDavBackupIncludePasswordsChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludePasswords = value);
    partial void OnWebDavBackupIncludeTotpChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeTotp = value);
    partial void OnWebDavBackupIncludeNotesChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeNotes = value);
    partial void OnWebDavBackupIncludeCardsChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeCards = value);
    partial void OnWebDavBackupIncludeDocumentsChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeDocuments = value);
    partial void OnWebDavBackupIncludeImagesChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeImages = value);
    partial void OnWebDavBackupIncludeCategoriesChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupIncludeCategories = value);
    partial void OnWebDavBackupEncryptionEnabledChanged(bool value) => UpdateWebDavBackupOption(settings => settings.WebDavBackupEncryptionEnabled = value);
    partial void OnWebDavBackupEncryptionPasswordChanged(string value) => UpdateSettings(settings => settings.WebDavBackupEncryptionPassword = value);

    partial void OnSyncConflictStrategyChanged(string value)
    {
        UpdateSettings(settings => settings.SyncConflictStrategy = value);
        RaiseSyncPageState();
        RefreshVaultSources();
    }

    partial void OnOneDriveEnabledChanged(bool value)
    {
        UpdateSettings(settings => settings.OneDriveEnabled = value);
        OnPropertyChanged(nameof(OneDriveConnectionStatusText));
        RaiseSyncPageState();
        RefreshMdbxVaultState();
    }
}
