using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Monica.Platform.Services;

namespace Monica.App.Controls;

/// <summary>
/// The settings control for a system-wide shortcut. It replaces a text box because a typed gesture is
/// wrong more often than it is right - "Ctrl+Shift+Space" and "Ctrl + Shift + Space" and a combination
/// the operating system already owns all look identical while you type them - and because a global
/// shortcut that needs no real modifier (Shift+A, say) is taken the moment another application is
/// focused. So the user presses the combination instead of spelling it out, and the same parser the
/// registration path uses decides on the spot whether it can be honoured.
/// </summary>
public partial class HotkeyRecorder : UserControl
{
    public static readonly StyledProperty<string> GestureProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(Gesture), string.Empty);

    public static readonly StyledProperty<string> PromptTextProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(PromptText), "Press a shortcut");

    public static readonly StyledProperty<string> InvalidTextProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(InvalidText), "Unsupported shortcut");

    public static readonly StyledProperty<string> AutomationNameProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(AutomationName), "Hotkey");

    public static readonly StyledProperty<string> AutomationHelpTextProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(AutomationHelpText), string.Empty);

    public static readonly StyledProperty<string> DisplayTextProperty =
        AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(DisplayText), string.Empty);

    private bool _isArmed;
    private bool _showInvalid;

    public HotkeyRecorder()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        UpdateDisplay();
    }

    public string Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public string PromptText
    {
        get => GetValue(PromptTextProperty);
        set => SetValue(PromptTextProperty, value);
    }

    public string InvalidText
    {
        get => GetValue(InvalidTextProperty);
        set => SetValue(InvalidTextProperty, value);
    }

    public string AutomationName
    {
        get => GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public string AutomationHelpText
    {
        get => GetValue(AutomationHelpTextProperty);
        set => SetValue(AutomationHelpTextProperty, value);
    }

    public string DisplayText
    {
        get => GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextProperty, value);
    }

    internal bool IsArmed => _isArmed;

    internal bool IsShowingInvalid => _showInvalid;

    internal Button? InnerRecordButton => RecordButton;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateDisplay();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GestureProperty ||
            change.Property == PromptTextProperty ||
            change.Property == InvalidTextProperty)
        {
            UpdateDisplay();
        }
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Disarm();
    }

    private void OnRecordButtonClicked(object? sender, RoutedEventArgs e)
    {
        _isArmed = true;
        _showInvalid = false;
        RecordButton.Focus();
        UpdateDisplay();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_isArmed)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            Disarm();
            return;
        }

        TryCommit(e.KeyModifiers, e.Key);
    }

    /// <summary>
    /// Takes one pressed combination and decides it: still waiting for the non-modifier key, refused
    /// because nothing but Shift is held, or accepted and written to the bound setting. Exposed for
    /// the tests; the application reaches it through the keyboard.
    /// </summary>
    internal bool TryCommit(KeyModifiers modifiers, Key key)
    {
        if (key is Key.None or
            Key.LeftCtrl or Key.RightCtrl or
            Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt or
            Key.LWin or Key.RWin)
        {
            // A bare modifier is only half a shortcut, and the prompt is already on screen saying so.
            return false;
        }

        var gesture = BuildGesture(modifiers, key);
        var hasPrimaryModifier = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0;
        if (gesture is null ||
            !hasPrimaryModifier ||
            !WindowsGlobalHotkeyService.TryParseGesture(gesture, out _, out _, out _, out _))
        {
            _showInvalid = true;
            UpdateDisplay();
            return false;
        }

        Gesture = gesture;
        _isArmed = false;
        _showInvalid = false;
        UpdateDisplay();
        return true;
    }

    private void Disarm()
    {
        if (!_isArmed && !_showInvalid)
        {
            return;
        }

        _isArmed = false;
        _showInvalid = false;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        DisplayText = !_isArmed
            ? Gesture
            : _showInvalid ? InvalidText : PromptText;
    }

    private static string? BuildGesture(KeyModifiers modifiers, Key key)
    {
        var keyName = KeyNameOf(key);
        if (keyName is null)
        {
            return null;
        }

        var parts = new List<string>(4);
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Win");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(keyName);
        return string.Join('+', parts);
    }

    private static string? KeyNameOf(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Space => "Space",
        Key.Enter => "Enter",
        Key.Tab => "Tab",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Insert => "Insert",
        _ => null,
    };
}
