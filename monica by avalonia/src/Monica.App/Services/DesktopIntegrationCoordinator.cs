using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.App.Services;

internal sealed class DesktopIntegrationCoordinator(
    MainWindow window,
    ITrayService trayService,
    IGlobalHotkeyService globalHotkeyService,
    IBrowserBridgeService browserBridgeService,
    IAutoTypeService autoTypeService) : IDisposable
{
    private MainWindowViewModel? _viewModel;
    private readonly DispatcherTimer _hotkeyRegistrationTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };
    private readonly DispatcherTimer _browserRegistrationTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };

    // Only the desktop knows whether RegisterHotKey actually accepted the gesture, and the published
    // artifact probe needs that to tell "the key is live" apart from "nobody has pressed it yet".
    internal bool IsAutoTypeHotkeyRegistered =>
        globalHotkeyService.IsRegistered(GlobalHotkeySlot.AutoType);

    public void Initialize(MainWindowViewModel viewModel)
    {
        if (_viewModel is not null)
        {
            return;
        }

        _viewModel = viewModel;
        _hotkeyRegistrationTimer.Tick += HotkeyRegistrationTimer_OnTick;
        _browserRegistrationTimer.Tick += BrowserRegistrationTimer_OnTick;
        trayService.Initialize(
            ShowWindow,
            () => Dispatcher.UIThread.Post(LockVault),
            () => Dispatcher.UIThread.Post(window.RequestExplicitExit));
        viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        ApplyTraySetting();
        ApplyGlobalHotkeySetting();
        ApplyAutoTypeSetting();
        ApplyBrowserBridgeSetting();
    }

    public void Dispose()
    {
        var viewModel = _viewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        }

        _hotkeyRegistrationTimer.Stop();
        _hotkeyRegistrationTimer.Tick -= HotkeyRegistrationTimer_OnTick;
        _browserRegistrationTimer.Stop();
        _browserRegistrationTimer.Tick -= BrowserRegistrationTimer_OnTick;
        StopBrowserBridge();
        _viewModel = null;
        browserBridgeService.Dispose();
        globalHotkeyService.Dispose();
        trayService.Dispose();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.MinimizeToTray))
        {
            ApplyTraySetting();
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.QuickSearchEnabled) or
                 nameof(MainWindowViewModel.QuickSearchHotkey) or
                 nameof(MainWindowViewModel.AutoTypeEnabled) or
                 nameof(MainWindowViewModel.AutoTypeHotkey))
        {
            _hotkeyRegistrationTimer.Stop();
            _hotkeyRegistrationTimer.Start();
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.BrowserIntegrationEnabled) or
                 nameof(MainWindowViewModel.BrowserIntegrationPort))
        {
            ScheduleBrowserBridgeRestart();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsUnlocked))
        {
            ApplyBrowserBridgeSetting();
            if (_viewModel is { IsUnlocked: false })
            {
                // A list of account names is not something to leave on screen behind a locked vault.
                window.CloseAutoTypePicker("VaultLocked");
            }
        }
    }

    private void ApplyTraySetting() => trayService.SetVisible(_viewModel?.MinimizeToTray == true);

    private void ApplyGlobalHotkeySetting()
    {
        var viewModel = _viewModel;
        if (viewModel is null || !viewModel.QuickSearchEnabled)
        {
            globalHotkeyService.Unregister(GlobalHotkeySlot.QuickSearch);
            viewModel?.SetGlobalHotkeyRegistrationError("");
            return;
        }

        if (globalHotkeyService.TryRegister(
                GlobalHotkeySlot.QuickSearch,
                viewModel.QuickSearchHotkey,
                () => Dispatcher.UIThread.Post(ShowQuickSearch)))
        {
            viewModel.SetGlobalHotkeyRegistrationError("");
        }
        else
        {
            viewModel.SetGlobalHotkeyRegistrationError(globalHotkeyService.LastError(GlobalHotkeySlot.QuickSearch));
        }
    }

    private void ApplyAutoTypeSetting()
    {
        var viewModel = _viewModel;
        if (viewModel is null || !viewModel.AutoTypeEnabled)
        {
            globalHotkeyService.Unregister(GlobalHotkeySlot.AutoType);
            viewModel?.SetAutoTypeRegistrationError("");
            return;
        }

        if (string.Equals(viewModel.AutoTypeHotkey, viewModel.QuickSearchHotkey, StringComparison.OrdinalIgnoreCase))
        {
            // Both slots would ask the desktop for the same key combination and the second request
            // would be refused, so say which one is in the way instead of reporting a mystery error.
            globalHotkeyService.Unregister(GlobalHotkeySlot.AutoType);
            viewModel.ReportAutoTypeGestureConflict();
            return;
        }

        if (globalHotkeyService.TryRegister(
                GlobalHotkeySlot.AutoType,
                viewModel.AutoTypeHotkey,
                () => Dispatcher.UIThread.Post(RunAutoType)))
        {
            viewModel.SetAutoTypeRegistrationError("");
        }
        else
        {
            viewModel.SetAutoTypeRegistrationError(globalHotkeyService.LastError(GlobalHotkeySlot.AutoType));
        }
    }

    private void HotkeyRegistrationTimer_OnTick(object? sender, EventArgs e)
    {
        _hotkeyRegistrationTimer.Stop();
        ApplyGlobalHotkeySetting();
        ApplyAutoTypeSetting();
    }

    private void BrowserRegistrationTimer_OnTick(object? sender, EventArgs e)
    {
        _browserRegistrationTimer.Stop();
        ApplyBrowserBridgeSetting();
    }

    private void ScheduleBrowserBridgeRestart()
    {
        _browserRegistrationTimer.Stop();
        StopBrowserBridge();
        if (_viewModel is { BrowserIntegrationEnabled: true, IsUnlocked: true })
        {
            _browserRegistrationTimer.Start();
        }
    }

    private void ApplyBrowserBridgeSetting()
    {
        var viewModel = _viewModel;
        StopBrowserBridge();
        if (viewModel is null || !viewModel.BrowserIntegrationEnabled || !viewModel.IsUnlocked)
        {
            return;
        }

        if (browserBridgeService.TryStart(viewModel.BrowserIntegrationPort, QueryBrowserCredentialsAsync))
        {
            viewModel.SetBrowserBridgeRuntimeState(true, browserBridgeService.SessionToken, "");
        }
        else
        {
            viewModel.SetBrowserBridgeRuntimeState(false, "", browserBridgeService.LastError);
        }
    }

    private void StopBrowserBridge()
    {
        browserBridgeService.Stop();
        _viewModel?.SetBrowserBridgeRuntimeState(false, "", "");
    }

    private async Task<IReadOnlyList<BrowserBridgeCredential>> QueryBrowserCredentialsAsync(
        Uri origin,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = Dispatcher.UIThread.InvokeAsync(() =>
        {
            var viewModel = _viewModel;
            return viewModel is { IsUnlocked: true }
                ? BrowserCredentialMatcher.Match(viewModel.Passwords, origin)
                : [];
        });
        var credentials = await operation;
        cancellationToken.ThrowIfCancellationRequested();
        return credentials;
    }

    private void ShowWindow() => Dispatcher.UIThread.Post(window.ShowFromDesktopIntegration);

    private void ShowQuickSearch()
    {
        window.ShowFromDesktopIntegration();
        window.FocusDesktopQuickSearch();
    }

    // Runs on the UI thread. It raises no Monica window of its own accord: anything shown here takes
    // the focus the target application still needs, which is why the one window it does surface - the
    // picker - has to hand that focus back before it types a single character.
    private void RunAutoType()
    {
        var viewModel = _viewModel;
        if (viewModel is null)
        {
            return;
        }

        // A second press while the list is up means "not this one, forget it". By then Monica does own
        // the foreground, so the refusal below would be technically true and useless as an answer.
        if (viewModel.IsAutoTypePickerOpen)
        {
            window.CloseAutoTypePicker("SecondHotkeyPress");
            return;
        }

        var foreground = autoTypeService.GetForegroundWindow();
        var foregroundIsMonicaWindow = autoTypeService.IsWindowOwnedByThisProcess(foreground);
        if (foregroundIsMonicaWindow)
        {
            // The one refusal a user cannot explain from the screen, because nothing appears to happen.
            AppDiagnostics.Info($"Auto type refused: Monica owns the foreground window {foreground}.");
        }

        viewModel.RunAutoTypeIntoForeground(
            foreground,
            autoTypeService.GetWindowTitle(foreground),
            foregroundIsMonicaWindow);
        if (viewModel.IsAutoTypePickerOpen)
        {
            window.ShowAutoTypePicker();
        }
    }

    private void LockVault()
    {
        var viewModel = _viewModel;
        if (viewModel?.LockCommand.CanExecute(null) == true)
        {
            viewModel.LockCommand.Execute(null);
        }
    }
}
