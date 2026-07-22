using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IReadOnlyList<PlatformCapability> _sourceCapabilities;
    private readonly IReadOnlyList<PlatformIntegrationCapability> _sourcePlatformIntegrationCapabilities;
    private readonly IExternalLinkService _externalLinkService;
    private readonly IFileSystemPickerService _fileSystemPickerService;

    public ObservableCollection<LocalizedPlatformIntegrationCapability> PlatformIntegrationCapabilities { get; } = [];
    public ObservableCollection<LocalizedPlatformCapability> Capabilities { get; } = [];

    public string PlatformName { get; }
    public string PlatformIntegrationsTitle => _localization.Get("PlatformIntegrations");
    public string PlatformIntegrationSummaryText => _localization.Format(
        "PlatformIntegrationsDescriptionFormat",
        PlatformName,
        PlatformIntegrationCapabilities.Count(item => item.IsUsable),
        PlatformIntegrationCapabilities.Count);
    public bool CanUseTrayIntegration => IsPlatformIntegrationUsable(PlatformFeatureKeys.Tray);
    public bool CanUseGlobalHotkeyIntegration => IsPlatformIntegrationUsable(PlatformFeatureKeys.GlobalHotkey);
    public bool CanUseBrowserBridgeIntegration => IsPlatformIntegrationUsable(PlatformFeatureKeys.BrowserBridge);
    public bool CanOpenExternalLinks => IsPlatformIntegrationUsable(PlatformFeatureKeys.ExternalLinks);
    public bool CanUseFilePicker => _fileSystemPickerService.Capability.IsUsable;
    public string TrayIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.Tray);
    public string GlobalHotkeyIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.GlobalHotkey);
    public string BrowserBridgeIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.BrowserBridge);
    public string ExternalLinksIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.ExternalLinks);
    public string FilePickerIntegrationStatusText => FormatPlatformIntegrationStatus(PlatformFeatureKeys.FilePicker);

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _quickSearchEnabled = true;

    [ObservableProperty]
    private string _quickSearchHotkey = "Ctrl+Shift+Space";

    [ObservableProperty]
    private bool _browserIntegrationEnabled;

    [ObservableProperty]
    private int _browserIntegrationPort = 49152;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlobalHotkeyIntegrationStatusText))]
    private string _globalHotkeyRegistrationError = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrowserBridgeIntegrationStatusText))]
    private bool _browserBridgeIsRunning;

    [ObservableProperty]
    private string _browserIntegrationSessionToken = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrowserBridgeIntegrationStatusText))]
    private string _browserBridgeRuntimeError = "";

    internal void SetGlobalHotkeyRegistrationError(string error) => GlobalHotkeyRegistrationError = error;

    internal void SetBrowserBridgeRuntimeState(bool isRunning, string sessionToken, string error)
    {
        BrowserBridgeIsRunning = isRunning;
        BrowserIntegrationSessionToken = sessionToken;
        BrowserBridgeRuntimeError = error;
        CopyBrowserIntegrationTokenCommand.NotifyCanExecuteChanged();
    }
}
