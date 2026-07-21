using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.App.Services;

internal sealed class DesktopIntegrationCoordinator(
    MainWindow window,
    ITrayService trayService,
    IGlobalHotkeyService globalHotkeyService) : IDisposable
{
    private MainWindowViewModel? _viewModel;
    private readonly DispatcherTimer _hotkeyRegistrationTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };

    public void Initialize(MainWindowViewModel viewModel)
    {
        if (_viewModel is not null)
        {
            return;
        }

        _viewModel = viewModel;
        _hotkeyRegistrationTimer.Tick += HotkeyRegistrationTimer_OnTick;
        trayService.Initialize(
            ShowWindow,
            () => Dispatcher.UIThread.Post(LockVault),
            () => Dispatcher.UIThread.Post(window.RequestExplicitExit));
        viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        ApplyTraySetting();
        ApplyGlobalHotkeySetting();
    }

    public void Dispose()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _viewModel = null;
        }

        _hotkeyRegistrationTimer.Stop();
        _hotkeyRegistrationTimer.Tick -= HotkeyRegistrationTimer_OnTick;
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
                 nameof(MainWindowViewModel.QuickSearchHotkey))
        {
            _hotkeyRegistrationTimer.Stop();
            _hotkeyRegistrationTimer.Start();
        }
    }

    private void ApplyTraySetting() => trayService.SetVisible(_viewModel?.MinimizeToTray == true);

    private void ApplyGlobalHotkeySetting()
    {
        var viewModel = _viewModel;
        if (viewModel is null || !viewModel.QuickSearchEnabled)
        {
            globalHotkeyService.Unregister();
            viewModel?.SetGlobalHotkeyRegistrationError("");
            return;
        }

        if (globalHotkeyService.TryRegister(
                viewModel.QuickSearchHotkey,
                () => Dispatcher.UIThread.Post(ShowQuickSearch)))
        {
            viewModel.SetGlobalHotkeyRegistrationError("");
        }
        else
        {
            viewModel.SetGlobalHotkeyRegistrationError(globalHotkeyService.LastError);
        }
    }

    private void HotkeyRegistrationTimer_OnTick(object? sender, EventArgs e)
    {
        _hotkeyRegistrationTimer.Stop();
        ApplyGlobalHotkeySetting();
    }

    private void ShowWindow() => Dispatcher.UIThread.Post(window.ShowFromDesktopIntegration);

    private void ShowQuickSearch()
    {
        window.ShowFromDesktopIntegration();
        window.FocusDesktopQuickSearch();
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
