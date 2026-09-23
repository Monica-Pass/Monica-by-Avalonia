using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Services;
using Monica.App.ViewModels;

namespace Monica.UiTests;

/// <summary>
/// Hiding the window to the tray is silent by construction, and the tray icon may be sitting in the
/// notification-area overflow, so the first time it happens Monica has to say so. These cover the whole
/// contract: both paths that hide the window explain it, the explanation is once per install rather
/// than every time (including across a settings reload), it retires on its own, and acting on it brings
/// the window back.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class TrayHintUiTests
{
    public TrayHintUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void First_minimize_explains_the_tray_and_the_next_one_does_not()
    {
        var (window, _, settings, services) = Boot();
        try
        {
            Assert.False(settings.Current.TrayHintShown);

            window.WindowState = WindowState.Minimized;
            Drain();

            Assert.False(window.IsVisible);
            Assert.True(window.IsTrayHintVisible);
            Assert.True(settings.Current.TrayHintShown);
            Assert.True(window.IsTrayHintRetirementScheduled);
            Assert.Equal(TimeSpan.FromSeconds(8), window.TrayHintRetirementInterval);

            window.ShowFromDesktopIntegration();
            Drain();

            Assert.True(window.IsVisible);
            Assert.False(window.IsTrayHintVisible);

            window.WindowState = WindowState.Minimized;
            Drain();

            Assert.False(window.IsTrayHintVisible);
            Assert.False(window.IsTrayHintRetirementScheduled);
        }
        finally
        {
            Dispose(window, services);
        }
    }

    [Fact]
    public void Close_to_tray_explains_where_the_window_went()
    {
        var (window, _, settings, services) = Boot();
        try
        {
            Assert.False(settings.Current.TrayHintShown);

            window.Close();
            Drain();

            Assert.False(window.IsVisible);
            Assert.True(window.IsTrayHintVisible);
            Assert.True(settings.Current.TrayHintShown);
        }
        finally
        {
            Dispose(window, services);
        }
    }

    [Fact]
    public void Taking_the_hint_back_to_the_window_closes_the_hint()
    {
        var (window, viewModel, _, services) = Boot();
        try
        {
            window.WindowState = WindowState.Minimized;
            Drain();
            var hint = Assert.IsType<Monica.App.TrayHintWindow>(window.ActiveTrayHint);
            var showAction = hint.ShowActionControl;
            Assert.Equal(viewModel.TrayHintShowActionText, showAction.Content?.ToString());

            showAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Drain();

            Assert.True(window.IsVisible);
            Assert.False(window.IsTrayHintVisible);
            Assert.Null(window.ActiveTrayHint);
        }
        finally
        {
            Dispose(window, services);
        }
    }

    [Fact]
    public async Task The_hint_retires_itself_once_its_dwell_has_elapsed()
    {
        var (window, _, _, services) = Boot();
        try
        {
            window.WindowState = WindowState.Minimized;
            Drain();
            Assert.True(window.IsTrayHintVisible);
            Assert.True(window.IsTrayHintRetirementScheduled);

            // Nothing but the product's own timer is allowed to take it away: the test waits past the
            // declared dwell and never calls into the close path itself.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (window.IsTrayHintVisible && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250, TestContext.Current.CancellationToken);
                Drain();
            }

            Assert.False(window.IsTrayHintVisible);
            Assert.False(window.IsTrayHintRetirementScheduled);
            Assert.Null(window.ActiveTrayHint);
            Assert.False(window.IsVisible);
        }
        finally
        {
            Dispose(window, services);
        }
    }

    [Fact]
    public async Task A_settings_reload_mid_run_does_not_earn_the_explanation_back()
    {
        var (window, viewModel, settings, services) = Boot();
        try
        {
            window.WindowState = WindowState.Minimized;
            Drain();
            Assert.True(window.IsTrayHintVisible);

            window.ShowFromDesktopIntegration();
            // Hand the reload the mismatch the debounced save produces in the wild: this run has been
            // told, the file has not. Putting the flag back in memory would only re-open the race with
            // the save still in flight, so the reload starts from the file's answer and the guard under
            // test has to be the one that remembers.
            settings.Current.TrayHintShown = false;
            await settings.SaveAsync(TestContext.Current.CancellationToken);
            var settingsBeforeReload = settings.Current;
            await settings.LoadAsync(TestContext.Current.CancellationToken);
            Drain();

            // The reload really did replace the in-memory object, so the two facts below cannot pass
            // because nothing happened.
            Assert.False(settings.Current.TrayHintShown);
            Assert.NotSame(settingsBeforeReload, settings.Current);
            Assert.False(viewModel.ShouldSurfaceTrayHint());

            window.WindowState = WindowState.Minimized;
            Drain();

            Assert.False(window.IsTrayHintVisible);
        }
        finally
        {
            Dispose(window, services);
        }
    }

    [Fact]
    public void A_window_that_keeps_its_tray_off_owes_no_explanation()
    {
        var (window, viewModel, settings, services) = Boot();
        try
        {
            viewModel.MinimizeToTray = false;

            window.WindowState = WindowState.Minimized;
            Drain();

            Assert.False(window.IsTrayHintVisible);
            Assert.False(settings.Current.TrayHintShown);
        }
        finally
        {
            Dispose(window, services);
        }
    }

    private static (
        Monica.App.MainWindow Window,
        MainWindowViewModel ViewModel,
        IAppSettingsService Settings,
        ServiceProvider Services) Boot()
    {
        var window = new Monica.App.MainWindow { Width = 1280, Height = 800 };
        var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var settings = services.GetRequiredService<IAppSettingsService>();
        window.Show();
        window.DataContext = viewModel;
        viewModel.MinimizeToTray = true;
        // Which installs have already been told lives in settings.json and the suite shares one file, so
        // every fact starts from "not yet" - the state a fresh install is in. Memory alone is not enough:
        // a re-show reloads the file copy, so a previous run's leftover would decide what these facts see.
        settings.Current.TrayHintShown = false;
        // Off the UI thread on purpose: SaveAsync awaits without ConfigureAwait(false), so blocking on it
        // here would deadlock the test on its own dispatcher.
        Task.Run(() => settings.SaveAsync()).GetAwaiter().GetResult();
        Drain();
        return (window, viewModel, settings, services);
    }

    private static void Dispose(Monica.App.MainWindow window, ServiceProvider services)
    {
        window.RequestExplicitExit();
        Drain();
        services.Dispose();
    }

    private static void Drain()
    {
        for (var i = 0; i < 60; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }
}
