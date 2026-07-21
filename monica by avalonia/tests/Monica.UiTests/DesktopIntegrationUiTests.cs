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
    public void Desktop_settings_disable_empty_browser_bridge_capability()
    {
        var capability = new PlatformIntegrationService().GetCapability(PlatformFeatureKeys.BrowserBridge);

        Assert.Equal(Monica.Core.Models.PlatformFeatureStatus.PlatformLimited, capability.Status);
        Assert.False(capability.IsUsable);
    }

    [Fact]
    public void Minimize_to_tray_hides_window_and_explicit_exit_closes_it()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.DataContext = viewModel;
        viewModel.MinimizeToTray = true;
        window.Show();

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);

        window.ShowFromDesktopIntegration();

        Assert.True(window.IsVisible);

        window.RequestExplicitExit();
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
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
}
