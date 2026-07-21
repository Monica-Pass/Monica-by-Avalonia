using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Monica.Platform.Services;

namespace Monica.App.Services;

internal sealed class AvaloniaTrayService(
    IPlatformIntegrationService platformIntegrationService,
    ILocalizationService localization) : ITrayService
{
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _showItem;
    private NativeMenuItem? _lockItem;
    private NativeMenuItem? _exitItem;
    private Action? _showWindow;
    private Action? _lockVault;
    private Action? _exitApplication;

    public PlatformIntegrationCapability Capability =>
        platformIntegrationService.GetCapability(PlatformFeatureKeys.Tray);
    public bool IsVisible => _trayIcon?.IsVisible == true;

    public void Initialize(Action showWindow, Action lockVault, Action exitApplication)
    {
        ArgumentNullException.ThrowIfNull(showWindow);
        ArgumentNullException.ThrowIfNull(lockVault);
        ArgumentNullException.ThrowIfNull(exitApplication);
        if (_trayIcon is not null)
        {
            return;
        }

        _showWindow = showWindow;
        _lockVault = lockVault;
        _exitApplication = exitApplication;
        _showItem = new NativeMenuItem();
        _lockItem = new NativeMenuItem();
        _exitItem = new NativeMenuItem();
        _showItem.Click += ShowItem_OnClick;
        _lockItem.Click += LockItem_OnClick;
        _exitItem.Click += ExitItem_OnClick;
        var menu = new NativeMenu();
        menu.Items.Add(_showItem);
        menu.Items.Add(_lockItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_exitItem);
        using var iconStream = AssetLoader.Open(new Uri("avares://Monica.App/Assets/AppIcon.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Monica",
            Menu = menu,
            IsVisible = false
        };
        _trayIcon.Clicked += TrayIcon_OnClicked;
        localization.PropertyChanged += Localization_OnPropertyChanged;
        RefreshLabels();
    }

    public void SetVisible(bool isVisible)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = isVisible;
        }
    }

    public void Dispose()
    {
        localization.PropertyChanged -= Localization_OnPropertyChanged;
        if (_showItem is not null) _showItem.Click -= ShowItem_OnClick;
        if (_lockItem is not null) _lockItem.Click -= LockItem_OnClick;
        if (_exitItem is not null) _exitItem.Click -= ExitItem_OnClick;
        if (_trayIcon is not null)
        {
            _trayIcon.Clicked -= TrayIcon_OnClicked;
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void Localization_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        RefreshLabels();

    private void RefreshLabels()
    {
        if (_showItem is not null) _showItem.Header = localization.Get("ShowMonica");
        if (_lockItem is not null) _lockItem.Header = localization.Get("LockVault");
        if (_exitItem is not null) _exitItem.Header = localization.Get("ExitApplication");
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e) => _showWindow?.Invoke();
    private void ShowItem_OnClick(object? sender, EventArgs e) => _showWindow?.Invoke();
    private void LockItem_OnClick(object? sender, EventArgs e) => _lockVault?.Invoke();
    private void ExitItem_OnClick(object? sender, EventArgs e) => _exitApplication?.Invoke();
}
