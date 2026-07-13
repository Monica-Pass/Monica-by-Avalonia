using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (value && !CanUseTrayIntegration)
        {
            MinimizeToTray = false;
            return;
        }

        UpdateSettings(settings => settings.MinimizeToTray = value);
    }

    partial void OnQuickSearchEnabledChanged(bool value) => UpdateSettings(settings => settings.QuickSearchEnabled = value);
    partial void OnQuickSearchHotkeyChanged(string value) => UpdateSettings(settings => settings.QuickSearchHotkey = value);

    partial void OnBrowserIntegrationEnabledChanged(bool value)
    {
        if (value && !CanUseBrowserBridgeIntegration)
        {
            BrowserIntegrationEnabled = false;
            return;
        }

        UpdateSettings(settings => settings.BrowserIntegrationEnabled = value);
    }

    partial void OnBrowserIntegrationPortChanged(int value) => UpdateSettings(settings => settings.BrowserIntegrationPort = value);

    private void RefreshPlatformIntegrationCapabilities()
    {
        PlatformIntegrationCapabilities.Clear();
        foreach (var capability in _sourcePlatformIntegrationCapabilities)
        {
            var descriptionKey = $"Integration.{capability.Key}.Description";
            var localizedDescription = _localization.Get(descriptionKey);
            PlatformIntegrationCapabilities.Add(new LocalizedPlatformIntegrationCapability(
                capability.Key,
                _localization.Get($"Integration.{capability.Key}.Title"),
                localizedDescription == descriptionKey ? capability.Description : localizedDescription,
                LocalizeFeatureStatus(capability.Status),
                capability.UnsupportedReason ?? "",
                capability.Status));
        }

        RaisePlatformIntegrationState();
    }

    private void RaisePlatformIntegrationState()
    {
        OnPropertyChanged(nameof(PlatformIntegrationSummaryText));
        OnPropertyChanged(nameof(CanUseTrayIntegration));
        OnPropertyChanged(nameof(CanUseGlobalHotkeyIntegration));
        OnPropertyChanged(nameof(CanUseBrowserBridgeIntegration));
        OnPropertyChanged(nameof(CanOpenExternalLinks));
        OnPropertyChanged(nameof(CanUseFilePicker));
        OnPropertyChanged(nameof(TrayIntegrationStatusText));
        OnPropertyChanged(nameof(GlobalHotkeyIntegrationStatusText));
        OnPropertyChanged(nameof(BrowserBridgeIntegrationStatusText));
        OnPropertyChanged(nameof(ExternalLinksIntegrationStatusText));
        OnPropertyChanged(nameof(FilePickerIntegrationStatusText));
        OpenGitHubRepositoryCommand.NotifyCanExecuteChanged();
        OpenNoteReferenceCommand.NotifyCanExecuteChanged();
        ImportMonicaJsonFileCommand.NotifyCanExecuteChanged();
        ImportPasswordCsvFileCommand.NotifyCanExecuteChanged();
        ImportTotpCsvFileCommand.NotifyCanExecuteChanged();
        ImportNoteCsvFileCommand.NotifyCanExecuteChanged();
        ImportAegisJsonFileCommand.NotifyCanExecuteChanged();
        SaveMonicaJsonExportCommand.NotifyCanExecuteChanged();
        SavePasswordCsvExportCommand.NotifyCanExecuteChanged();
        SaveTotpCsvExportCommand.NotifyCanExecuteChanged();
        SaveNoteCsvExportCommand.NotifyCanExecuteChanged();
        SaveWalletCsvExportCommand.NotifyCanExecuteChanged();
        SaveAegisJsonExportCommand.NotifyCanExecuteChanged();
        ImportMarkdownNoteCommand.NotifyCanExecuteChanged();
        ExportCurrentNoteMarkdownCommand.NotifyCanExecuteChanged();
    }

    private void RefreshCapabilities()
    {
        Capabilities.Clear();
        foreach (var capability in _sourceCapabilities)
        {
            var canToggle = capability.Status is not (PlatformFeatureStatus.Unsupported or PlatformFeatureStatus.Planned);
            Capabilities.Add(new LocalizedPlatformCapability(
                capability.Key,
                _localization.Get($"Capability.{capability.Key}.Title"),
                _localization.Get($"Capability.{capability.Key}.Description"),
                LocalizeFeatureStatus(capability.Status),
                _settingsService.IsFeatureEnabled(capability.Key),
                canToggle,
                _localization.FeatureEnabled,
                _localization.FeatureDisabled,
                capability.UnsupportedReason ?? "",
                UpdateFeatureToggle));
        }
    }

    private void UpdateFeatureToggle(string key, bool isEnabled)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        _settingsService.SetFeatureEnabled(key, isEnabled);
        QueueSaveSettings();
    }

    private string LocalizeFeatureStatus(PlatformFeatureStatus status)
    {
        return status switch
        {
            PlatformFeatureStatus.Available => _localization.Available,
            PlatformFeatureStatus.DesktopEquivalent => _localization.DesktopEquivalent,
            PlatformFeatureStatus.PlatformLimited => _localization.PlatformLimited,
            PlatformFeatureStatus.Unsupported => _localization.Get("Unsupported"),
            PlatformFeatureStatus.Planned => _localization.Planned,
            _ => status.ToString()
        };
    }

    private bool IsPlatformIntegrationUsable(string key) => GetPlatformIntegration(key).IsUsable;

    private string FormatPlatformIntegrationStatus(string key)
    {
        var capability = GetPlatformIntegration(key);
        var status = LocalizeFeatureStatus(capability.Status);
        return string.IsNullOrWhiteSpace(capability.UnsupportedReason)
            ? status
            : $"{status}: {capability.UnsupportedReason}";
    }

    private PlatformIntegrationCapability GetPlatformIntegration(string key) =>
        _sourcePlatformIntegrationCapabilities.FirstOrDefault(
            item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? new PlatformIntegrationCapability(
            key,
            PlatformFeatureStatus.Unsupported,
            "This platform adapter has not declared this feature.",
            "This platform adapter has not declared this feature.");
}
