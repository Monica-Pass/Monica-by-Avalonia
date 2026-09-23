using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Monica.App.Controls;
using Monica.Platform.Services;

namespace Monica.UiTests;

/// <summary>
/// The recorder is the only place a shortcut gets decided, and it keeps two promises at once: every
/// combination it accepts must be one the registration path can actually reserve, and every
/// combination it refuses must leave the stored setting untouched. The second half is why the refusal
/// cases assert on <see cref="HotkeyRecorder.Gesture"/> rather than only on the message - a recorder
/// that shows "unsupported" while quietly overwriting the hotkey is worse than one that says nothing.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class HotkeyRecorderUiTests : IDisposable
{
    private const string Stored = "Ctrl+Shift+Space";
    private const string Prompt = "Press a shortcut";
    private const string Refused = "Combine another key with Ctrl, Alt or Win.";

    private readonly Window _host = new() { Width = 520, Height = 320 };
    private readonly TextBox _elsewhere = new();

    public HotkeyRecorderUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Recorder_shows_the_stored_gesture_until_you_click_it()
    {
        var recorder = Host(Stored);

        Assert.Equal(Stored, recorder.DisplayText);
        Assert.False(recorder.IsArmed);

        Arm(recorder);

        Assert.True(recorder.IsArmed);
        Assert.Equal(Prompt, recorder.DisplayText);
        Assert.True(recorder.InnerRecordButton!.IsFocused);
    }

    [Fact]
    public void Armed_recorder_takes_the_next_combination_from_the_keyboard()
    {
        var recorder = Host(Stored);
        Arm(recorder);

        var handled = Press(recorder, Key.K, KeyModifiers.Control | KeyModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.True(handled);
        Assert.Equal("Ctrl+Shift+K", recorder.Gesture);
        Assert.False(recorder.IsArmed);
        Assert.Equal("Ctrl+Shift+K", recorder.DisplayText);
    }

    [Fact]
    public void Armed_recorder_keeps_the_stored_gesture_when_escape_ends_the_recording()
    {
        var recorder = Host(Stored);
        Arm(recorder);

        Press(recorder, Key.Escape, KeyModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.False(recorder.IsArmed);
        Assert.Equal(Stored, recorder.Gesture);
        Assert.Equal(Stored, recorder.DisplayText);
    }

    [Fact]
    public void A_bare_modifier_keeps_the_recorder_listening_instead_of_refusing()
    {
        var recorder = Host(Stored);
        Arm(recorder);

        Assert.False(recorder.TryCommit(KeyModifiers.Control, Key.LeftCtrl));
        Assert.False(recorder.TryCommit(KeyModifiers.Control, Key.None));

        Assert.True(recorder.IsArmed);
        Assert.False(recorder.IsShowingInvalid);
        Assert.Equal(Prompt, recorder.DisplayText);
        Assert.Equal(Stored, recorder.Gesture);
    }

    [Fact]
    public void Shift_is_not_enough_for_a_system_wide_shortcut_and_saves_nothing()
    {
        var recorder = Host(Stored);
        Arm(recorder);

        Assert.False(recorder.TryCommit(KeyModifiers.Shift, Key.K));

        Assert.True(recorder.IsShowingInvalid);
        Assert.Equal(Refused, recorder.DisplayText);
        Assert.Equal(Stored, recorder.Gesture);
    }

    [Fact]
    public void A_key_the_desktop_cannot_name_is_refused_instead_of_stored()
    {
        var recorder = Host(Stored);
        Arm(recorder);

        Assert.False(recorder.TryCommit(KeyModifiers.Control, Key.CapsLock));

        Assert.True(recorder.IsShowingInvalid);
        Assert.Equal(Stored, recorder.Gesture);
    }

    [Theory]
    [InlineData(KeyModifiers.Control, Key.D5, "Ctrl+5")]
    [InlineData(KeyModifiers.Control, Key.A, "Ctrl+A")]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, Key.F9, "Ctrl+Alt+F9")]
    [InlineData(KeyModifiers.Control | KeyModifiers.Meta, Key.Space, "Ctrl+Win+Space")]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, Key.Enter, "Ctrl+Shift+Enter")]
    [InlineData(KeyModifiers.Alt | KeyModifiers.Shift, Key.PageUp, "Alt+Shift+PageUp")]
    public void Every_gesture_the_recorder_accepts_is_one_the_registration_path_reads(
        KeyModifiers modifiers,
        Key key,
        string expected)
    {
        var recorder = Host(Stored);
        Arm(recorder);

        Assert.True(recorder.TryCommit(modifiers, key));
        Assert.Equal(expected, recorder.Gesture);
        Assert.True(
            WindowsGlobalHotkeyService.TryParseGesture(
                recorder.Gesture,
                out _,
                out _,
                out var normalized,
                out var error),
            $"Recorded {recorder.Gesture} but registration would refuse it: {error}");
        Assert.False(string.IsNullOrEmpty(normalized));
        Assert.Equal(expected, recorder.DisplayText);
    }

    [Fact]
    public void Clicking_away_abandons_the_recording_with_the_gesture_untouched()
    {
        var recorder = Host(Stored);
        Arm(recorder);

        _elsewhere.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.False(recorder.IsArmed);
        Assert.Equal(Stored, recorder.Gesture);
        Assert.Equal(Stored, recorder.DisplayText);
    }

    [Fact]
    public void A_gesture_pushed_from_the_setting_redraws_the_row()
    {
        var recorder = Host(Stored);

        recorder.Gesture = "Ctrl+Alt+F7";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Ctrl+Alt+F7", recorder.DisplayText);
    }

    private HotkeyRecorder Host(string gesture)
    {
        var recorder = new HotkeyRecorder
        {
            Gesture = gesture,
            PromptText = Prompt,
            InvalidText = Refused,
        };
        _host.Content = new StackPanel
        {
            Spacing = 8,
            Children = { recorder, _elsewhere },
        };
        _host.Show();
        Dispatcher.UIThread.RunJobs();
        return recorder;
    }

    private static void Arm(HotkeyRecorder recorder)
    {
        recorder.InnerRecordButton!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static bool Press(HotkeyRecorder recorder, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        };
        recorder.InnerRecordButton!.RaiseEvent(args);
        return args.Handled;
    }

    public void Dispose() => _host.Close();
}
