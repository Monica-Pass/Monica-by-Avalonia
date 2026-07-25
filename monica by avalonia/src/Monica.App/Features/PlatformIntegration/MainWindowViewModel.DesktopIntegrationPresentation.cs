namespace Monica.App.ViewModels;

public enum BrowserBridgeDesktopState
{
    Unavailable,
    Disabled,
    WaitingForUnlock,
    Starting,
    Running,
    Error
}

public sealed partial class MainWindowViewModel
{
    public string TrayIntegrationTaskStatusText => !CanUseTrayIntegration
        ? TrayIntegrationStatusText
        : _localization.Get(MinimizeToTray ? "DesktopTrayActiveStatus" : "DesktopTrayInactiveStatus");

    public bool HasGlobalHotkeyRegistrationError =>
        !string.IsNullOrWhiteSpace(GlobalHotkeyRegistrationError);

    public string GlobalQuickSearchStatusTitleText
    {
        get
        {
            if (!CanUseGlobalHotkeyIntegration)
            {
                return _localization.Get("QuickSearchStatusUnavailableTitle");
            }

            if (!QuickSearchEnabled)
            {
                return _localization.Get("QuickSearchStatusDisabledTitle");
            }

            return HasGlobalHotkeyRegistrationError
                ? _localization.Get("QuickSearchStatusErrorTitle")
                : _localization.Get("QuickSearchStatusActiveTitle");
        }
    }

    public string GlobalQuickSearchStatusDescriptionText
    {
        get
        {
            if (!CanUseGlobalHotkeyIntegration)
            {
                return GlobalHotkeyIntegrationStatusText;
            }

            if (!QuickSearchEnabled)
            {
                return _localization.Get("QuickSearchStatusDisabledDescription");
            }

            return HasGlobalHotkeyRegistrationError
                ? GlobalHotkeyRegistrationError
                : _localization.Format("QuickSearchStatusActiveDescriptionFormat", QuickSearchHotkey);
        }
    }

    public BrowserBridgeDesktopState BrowserBridgeDesktopStatus
    {
        get
        {
            if (!CanUseBrowserBridgeIntegration)
            {
                return BrowserBridgeDesktopState.Unavailable;
            }

            if (!BrowserIntegrationEnabled)
            {
                return BrowserBridgeDesktopState.Disabled;
            }

            if (!IsUnlocked)
            {
                return BrowserBridgeDesktopState.WaitingForUnlock;
            }

            if (!string.IsNullOrWhiteSpace(BrowserBridgeRuntimeError))
            {
                return BrowserBridgeDesktopState.Error;
            }

            return BrowserBridgeIsRunning
                ? BrowserBridgeDesktopState.Running
                : BrowserBridgeDesktopState.Starting;
        }
    }

    public bool IsBrowserBridgeUnavailableState =>
        BrowserBridgeDesktopStatus == BrowserBridgeDesktopState.Unavailable;

    public bool IsBrowserBridgeDisabledState =>
        BrowserBridgeDesktopStatus == BrowserBridgeDesktopState.Disabled;

    public bool IsBrowserBridgeWaitingForUnlockState =>
        BrowserBridgeDesktopStatus == BrowserBridgeDesktopState.WaitingForUnlock;

    public bool IsBrowserBridgeStartingState =>
        BrowserBridgeDesktopStatus == BrowserBridgeDesktopState.Starting;

    public bool IsBrowserBridgeRunningState =>
        BrowserBridgeDesktopStatus == BrowserBridgeDesktopState.Running;

    public bool IsBrowserBridgeErrorState =>
        BrowserBridgeDesktopStatus == BrowserBridgeDesktopState.Error;

    public bool HasBrowserIntegrationSessionToken =>
        IsBrowserBridgeRunningState && !string.IsNullOrWhiteSpace(BrowserIntegrationSessionToken);

    public string BrowserIntegrationMaskedSessionToken =>
        HasBrowserIntegrationSessionToken ? "•••• •••• •••• ••••" : "";

    public string BrowserBridgeStatusTitleText => BrowserBridgeDesktopStatus switch
    {
        BrowserBridgeDesktopState.Unavailable => _localization.Get("BrowserBridgeStateUnavailableTitle"),
        BrowserBridgeDesktopState.Disabled => _localization.Get("BrowserBridgeStateDisabledTitle"),
        BrowserBridgeDesktopState.WaitingForUnlock => _localization.Get("BrowserBridgeStateWaitingTitle"),
        BrowserBridgeDesktopState.Starting => _localization.Get("BrowserBridgeStateStartingTitle"),
        BrowserBridgeDesktopState.Running => _localization.Get("BrowserBridgeStateRunningTitle"),
        BrowserBridgeDesktopState.Error => _localization.Get("BrowserBridgeStateErrorTitle"),
        _ => _localization.Get("BrowserBridgeStateDisabledTitle")
    };

    public string BrowserBridgeStatusDescriptionText => BrowserBridgeDesktopStatus switch
    {
        BrowserBridgeDesktopState.Unavailable => BrowserBridgeIntegrationStatusText,
        BrowserBridgeDesktopState.Disabled => _localization.Get("BrowserBridgeStateDisabledDescription"),
        BrowserBridgeDesktopState.WaitingForUnlock => _localization.Get("BrowserBridgeStateWaitingDescription"),
        BrowserBridgeDesktopState.Starting =>
            _localization.Format("BrowserBridgeStateStartingDescriptionFormat", BrowserIntegrationPort),
        BrowserBridgeDesktopState.Running =>
            _localization.Format("BrowserBridgeStateRunningDescriptionFormat", BrowserIntegrationPort),
        BrowserBridgeDesktopState.Error => BrowserBridgeRuntimeError,
        _ => BrowserBridgeIntegrationStatusText
    };

    public string BrowserBridgeEndpointText =>
        _localization.Format("BrowserBridgeEndpointFormat", BrowserIntegrationPort);

    private void RaiseDesktopIntegrationPresentationState()
    {
        OnPropertyChanged(nameof(TrayIntegrationTaskStatusText));
        OnPropertyChanged(nameof(HasGlobalHotkeyRegistrationError));
        OnPropertyChanged(nameof(GlobalQuickSearchStatusTitleText));
        OnPropertyChanged(nameof(GlobalQuickSearchStatusDescriptionText));
        OnPropertyChanged(nameof(BrowserBridgeDesktopStatus));
        OnPropertyChanged(nameof(IsBrowserBridgeUnavailableState));
        OnPropertyChanged(nameof(IsBrowserBridgeDisabledState));
        OnPropertyChanged(nameof(IsBrowserBridgeWaitingForUnlockState));
        OnPropertyChanged(nameof(IsBrowserBridgeStartingState));
        OnPropertyChanged(nameof(IsBrowserBridgeRunningState));
        OnPropertyChanged(nameof(IsBrowserBridgeErrorState));
        OnPropertyChanged(nameof(HasBrowserIntegrationSessionToken));
        OnPropertyChanged(nameof(BrowserIntegrationMaskedSessionToken));
        OnPropertyChanged(nameof(BrowserBridgeStatusTitleText));
        OnPropertyChanged(nameof(BrowserBridgeStatusDescriptionText));
        OnPropertyChanged(nameof(BrowserBridgeEndpointText));
    }
}
