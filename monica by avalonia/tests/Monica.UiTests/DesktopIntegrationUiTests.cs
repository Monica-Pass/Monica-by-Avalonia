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

        Assert.True(hotkey.IsRegistered(GlobalHotkeySlot.QuickSearch));
        Assert.Equal("Ctrl+Shift+Space", hotkey.RegisteredGesture(GlobalHotkeySlot.QuickSearch));
        Assert.False(tray.IsVisible);

        await viewModel.InitializeAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.True(viewModel.MinimizeToTray);
        Assert.True(tray.IsVisible);

        viewModel.MinimizeToTray = false;

        Assert.False(tray.IsVisible);

        viewModel.QuickSearchEnabled = false;
        await PumpDebounceAsync();

        Assert.False(hotkey.IsRegistered(GlobalHotkeySlot.QuickSearch));

        hotkey.RegistrationSucceeds = false;
        viewModel.QuickSearchEnabled = true;
        viewModel.QuickSearchHotkey = "Ctrl+Alt+K";
        await PumpDebounceAsync();

        Assert.False(hotkey.IsRegistered(GlobalHotkeySlot.QuickSearch));
        Assert.Contains(hotkey.LastError(GlobalHotkeySlot.QuickSearch), viewModel.GlobalHotkeyIntegrationStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Auto_type_registers_its_own_slot_and_refuses_a_shared_gesture()
    {
        var hotkey = new RecordingGlobalHotkeyService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<ITrayService>(new RecordingTrayService());
            collection.AddSingleton<IGlobalHotkeyService>(hotkey);
        });
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var coordinator = services.GetRequiredService<DesktopIntegrationCoordinator>();
        coordinator.Initialize(viewModel);

        viewModel.AutoTypeEnabled = true;
        await PumpDebounceAsync();

        Assert.True(hotkey.IsRegistered(GlobalHotkeySlot.QuickSearch));
        Assert.True(hotkey.IsRegistered(GlobalHotkeySlot.AutoType));
        Assert.Equal("Ctrl+Shift+Enter", hotkey.RegisteredGesture(GlobalHotkeySlot.AutoType));
        Assert.Empty(viewModel.AutoTypeRegistrationError);

        viewModel.AutoTypeHotkey = "Ctrl+Shift+Space";
        await PumpDebounceAsync();

        Assert.True(hotkey.IsRegistered(GlobalHotkeySlot.QuickSearch));
        Assert.False(hotkey.IsRegistered(GlobalHotkeySlot.AutoType));
        Assert.Contains("Ctrl+Shift+Space", viewModel.QuickSearchHotkey, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.AutoTypeRegistrationError));
        Assert.Contains(viewModel.AutoTypeRegistrationError, viewModel.AutoTypeIntegrationStatusText, StringComparison.Ordinal);

        viewModel.AutoTypeEnabled = false;
        await PumpDebounceAsync();

        Assert.False(hotkey.IsRegistered(GlobalHotkeySlot.AutoType));
        Assert.True(hotkey.IsRegistered(GlobalHotkeySlot.QuickSearch));
        Assert.Empty(viewModel.AutoTypeRegistrationError);
    }

    [Fact]
    public void Auto_type_types_the_single_matching_entry_and_refuses_to_guess()
    {
        var autoType = new RecordingAutoTypeService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
            collection.AddSingleton<IAutoTypeService>(autoType));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Title = "GitHub",
            Website = "https://github.com",
            Username = "octocat",
            Password = "hunter2"
        });
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Title = "Mail",
            Website = "https://mail.smoke.local",
            Username = "me",
            Password = "other-secret"
        });

        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub · GitHub", false);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.Typed, viewModel.LastAutoTypeOutcome);
        Assert.Collection(
            autoType.LastTokens,
            token =>
            {
                Assert.Equal(AutoTypeTokenKind.Text, token.Kind);
                Assert.Equal("octocat", token.Value);
            },
            token => Assert.Equal(AutoTypeTokenKind.Tab, token.Kind),
            token =>
            {
                Assert.Equal(AutoTypeTokenKind.Text, token.Kind);
                Assert.Equal("hunter2", token.Value);
            });

        autoType.Reset();
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Title = "GitHub Work",
            Website = "github.com",
            Username = "work",
            Password = "work-secret"
        });
        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub · GitHub", false);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.Ambiguous, viewModel.LastAutoTypeOutcome);
        Assert.Equal(0, autoType.TypeCallCount);
        Assert.Equal(2, viewModel.LastAutoTypeMatches.Count);

        autoType.Reset();
        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Local Console", false);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.NoMatch, viewModel.LastAutoTypeOutcome);
        Assert.Equal(0, autoType.TypeCallCount);

        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub · GitHub", true);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.MonicaIsForeground, viewModel.LastAutoTypeOutcome);
        Assert.Equal(0, autoType.TypeCallCount);
    }

    [Fact]
    public async Task Auto_type_press_asks_the_operating_system_who_owns_the_foreground_window()
    {
        var hotkey = new RecordingGlobalHotkeyService();
        var autoType = new RecordingAutoTypeService
        {
            ForegroundTitle = "Sign in to GitHub - github.com"
        };
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<ITrayService>(new RecordingTrayService());
            collection.AddSingleton<IGlobalHotkeyService>(hotkey);
            collection.AddSingleton<IAutoTypeService>(autoType);
        });
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var coordinator = services.GetRequiredService<DesktopIntegrationCoordinator>();
        coordinator.Initialize(viewModel);

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Title = "GitHub",
            Website = "https://github.com",
            Username = "octocat",
            Password = "hunter2"
        });
        viewModel.AutoTypeEnabled = true;
        await PumpDebounceAsync();

        var press = hotkey.Callback(GlobalHotkeySlot.AutoType);
        Assert.NotNull(press);

        // A window this process does not own receives the credential, whatever the shell window reports
        // about itself; living off that report is what used to refuse every real auto-type.
        press!.Invoke();
        await PumpDebounceAsync();

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.Typed, viewModel.LastAutoTypeOutcome);
        Assert.Equal(1, autoType.TypeCallCount);

        autoType.Reset();
        autoType.ForegroundIsOwned = true;
        press.Invoke();
        await PumpDebounceAsync();

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.MonicaIsForeground, viewModel.LastAutoTypeOutcome);
        Assert.Equal(0, autoType.TypeCallCount);
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

        Assert.False(bridge.IsRunning);
        Assert.False(viewModel.BrowserBridgeIsRunning);
        Assert.Empty(viewModel.BrowserIntegrationSessionToken);

        await PumpDebounceAsync();

        Assert.Equal(50123, bridge.Port);

        viewModel.BrowserIntegrationEnabled = false;

        Assert.False(bridge.IsRunning);
        Assert.False(viewModel.BrowserBridgeIsRunning);
        Assert.Empty(viewModel.BrowserIntegrationSessionToken);
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
        public string LastError(GlobalHotkeySlot slot) => "Hotkey already used.";
        public bool IsRegistered(GlobalHotkeySlot slot) => _registered[(int)slot];
        public string RegisteredGesture(GlobalHotkeySlot slot) => _gestures[(int)slot];
        public Action? Callback(GlobalHotkeySlot slot) => _callbacks[(int)slot];
        private readonly bool[] _registered = new bool[2];
        private readonly string[] _gestures = ["", ""];
        private readonly Action?[] _callbacks = new Action?[2];

        public bool TryRegister(GlobalHotkeySlot slot, string gesture, Action activated)
        {
            var index = (int)slot;
            _registered[index] = RegistrationSucceeds;
            _gestures[index] = RegistrationSucceeds ? gesture : "";
            _callbacks[index] = RegistrationSucceeds ? activated : null;
            return RegistrationSucceeds;
        }

        public void Unregister(GlobalHotkeySlot slot)
        {
            var index = (int)slot;
            _registered[index] = false;
            _gestures[index] = "";
            _callbacks[index] = null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingAutoTypeService : IAutoTypeService
    {
        public PlatformIntegrationCapability Capability { get; } =
            PlatformIntegrationService.Available(PlatformFeatureKeys.AutoType, "Test auto-type");
        public string LastError { get; private set; } = "";
        public IntPtr ForegroundHandle { get; set; } = new(0x1234);
        public string ForegroundTitle { get; set; } = "";
        public bool ForegroundIsOwned { get; set; }
        public IReadOnlyList<AutoTypeToken> LastTokens { get; private set; } = [];
        public int TypeCallCount { get; private set; }
        public bool InjectionSucceeds { get; set; } = true;

        public IntPtr GetForegroundWindow() => ForegroundHandle;

        public void Reset()
        {
            TypeCallCount = 0;
            LastTokens = [];
            LastError = "";
        }

        public string GetWindowTitle(IntPtr windowHandle) =>
            windowHandle == ForegroundHandle ? ForegroundTitle : "";

        public bool IsWindowOwnedByThisProcess(IntPtr windowHandle) =>
            ForegroundIsOwned && windowHandle == ForegroundHandle;

        public bool TryType(IReadOnlyList<AutoTypeToken> tokens)
        {
            TypeCallCount++;
            LastTokens = tokens;
            LastError = InjectionSucceeds ? "" : "The keystrokes were refused.";
            return InjectionSucceeds;
        }
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
