using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Settings;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class DesktopSettingsUiTests
{
    public DesktopSettingsUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Desktop_settings_are_grouped_by_windows_tasks_and_keep_primary_controls_keyboard_accessible()
    {
        var view = new SettingsDesktopView();

        Assert.NotNull(view.FindControl<StackPanel>("DesktopBackgroundBehaviorSection"));
        Assert.NotNull(view.FindControl<StackPanel>("DesktopQuickAccessSection"));
        Assert.NotNull(view.FindControl<StackPanel>("BrowserExtensionPairingSection"));
        Assert.NotNull(view.FindControl<StackPanel>("DesktopAppearanceSection"));

        var trayToggle = view.FindControl<ToggleSwitch>("MinimizeToTrayToggle")!;
        var quickSearchToggle = view.FindControl<ToggleSwitch>("QuickSearchToggle")!;
        var hotkeyBox = view.FindControl<TextBox>("QuickSearchHotkeyBox")!;
        var browserToggle = view.FindControl<ToggleSwitch>("BrowserIntegrationToggle")!;
        var copyButton = view.FindControl<Button>("CopyBrowserPairingTokenButton")!;

        Assert.True(trayToggle.Focusable);
        Assert.True(quickSearchToggle.Focusable);
        Assert.True(hotkeyBox.Focusable);
        Assert.True(browserToggle.Focusable);
        Assert.True(copyButton.Focusable);
        Assert.Equal(
            AutomationLiveSetting.Assertive,
            AutomationProperties.GetLiveSetting(view.FindControl<FAInfoBar>("BrowserBridgeErrorStatus")!));
    }

    [Fact]
    public async Task Browser_pairing_states_mask_the_token_and_copy_it_only_through_sensitive_clipboard()
    {
        var bridge = new RecordingSettingsBrowserBridgeService();
        var clipboard = new RecordingSensitiveClipboardService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<IBrowserBridgeService>(bridge);
            collection.AddSingleton<IClipboardService>(clipboard);
        });
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var coordinator = services.GetRequiredService<DesktopIntegrationCoordinator>();
        var view = new SettingsDesktopView { DataContext = viewModel };
        coordinator.Initialize(viewModel);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(BrowserBridgeDesktopState.Disabled, viewModel.BrowserBridgeDesktopStatus);
        Assert.True(view.FindControl<FAInfoBar>("BrowserBridgeDisabledStatus")!.IsVisible);

        viewModel.BrowserIntegrationEnabled = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(BrowserBridgeDesktopState.WaitingForUnlock, viewModel.BrowserBridgeDesktopStatus);
        Assert.True(view.FindControl<FAInfoBar>("BrowserBridgeWaitingStatus")!.IsVisible);

        viewModel.IsUnlocked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(BrowserBridgeDesktopState.Running, viewModel.BrowserBridgeDesktopStatus);
        Assert.True(view.FindControl<FAInfoBar>("BrowserBridgeRunningStatus")!.IsVisible);
        Assert.True(view.FindControl<Border>("BrowserPairingTokenPanel")!.IsVisible);
        var mask = view.FindControl<TextBlock>("BrowserPairingTokenMask")!.Text;
        Assert.Equal("•••• •••• •••• ••••", mask);
        Assert.DoesNotContain(bridge.SessionToken, mask, StringComparison.Ordinal);

        await viewModel.CopyBrowserIntegrationTokenCommand.ExecuteAsync(null);

        Assert.Equal(bridge.SessionToken, clipboard.SensitiveText);
        Assert.Empty(clipboard.RegularText);

        viewModel.BrowserIntegrationPort = 50123;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(BrowserBridgeDesktopState.Starting, viewModel.BrowserBridgeDesktopStatus);
        Assert.True(view.FindControl<FAInfoBar>("BrowserBridgeStartingStatus")!.IsVisible);
        Assert.False(view.FindControl<Border>("BrowserPairingTokenPanel")!.IsVisible);
        Assert.False(bridge.IsRunning);
        Assert.Empty(viewModel.BrowserIntegrationSessionToken);

        await PumpBrowserRestartAsync();

        Assert.Equal(BrowserBridgeDesktopState.Running, viewModel.BrowserBridgeDesktopStatus);
        Assert.Equal(50123, bridge.Port);
    }

    [Fact]
    public void Browser_pairing_error_is_distinct_and_never_exposes_a_token()
    {
        var bridge = new RecordingSettingsBrowserBridgeService { StartSucceeds = false };
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
            collection.AddSingleton<IBrowserBridgeService>(bridge));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var coordinator = services.GetRequiredService<DesktopIntegrationCoordinator>();
        var view = new SettingsDesktopView { DataContext = viewModel };
        coordinator.Initialize(viewModel);

        viewModel.BrowserIntegrationEnabled = true;
        viewModel.IsUnlocked = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(BrowserBridgeDesktopState.Error, viewModel.BrowserBridgeDesktopStatus);
        Assert.True(view.FindControl<FAInfoBar>("BrowserBridgeErrorStatus")!.IsVisible);
        Assert.Contains("Port already in use", viewModel.BrowserBridgeStatusDescriptionText, StringComparison.Ordinal);
        Assert.False(view.FindControl<Border>("BrowserPairingTokenPanel")!.IsVisible);
        Assert.Empty(viewModel.BrowserIntegrationSessionToken);
        Assert.False(viewModel.CopyBrowserIntegrationTokenCommand.CanExecute(null));
    }

    [Fact]
    public void Browser_pairing_unavailable_state_preserves_the_platform_capability_reason()
    {
        var detectedPlatform = new PlatformIntegrationService();
        var unavailableReason = "Browser bridge is unavailable in this desktop adapter.";
        var platform = new PlatformIntegrationService(
            "Test desktop",
            detectedPlatform.GetCapabilities().Select(capability =>
                capability.Key == PlatformFeatureKeys.BrowserBridge
                    ? PlatformIntegrationService.Unsupported(PlatformFeatureKeys.BrowserBridge, unavailableReason)
                    : capability));
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
            collection.AddSingleton<IPlatformIntegrationService>(platform));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var view = new SettingsDesktopView { DataContext = viewModel };
        var host = new Window { Content = view };
        viewModel.SelectedSettingsPage = "Desktop";
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(BrowserBridgeDesktopState.Unavailable, viewModel.BrowserBridgeDesktopStatus);
            Assert.Contains(unavailableReason, viewModel.BrowserBridgeStatusDescriptionText, StringComparison.Ordinal);
            Assert.True(view.FindControl<FAInfoBar>("BrowserBridgeUnavailableStatus")!.IsVisible);
            Assert.False(view.FindControl<ToggleSwitch>("BrowserIntegrationToggle")!.IsEnabled);
            Assert.False(view.FindControl<Border>("BrowserPairingTokenPanel")!.IsVisible);
        }
        finally
        {
            host.Close();
        }
    }

    private static async Task PumpBrowserRestartAsync()
    {
        await Task.Delay(450);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class RecordingSettingsBrowserBridgeService : IBrowserBridgeService
    {
        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.BrowserBridge, "Test browser bridge");
        public bool StartSucceeds { get; set; } = true;
        public bool IsRunning { get; private set; }
        public int Port { get; private set; }
        public string SessionToken { get; private set; } = "";
        public string LastError { get; private set; } = "";

        public bool TryStart(
            int port,
            Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>> queryCredentials)
        {
            if (!StartSucceeds)
            {
                LastError = "Port already in use.";
                return false;
            }

            IsRunning = true;
            Port = port;
            SessionToken = "test-session-token";
            LastError = "";
            return true;
        }

        public void Stop()
        {
            IsRunning = false;
            Port = 0;
            SessionToken = "";
        }

        public void Dispose() => Stop();
    }

    private sealed class RecordingSensitiveClipboardService : IClipboardService
    {
        public string RegularText { get; private set; } = "";
        public string SensitiveText { get; private set; } = "";

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            RegularText = text;
            return Task.CompletedTask;
        }

        public Task SetSensitiveTextAsync(string text, CancellationToken cancellationToken = default)
        {
            SensitiveText = text;
            return Task.CompletedTask;
        }
    }
}
