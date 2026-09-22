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
        NoMatch,
        Ambiguous,
        InjectionFailed,
        Typed,
    }

    internal AutoTypeOutcome LastAutoTypeOutcome { get; private set; } = AutoTypeOutcome.None;

    internal IReadOnlyList<PasswordEntry> LastAutoTypeMatches { get; private set; } = [];

    // Types the matched credential into whatever window had focus when the hotkey was pressed. The
    // caller (the desktop integration coordinator) resolves the foreground window first and says
    // whether it belongs to Monica, because typing into our own window is always a mistake and the
    // ViewModel has no handle on the shell.
    internal void RunAutoTypeIntoForeground(IntPtr foregroundHandle, string windowText, bool foregroundIsMonicaWindow)
    {
        if (!IsUnlocked)
        {
            LastAutoTypeMatches = [];
            CompleteAutoType(AutoTypeOutcome.Locked);
            return;
        }

        if (foregroundIsMonicaWindow)
        {
            LastAutoTypeMatches = [];
            CompleteAutoType(AutoTypeOutcome.MonicaIsForeground);
            return;
        }

        if (foregroundHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(windowText))
        {
            LastAutoTypeMatches = [];
            CompleteAutoType(AutoTypeOutcome.NoTargetWindow);
            return;
        }

        var matches = AutoTypeMatcher.Match(Passwords, windowText);
        LastAutoTypeMatches = matches;
        if (matches.Count == 0)
        {
            CompleteAutoType(AutoTypeOutcome.NoMatch);
            return;
        }

        if (matches.Count > 1)
        {
            CompleteAutoType(AutoTypeOutcome.Ambiguous);
            return;
        }

        var entry = matches[0];
        var tokens = AutoTypeMatcher.BuildTokens(entry);
        if (tokens.Count == 0 || !_autoTypeService.TryType(tokens))
        {
            LastAutoTypeMatches = [];
            CompleteAutoType(AutoTypeOutcome.InjectionFailed);
            return;
        }

        CompleteAutoType(AutoTypeOutcome.Typed, entry.Title);
    }

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
            case AutoTypeOutcome.NoMatch:
                SetStatusFailure("AutoTypeNoMatch");
                break;
            case AutoTypeOutcome.Ambiguous:
                SetStatusFailure("AutoTypeAmbiguousFormat", LastAutoTypeMatches.Count);
                break;
            case AutoTypeOutcome.InjectionFailed:
                SetStatusFailure("AutoTypeInjectionFailedFormat", _autoTypeService.LastError);
                break;
        }
    }
}
