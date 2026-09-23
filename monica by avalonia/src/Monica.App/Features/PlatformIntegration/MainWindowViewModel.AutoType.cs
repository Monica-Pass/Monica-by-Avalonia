using Monica.App.Services;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // What the auto-type hotkey decided the last time it fired. The status line tells the user the
    // same thing in prose; this is the machine-readable verdict the smoke probe and the tests read.
    internal enum AutoTypeOutcome
    {
        None,
        Locked,
        NoTargetWindow,
        MonicaIsForeground,
        PickerForMatches,
        PickerForAllEntries,
        TargetUnavailable,
        InjectionFailed,
        SequenceInvalid,
        NothingToType,
        Typed,
    }

    internal AutoTypeOutcome LastAutoTypeOutcome { get; private set; } = AutoTypeOutcome.None;

    internal IReadOnlyList<PasswordEntry> LastAutoTypeMatches { get; private set; } = [];

    internal bool IsAutoTypePickerOpen { get; private set; }

    internal IReadOnlyList<AutoTypeCandidate> AutoTypePickerCandidates { get; private set; } = [];

    // Says whether the list holds a handful of title matches or the whole vault. A short list is
    // meant to be picked from, so the list keeps the keyboard; a full vault has to be filtered first,
    // so the filter box takes it.
    internal bool AutoTypePickerListsAllEntries { get; private set; }

    internal string AutoTypePickerHeadingText =>
        _localization.Format("AutoTypePickerHeadingFormat", ShortenAutoTypeTarget(_autoTypeTargetTitle));

    internal string AutoTypePickerFilterPlaceholderText => _localization.Get("AutoTypePickerFilterPlaceholder");

    internal string AutoTypePickerHintText => _localization.Get("AutoTypePickerHint");

    // The window the credential is headed for, kept between the hotkey press and the moment the user
    // picks a row. Nothing is sent until that handle has the keyboard back.
    private IntPtr _autoTypeTargetHandle;
    private string _autoTypeTargetTitle = "";
    private IReadOnlyList<PasswordEntry> _autoTypeTargetEntries = [];

    /// <summary>
    /// Types the matched credential into whatever window had focus when the hotkey was pressed. The
    /// caller (the desktop integration coordinator) resolves the foreground window first and says
    /// whether it belongs to Monica, because typing into our own window is always a mistake and the
    /// ViewModel has no handle on the shell.
    /// </summary>
    internal void RunAutoTypeIntoForeground(IntPtr foregroundHandle, string windowText, bool foregroundIsMonicaWindow)
    {
        if (!IsUnlocked)
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.Locked);
            return;
        }

        if (foregroundIsMonicaWindow)
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.MonicaIsForeground);
            return;
        }

        if (foregroundHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(windowText))
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.NoTargetWindow);
            return;
        }

        var matches = AutoTypeMatcher.Match(Passwords, windowText);
        LastAutoTypeMatches = matches;
        _autoTypeTargetHandle = foregroundHandle;
        _autoTypeTargetTitle = windowText;

        if (matches.Count == 1)
        {
            // The common case stays one keystroke: a unique match types straight through, exactly as
            // it did before the picker existed.
            _autoTypeTargetEntries = matches;
            TypeIntoAutoTypeTarget(matches[0]);
            return;
        }

        IsAutoTypePickerOpen = true;
        AutoTypePickerListsAllEntries = matches.Count == 0;
        _autoTypeTargetEntries = AutoTypePickerListsAllEntries ? Passwords.ToArray() : matches;
        AutoTypePickerCandidates = AutoTypeMatcher.Candidates(_autoTypeTargetEntries);
        CompleteAutoType(AutoTypePickerListsAllEntries
            ? AutoTypeOutcome.PickerForAllEntries
            : AutoTypeOutcome.PickerForMatches);
    }

    /// <summary>
    /// Runs the row the user picked. The picker took the keyboard, so the first thing this does after
    /// closing the list is hand the focus back to the target window and refuse to type unless it worked.
    /// </summary>
    internal void CompleteAutoTypeFromPicker(AutoTypeCandidate candidate)
    {
        if (!IsAutoTypePickerOpen)
        {
            return;
        }

        var entry = _autoTypeTargetEntries.FirstOrDefault(item => item.Id == candidate.EntryId);
        CloseAutoTypePicker();
        if (!IsUnlocked)
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.Locked);
            return;
        }

        if (entry is null)
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.TargetUnavailable);
            return;
        }

        TypeIntoAutoTypeTarget(entry);
    }

    internal void CancelAutoTypePicker()
    {
        if (!IsAutoTypePickerOpen)
        {
            return;
        }

        CloseAutoTypePicker();
        RetireAutoTypeTarget();
        SetStatusNotice("AutoTypePickerCancelled");
    }

    private void TypeIntoAutoTypeTarget(PasswordEntry entry)
    {
        var targetHandle = _autoTypeTargetHandle;
        // The sequence is the user's, so it is re-checked here rather than trusted from the settings
        // page: the file could have been edited on disk while the app was closed.
        if (!AutoTypeSequenceParser.TryBuild(
                AutoTypeSequence,
                entry.Username,
                entry.Password,
                out var tokens,
                out _))
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.SequenceInvalid);
            return;
        }

        if (tokens.Count == 0)
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.NothingToType);
            return;
        }

        if (!_autoTypeService.TryRestoreForeground(targetHandle))
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.TargetUnavailable);
            return;
        }

        if (!_autoTypeService.TryType(tokens))
        {
            RetireAutoTypeTarget();
            CompleteAutoType(AutoTypeOutcome.InjectionFailed);
            return;
        }

        _autoTypeTargetEntries = [];
        RetireAutoTypeTarget();
        CompleteAutoType(AutoTypeOutcome.Typed, entry.Title);
    }

    private void CloseAutoTypePicker()
    {
        IsAutoTypePickerOpen = false;
        AutoTypePickerCandidates = [];
        _autoTypeTargetEntries = [];
    }

    private void RetireAutoTypeTarget()
    {
        _autoTypeTargetHandle = IntPtr.Zero;
        _autoTypeTargetTitle = "";
    }

    private static string ShortenAutoTypeTarget(string value) =>
        value.Length <= 48 ? value : value[..45] + "...";

    private void CompleteAutoType(AutoTypeOutcome outcome, string? targetLabel = null)
    {
        LastAutoTypeOutcome = outcome;
        switch (outcome)
        {
            case AutoTypeOutcome.Typed:
                SetStatusNotice("AutoTypedIntoForegroundFormat", targetLabel ?? "");
                break;
            case AutoTypeOutcome.Locked:
                SetStatusFailure("AutoTypeLocked");
                break;
            case AutoTypeOutcome.MonicaIsForeground:
                SetStatusFailure("AutoTypeMonicaIsForeground");
                break;
            case AutoTypeOutcome.NoTargetWindow:
                SetStatusFailure("AutoTypeNoTargetWindow");
                break;
            case AutoTypeOutcome.PickerForMatches:
                SetStatusNotice("AutoTypePickerMatchesFormat", LastAutoTypeMatches.Count);
                break;
            case AutoTypeOutcome.PickerForAllEntries:
                SetStatusNotice("AutoTypePickerNoMatch");
                break;
            case AutoTypeOutcome.TargetUnavailable:
                SetStatusFailure("AutoTypeTargetUnavailable");
                break;
            case AutoTypeOutcome.InjectionFailed:
                SetStatusFailure("AutoTypeInjectionFailedFormat", _autoTypeService.LastError);
                break;
            case AutoTypeOutcome.SequenceInvalid:
                SetStatusFailure("AutoTypeSequenceInvalidFormat", AutoTypeSequenceErrorText);
                break;
            case AutoTypeOutcome.NothingToType:
                SetStatusFailure("AutoTypeNothingToType");
                break;
        }
    }
}
