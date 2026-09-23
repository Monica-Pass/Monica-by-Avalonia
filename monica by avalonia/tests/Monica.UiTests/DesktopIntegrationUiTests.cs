using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Settings;
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

    // A gesture the user pressed into the settings row has to reach three places at once - the view
    // model, the live desktop slot and the saved file - and the binding is the one link in that chain
    // nothing else in the suite can see.
    [Fact]
    public async Task Recording_a_hotkey_in_settings_replaces_the_registered_gesture()
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
        viewModel.SelectedSettingsPage = "Desktop";
        var settingsView = new SettingsDesktopView { DataContext = viewModel };
        var host = new Window { Width = 1100, Height = 800, Content = settingsView };
        host.Show();
        await PumpDebounceAsync();

        try
        {
            var recorder = settingsView.FindControl<HotkeyRecorder>("AutoTypeHotkeyBox")!;
            Assert.True(recorder.IsEnabled);
            Assert.Equal("Ctrl+Shift+Enter", recorder.Gesture);

            Assert.True(recorder.TryCommit(KeyModifiers.Control | KeyModifiers.Shift, Key.K));
            await PumpDebounceAsync();

            Assert.Equal("Ctrl+Shift+K", viewModel.AutoTypeHotkey);
            Assert.Equal("Ctrl+Shift+K", hotkey.RegisteredGesture(GlobalHotkeySlot.AutoType));
            Assert.Equal(
                "Ctrl+Shift+K",
                services.GetRequiredService<IAppSettingsService>().Current.AutoTypeHotkey);
            Assert.True(hotkey.IsRegistered(GlobalHotkeySlot.QuickSearch));
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void Auto_type_types_a_unique_match_and_lists_the_choices_when_the_window_is_not_unique()
    {
        var autoType = new RecordingAutoTypeService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
            collection.AddSingleton<IAutoTypeService>(autoType));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 1,
            Title = "GitHub",
            Website = "https://github.com",
            Username = "octocat",
            Password = "hunter2"
        });
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 2,
            Title = "Mail",
            Website = "https://mail.smoke.local",
            Username = "me",
            Password = "other-secret"
        });

        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub · GitHub", false);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.Typed, viewModel.LastAutoTypeOutcome);
        Assert.False(viewModel.IsAutoTypePickerOpen);
        Assert.Equal(1, autoType.TypeCallCount);
        Assert.False(autoType.TypedWithoutRestoringForeground);
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

        // A second account on the same host makes the window ambiguous. The choice goes back to the
        // user as a list instead of a refusal, and nothing is typed until one is picked.
        autoType.Reset();
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 3,
            Title = "GitHub Work",
            Website = "github.com",
            Username = "work",
            Password = "work-secret"
        });
        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub · GitHub", false);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.PickerForMatches, viewModel.LastAutoTypeOutcome);
        Assert.True(viewModel.IsAutoTypePickerOpen);
        Assert.False(viewModel.AutoTypePickerListsAllEntries);
        Assert.Equal(0, autoType.TypeCallCount);
        Assert.Equal(
            new[] { "GitHub", "GitHub Work" },
            viewModel.AutoTypePickerCandidates.Select(candidate => candidate.Title));

        viewModel.CancelAutoTypePicker();

        Assert.False(viewModel.IsAutoTypePickerOpen);
        Assert.Empty(viewModel.AutoTypePickerCandidates);
        Assert.Equal(0, autoType.TypeCallCount);

        // A window nothing matches is the other half of the list: the vault, filtered, rather than a
        // dead end that sends the user hunting for the entry by hand.
        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Local Console", false);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.PickerForAllEntries, viewModel.LastAutoTypeOutcome);
        Assert.True(viewModel.AutoTypePickerListsAllEntries);
        Assert.Equal(3, viewModel.AutoTypePickerCandidates.Count);
        Assert.Equal(0, autoType.TypeCallCount);

        viewModel.CancelAutoTypePicker();
        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub · GitHub", true);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.MonicaIsForeground, viewModel.LastAutoTypeOutcome);
        Assert.False(viewModel.IsAutoTypePickerOpen);
        Assert.Equal(0, autoType.TypeCallCount);
    }

    [Fact]
    public void Auto_type_picker_selection_gives_the_keyboard_back_to_the_target_before_typing()
    {
        var autoType = new RecordingAutoTypeService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
            collection.AddSingleton<IAutoTypeService>(autoType));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 1,
            Title = "GitHub",
            Website = "github.com",
            Username = "octocat",
            Password = "hunter2"
        });
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 2,
            Title = "GitHub Work",
            Website = "github.com",
            Username = "work",
            Password = "work-secret"
        });
        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub - github.com", false);

        Assert.True(viewModel.IsAutoTypePickerOpen);
        var picked = viewModel.AutoTypePickerCandidates.Single(candidate => candidate.Title == "GitHub Work");
        viewModel.CompleteAutoTypeFromPicker(picked);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.Typed, viewModel.LastAutoTypeOutcome);
        Assert.False(viewModel.IsAutoTypePickerOpen);
        Assert.Equal(autoType.ForegroundHandle, autoType.LastRestoredHandle);
        Assert.False(autoType.TypedWithoutRestoringForeground);
        Assert.Equal(
            new[] { "work", "work-secret" },
            autoType.LastTokens
                .Where(token => token.Kind == AutoTypeTokenKind.Text)
                .Select(token => token.Value)
                .ToArray());
    }

    [Fact]
    public void Auto_type_picker_sends_nothing_when_the_target_cannot_take_the_focus_back()
    {
        var autoType = new RecordingAutoTypeService();
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window, collection =>
            collection.AddSingleton<IAutoTypeService>(autoType));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 1,
            Title = "GitHub",
            Website = "github.com",
            Username = "octocat",
            Password = "hunter2"
        });
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 2,
            Title = "GitHub Work",
            Website = "github.com",
            Username = "work",
            Password = "work-secret"
        });
        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub - github.com", false);
        autoType.Reset();
        autoType.RestoresSucceed = false;
        var candidate = viewModel.AutoTypePickerCandidates[0];

        viewModel.CompleteAutoTypeFromPicker(candidate);

        // The keystrokes would have landed in whatever window did hold the focus, which is the one
        // outcome worse than no auto-fill at all.
        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.TargetUnavailable, viewModel.LastAutoTypeOutcome);
        Assert.Equal(0, autoType.TypeCallCount);
        Assert.False(viewModel.IsAutoTypePickerOpen);

        viewModel.RunAutoTypeIntoForeground(autoType.ForegroundHandle, "Sign in to GitHub - github.com", false);
        autoType.RestoresSucceed = true;
        var secondCandidate = viewModel.AutoTypePickerCandidates[0];
        viewModel.IsUnlocked = false;

        viewModel.CompleteAutoTypeFromPicker(secondCandidate);

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.Locked, viewModel.LastAutoTypeOutcome);
        Assert.Equal(0, autoType.TypeCallCount);
        Assert.False(viewModel.IsAutoTypePickerOpen);
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
    public async Task Auto_type_press_surfaces_a_picker_whose_rows_hold_no_password()
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
        window.DataContext = viewModel;

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 1,
            Title = "GitHub Personal",
            Website = "github.com",
            Username = "octocat",
            Password = "personal-hunter2"
        });
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 2,
            Title = "GitHub Work",
            Website = "github.com",
            Username = "work",
            Password = "work-hunter2"
        });
        viewModel.AutoTypeEnabled = true;
        await PumpDebounceAsync();

        var press = hotkey.Callback(GlobalHotkeySlot.AutoType);
        Assert.NotNull(press);
        press!.Invoke();
        await PumpDebounceAsync();

        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.PickerForMatches, viewModel.LastAutoTypeOutcome);
        Assert.True(window.IsAutoTypePickerVisible);
        var picker = window.ActiveAutoTypePicker!;
        Assert.Equal(2, picker.VisibleCandidateCount);
        Assert.Equal(0, autoType.TypeCallCount);

        // The standing red line, read off what the window actually renders: a picker row names the
        // account it is about to send and never the secret itself.
        var rendered = picker
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(text => text.Text)
            .ToArray();
        Assert.Contains("GitHub Personal", rendered);
        Assert.Contains("octocat", rendered);
        Assert.DoesNotContain(rendered, text =>
            text is not null && text.Contains("hunter2", StringComparison.OrdinalIgnoreCase));

        picker.InnerEntryList!.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Null(window.ActiveAutoTypePicker);
        Assert.Equal(MainWindowViewModel.AutoTypeOutcome.Typed, viewModel.LastAutoTypeOutcome);
        Assert.Equal(1, autoType.TypeCallCount);
        Assert.False(autoType.TypedWithoutRestoringForeground);
        Assert.Equal(
            new[] { "octocat", "personal-hunter2" },
            autoType.LastTokens
                .Where(token => token.Kind == AutoTypeTokenKind.Text)
                .Select(token => token.Value)
                .ToArray());
    }

    [Fact]
    public async Task Auto_type_picker_filters_and_escapes_without_ever_typing()
    {
        var hotkey = new RecordingGlobalHotkeyService();
        var autoType = new RecordingAutoTypeService
        {
            ForegroundTitle = "Local Console"
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
        window.DataContext = viewModel;

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 1,
            Title = "GitHub Personal",
            Website = "github.com",
            Username = "octocat",
            Password = "personal-hunter2"
        });
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 2,
            Title = "Notion",
            Website = "notion.so",
            Username = "notes",
            Password = "notes-hunter2"
        });
        viewModel.AutoTypeEnabled = true;
        await PumpDebounceAsync();

        var press = hotkey.Callback(GlobalHotkeySlot.AutoType);
        press!.Invoke();
        await PumpDebounceAsync();

        // Nothing matched the title, so the whole vault is on screen and the filter box has the
        // keyboard: typing narrows it instead of the user hunting through pages.
        Assert.Equal(
            MainWindowViewModel.AutoTypeOutcome.PickerForAllEntries,
            viewModel.LastAutoTypeOutcome);
        var picker = window.ActiveAutoTypePicker!;
        Assert.Equal(2, picker.VisibleCandidateCount);
        Assert.True(picker.FilterHasFocusRequest);

        picker.InnerFilterBox!.Text = "notio";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, picker.VisibleCandidateCount);
        Assert.Equal("Notion", Assert.IsType<AutoTypeCandidate>(picker.InnerEntryList!.SelectedItem).Title);

        picker.InnerFilterBox.Text = "zzz";
        Dispatcher.UIThread.RunJobs();

        // With nothing left to send, Enter leaves the list up instead of dismissing a press the user
        // meant as "fill this in"; the filter is what has to change.
        Assert.Equal(0, picker.VisibleCandidateCount);
        picker.InnerFilterBox.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });
        Assert.True(window.IsAutoTypePickerVisible);
        Assert.Equal(0, autoType.TypeCallCount);

        picker.InnerFilterBox.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
        });
        Dispatcher.UIThread.RunJobs();

        Assert.Null(window.ActiveAutoTypePicker);
        Assert.False(viewModel.IsAutoTypePickerOpen);
        Assert.Equal(0, autoType.TypeCallCount);
    }

    [Fact]
    public async Task The_Enter_that_commits_an_input_method_composition_picks_the_row_it_narrowed_to()
    {
        var hotkey = new RecordingGlobalHotkeyService();
        var autoType = new RecordingAutoTypeService { ForegroundTitle = "Sign in to Contoso" };
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
        window.DataContext = viewModel;

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 1,
            Title = "GitHub Personal",
            Website = "github.com",
            Username = "octocat",
            Password = "personal-hunter2"
        });
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 2,
            Title = "Notion",
            Website = "notion.so",
            Username = "notes",
            Password = "notes-hunter2"
        });
        viewModel.AutoTypeEnabled = true;
        await PumpDebounceAsync();

        hotkey.Callback(GlobalHotkeySlot.AutoType)!.Invoke();
        await PumpDebounceAsync();
        var picker = window.ActiveAutoTypePicker!;
        Assert.Equal(2, picker.VisibleCandidateCount);

        // Measured on a Chinese Windows with an input method active: the key down of every keystroke it
        // is composing - including the Enter that commits the composition - arrives as ImeProcessed, and
        // the committed text reaches the box only after the key up. A picker that confirmed on the key up
        // would send the row the stale text was highlighting, which is a different account.
        picker.InnerFilterBox!.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.ImeProcessed,
        });
        picker.InnerFilterBox.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Key = Key.Return,
        });
        Assert.True(window.IsAutoTypePickerVisible);
        Assert.Equal(0, autoType.TypeCallCount);

        picker.InnerFilterBox.Text = "notio";
        Dispatcher.UIThread.RunJobs();

        Assert.Null(window.ActiveAutoTypePicker);
        Assert.Equal(
            new[] { "notes", "notes-hunter2" },
            autoType.LastTokens
                .Where(token => token.Kind == AutoTypeTokenKind.Text)
                .Select(token => token.Value)
                .ToArray());
    }

    [Fact]
    public async Task Auto_type_press_while_the_picker_is_up_takes_it_down()
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
        window.DataContext = viewModel;

        viewModel.IsUnlocked = true;
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 1,
            Title = "GitHub Personal",
            Website = "github.com",
            Username = "octocat",
            Password = "personal-hunter2"
        });
        viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry
        {
            Id = 2,
            Title = "GitHub Work",
            Website = "github.com",
            Username = "work",
            Password = "work-hunter2"
        });
        viewModel.AutoTypeEnabled = true;
        await PumpDebounceAsync();

        var press = hotkey.Callback(GlobalHotkeySlot.AutoType);
        press!.Invoke();
        await PumpDebounceAsync();
        Assert.True(window.IsAutoTypePickerVisible);

        press.Invoke();
        await PumpDebounceAsync();

        Assert.Null(window.ActiveAutoTypePicker);
        Assert.False(viewModel.IsAutoTypePickerOpen);
        Assert.Equal(0, autoType.TypeCallCount);

        viewModel.Passwords.Clear();
        press.Invoke();
        await PumpDebounceAsync();

        Assert.True(window.IsAutoTypePickerVisible);
        Assert.Equal(0, window.ActiveAutoTypePicker!.VisibleCandidateCount);

        // A list of the whole vault is only usable by typing into it, so the window has to hold the
        // keyboard itself rather than just sit on top of the target. Focus used to be requested before
        // the window existed, and the desktop dropped it silently.
        Assert.True(window.ActiveAutoTypePicker.FilterHasFocusRequest);
        Assert.True(window.ActiveAutoTypePicker.InnerFilterBox!.IsFocused);

        // Locking the vault retires a list of account names with it.
        viewModel.IsUnlocked = false;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(window.ActiveAutoTypePicker);
        Assert.False(viewModel.IsAutoTypePickerOpen);
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
        public bool RestoresSucceed { get; set; } = true;
        public int RestoreCallCount { get; private set; }
        public IntPtr LastRestoredHandle { get; private set; }
        public bool TypedWithoutRestoringForeground { get; private set; }

        public IntPtr GetForegroundWindow() => ForegroundHandle;

        public void Reset()
        {
            TypeCallCount = 0;
            RestoreCallCount = 0;
            LastRestoredHandle = IntPtr.Zero;
            TypedWithoutRestoringForeground = false;
            LastTokens = [];
            LastError = "";
        }

        public string GetWindowTitle(IntPtr windowHandle) =>
            windowHandle == ForegroundHandle ? ForegroundTitle : "";

        public bool IsWindowOwnedByThisProcess(IntPtr windowHandle) =>
            ForegroundIsOwned && windowHandle == ForegroundHandle;

        public bool TryRestoreForeground(IntPtr windowHandle)
        {
            RestoreCallCount++;
            LastRestoredHandle = windowHandle;
            LastError = RestoresSucceed ? "" : "The target window did not take the focus back.";
            return RestoresSucceed;
        }

        public bool TryType(IReadOnlyList<AutoTypeToken> tokens)
        {
            // Catches the ordering bug the picker introduces: keystrokes sent while one of our own
            // windows still holds the focus land in Monica, not in the application the user pointed at.
            if (RestoreCallCount == 0)
            {
                TypedWithoutRestoringForeground = true;
            }

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
