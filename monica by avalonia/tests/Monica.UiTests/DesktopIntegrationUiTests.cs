using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class DesktopIntegrationUiTests
{
    public DesktopIntegrationUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public async Task Desktop_integrations_follow_settings_and_report_hotkey_registration_failure()
    {
        var tray = new RecordingTrayService();
        var hotkey = new RecordingGlobalHotkeyService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<ITrayService>(tray);
            collection.AddSingleton<IGlobalHotkeyService>(hotkey);
        });
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var coordinator = services.GetRequiredService<DesktopIntegrationCoordinator>();

        coordinator.Initialize(viewModel);

        Assert.True(hotkey.IsRegistered);
        Assert.Equal("Ctrl+Shift+Space", hotkey.RegisteredGesture);
        Assert.False(tray.IsVisible);

        viewModel.MinimizeToTray = true;

        Assert.True(tray.IsVisible);

        viewModel.QuickSearchEnabled = false;
        await PumpDebounceAsync();

        Assert.False(hotkey.IsRegistered);

        hotkey.RegistrationSucceeds = false;
        viewModel.QuickSearchEnabled = true;
        viewModel.QuickSearchHotkey = "Ctrl+Alt+K";
        await PumpDebounceAsync();

        Assert.False(hotkey.IsRegistered);
        Assert.Contains(hotkey.LastError, viewModel.GlobalHotkeyIntegrationStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_settings_enable_operational_browser_bridge_capability()
    {
        var capability = new PlatformIntegrationService().GetCapability(PlatformFeatureKeys.BrowserBridge);

        Assert.Equal(Monica.Core.Models.PlatformFeatureStatus.Available, capability.Status);
        Assert.True(capability.IsUsable);
    }

    [Fact]
    public async Task Minimize_to_tray_hides_window_and_explicit_exit_closes_it()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.DataContext = viewModel;
        window.Show();
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        viewModel.MinimizeToTray = true;
        Assert.True(viewModel.MinimizeToTray);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);

        window.ShowFromDesktopIntegration();

        Assert.True(window.IsVisible);

        window.RequestExplicitExit();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
    }

    [Fact]
    public async Task Browser_bridge_follows_enable_unlock_and_lock_lifecycle()
    {
        var bridge = new RecordingBrowserBridgeService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
            collection.AddSingleton<IBrowserBridgeService>(bridge));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var coordinator = services.GetRequiredService<DesktopIntegrationCoordinator>();
        coordinator.Initialize(viewModel);

        viewModel.BrowserIntegrationEnabled = true;
        viewModel.IsUnlocked = true;

        Assert.True(bridge.IsRunning);
        Assert.True(viewModel.BrowserBridgeIsRunning);
        Assert.Equal("test-session-token", viewModel.BrowserIntegrationSessionToken);

        viewModel.IsUnlocked = false;

        Assert.False(bridge.IsRunning);
        Assert.False(viewModel.BrowserBridgeIsRunning);
        Assert.Empty(viewModel.BrowserIntegrationSessionToken);

        viewModel.IsUnlocked = true;
        viewModel.BrowserIntegrationPort = 50123;
        await PumpDebounceAsync();

        Assert.Equal(50123, bridge.Port);
    }

    [Theory]
    [InlineData("https://example.com", "example.com", true)]
    [InlineData("https://example.com", "accounts.example.com", true)]
    [InlineData("example.com", "example.com", true)]
    [InlineData("https://accounts.example.com", "example.com", false)]
    [InlineData("https://example.com", "example.com.attacker.test", false)]
    [InlineData("androidapp://com.example.app", "example.com", false)]
    public void Browser_credential_matcher_uses_strict_domain_boundaries(
        string storedWebsite,
        string requestedHost,
        bool expected)
    {
        Assert.Equal(expected, BrowserCredentialMatcher.EntryMatchesHost(storedWebsite, requestedHost));
    }

    private static async Task PumpDebounceAsync()
    {
        await Task.Delay(450);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class RecordingTrayService : ITrayService
    {
        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.Tray, "Test tray");
        public bool IsVisible { get; private set; }
        public void Initialize(Action showWindow, Action lockVault, Action exitApplication) { }
        public void SetVisible(bool isVisible) => IsVisible = isVisible;
        public void Dispose() { }
    }

    private sealed class RecordingGlobalHotkeyService : IGlobalHotkeyService
    {
        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.GlobalHotkey, "Test hotkey");
        public bool RegistrationSucceeds { get; set; } = true;
        public bool IsRegistered { get; private set; }
        public string RegisteredGesture { get; private set; } = "";
        public string LastError { get; private set; } = "";

        public bool TryRegister(string gesture, Action activated)
        {
            IsRegistered = RegistrationSucceeds;
            RegisteredGesture = RegistrationSucceeds ? gesture : "";
            LastError = RegistrationSucceeds ? "" : "Hotkey already used.";
            return RegistrationSucceeds;
        }

        public void Unregister()
        {
            IsRegistered = false;
            RegisteredGesture = "";
        }

        public void Dispose() => Unregister();
    }

    private sealed class RecordingBrowserBridgeService : IBrowserBridgeService
    {
        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.BrowserBridge, "Test bridge");
        public bool IsRunning { get; private set; }
        public int Port { get; private set; }
        public string SessionToken { get; private set; } = "";
        public string LastError { get; private set; } = "";

        public bool TryStart(int port, Func<Uri, CancellationToken, Task<IReadOnlyList<BrowserBridgeCredential>>> queryCredentials)
        {
            IsRunning = true;
            Port = port;
            SessionToken = "test-session-token";
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
}
