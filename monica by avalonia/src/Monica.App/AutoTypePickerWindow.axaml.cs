using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Monica.App.Services;

namespace Monica.App;

/// <summary>
/// The list the auto-type hotkey shows when the foreground window does not name exactly one entry -
/// several matched, or none did. Rows carry a title and a username and nothing else, because a
/// credential picker that prints the secret it is about to type is not a picker, and the whole point
/// of the list is that the user is looking at whatever window is behind it.
/// </summary>
internal sealed partial class AutoTypePickerWindow : Window
{
    private const double ScreenVerticalRatio = 0.28;

    private readonly List<AutoTypeCandidate> _candidates = [];
    private bool _isResolving;
    private bool _filterHasFocusRequest;
    private bool _entryListHasFocusRequest;
    private bool _isOpen;
    private bool? _focusFilterFirst;
    private bool _isComposing;
    private bool _confirmWhenFilterNextChanges;

    internal AutoTypePickerWindow(
        IReadOnlyList<AutoTypeCandidate> candidates,
        string heading,
        string filterPlaceholder,
        string hint)
    {
        InitializeComponent();
        _candidates.AddRange(candidates);
        TargetHeading.Text = heading;
        FilterBox.PlaceholderText = filterPlaceholder;
        AutomationProperties.SetName(FilterBox, filterPlaceholder);
        HintLine.Text = hint;
        ApplyFilter("");

        // Tunnelled from the window so Escape and Enter mean the same thing wherever the keyboard is,
        // including inside the filter box, where a bubbling handler would never see them.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        FilterBox.TextChanged += (_, _) => ApplyFilter(FilterBox.Text);
        EntryList.DoubleTapped += (_, _) => ConfirmSelection();
        Opened += (_, _) =>
        {
            _isOpen = true;
            AnchorToScreen();
            DeliverFocusRequest();
        };
    }

    internal event Action<AutoTypeCandidate>? EntryPicked;

    internal event Action? DismissRequested;

    internal int VisibleCandidateCount { get; private set; }

    // Which control was asked for the keyboard. Focus delivery itself depends on the window manager,
    // so this records the decision the picker makes and the real-artifact probe checks the effect.
    internal bool FilterHasFocusRequest => _filterHasFocusRequest;

    internal bool EntryListHasFocusRequest => _entryListHasFocusRequest;

    internal ListBox? InnerEntryList => EntryList;

    internal TextBox? InnerFilterBox => FilterBox;

    /// <summary>
    /// Gives the keyboard to the control that answers the question the list is asking: a handful of
    /// matches is picked from, a whole vault has to be filtered down before it can be picked from.
    /// The decision is recorded here and then; handing over the keyboard can only happen once the
    /// window exists on the desktop, because a focus request against a window that has not opened is
    /// dropped, and the caller asks right after Show().
    /// </summary>
    internal void FocusForMode(bool filterFirst)
    {
        _filterHasFocusRequest = filterFirst;
        _entryListHasFocusRequest = !filterFirst;
        _focusFilterFirst = filterFirst;
        DeliverFocusRequest();
    }

    private void DeliverFocusRequest()
    {
        if (!_isOpen || _focusFilterFirst is not { } filterFirst)
        {
            return;
        }

        if (filterFirst)
        {
            FilterBox.Focus();
        }
        else
        {
            EntryList.Focus();
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // An input method that is composing swallows the key down of every keystroke it takes, including
        // the Enter that commits the composition, and reports it as ImeProcessed. Measured on a Chinese
        // Windows: the key up of that same press still carries the real key, so the confirmation of a
        // composed press lives there and this one only handles keys the input method left alone.
        _isComposing = e.Key == Key.ImeProcessed;
        if (_isComposing)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Dismiss();
            return;
        }

        if (e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            ConfirmSelection();
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_isComposing)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Dismiss();
            return;
        }

        if (e.Key is Key.Enter or Key.Return)
        {
            // The text this Enter committed has not reached the filter box yet — it arrives just after
            // the key up — so picking a row here would send whichever row the stale text was showing.
            // The confirmation rides on the filter change instead.
            _confirmWhenFilterNextChanges = true;
        }
    }

    private void Dismiss()
    {
        if (!_isResolving)
        {
            DismissRequested?.Invoke();
        }
    }

    private void ConfirmSelection()
    {
        if (_isResolving)
        {
            return;
        }

        // No row to send means the filter excluded everything: the list stays up so the text can be
        // cleared, rather than dismissing a hotkey press the user meant as "fill this in".
        if (EntryList.SelectedItem is not AutoTypeCandidate candidate)
        {
            return;
        }

        _isResolving = true;
        EntryPicked?.Invoke(candidate);
    }

    private void ApplyFilter(string? value)
    {
        var needle = value?.Trim() ?? "";
        var visible = needle.Length == 0
            ? _candidates
            : _candidates
                .Where(candidate => candidate.FilterText.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        EntryList.ItemsSource = visible;
        EntryList.SelectedIndex = visible.Count > 0 ? 0 : -1;
        VisibleCandidateCount = visible.Count;

        if (_confirmWhenFilterNextChanges)
        {
            _confirmWhenFilterNextChanges = false;
            ConfirmSelection();
        }
    }

    // Screen areas are device pixels while every size here is device-independent, so the sizes are
    // converted before they are subtracted from the work area. ClientSize, not Bounds: a SizeToContent
    // window keeps reporting its pre-content bounds (measured on the tray hint).
    private void AnchorToScreen()
    {
        var screen = Screens?.Primary ?? Screens?.ScreenFromVisual(this);
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var widthDip = ClientSize.Width > 0 ? ClientSize.Width : Width;
        var area = screen.WorkingArea;
        Position = new PixelPoint(
            (int)Math.Round(area.X + (area.Width - widthDip * scaling) / 2),
            (int)Math.Round(area.Y + area.Height * ScreenVerticalRatio));
    }
}
