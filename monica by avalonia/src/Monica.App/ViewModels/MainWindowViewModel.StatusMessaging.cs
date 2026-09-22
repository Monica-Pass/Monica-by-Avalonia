using Monica.App.Services;

namespace Monica.App.ViewModels;

// Status messaging exists as a single funnel: every user-visible line in the status bar, the
// unlock banner and the amber "this did not go through" strip is written here and nowhere else.
// StatusMessage therefore has no setter at all — `StatusMessage = …` does not compile anywhere,
// including inside this class, so a new call site cannot silently skip classification.
//
// Three intents, and what separates them is how long the sentence stays true:
//   SetStatusNotice   "已复制 GitHub 的密码" — an acknowledgement of something that already
//                     happened. It stops being information once it has been read, so it retires
//                     itself instead of claiming "已打开 25 条" above a preview the user has since
//                     closed, or naming a copied secret on a screen left open for hours.
//   SetStatusMessage  "请输入当前主密码" / "正在同步" — a prompt the user still has to act on, or
//                     a state that is still true. It stays until something replaces it.
//   SetStatusFailure  the operation the user asked for did not succeed. Stays, and earns the strip.
public sealed partial class MainWindowViewModel
{
    // How long a completed-action acknowledgement stays on screen before it retires itself. Long
    // enough to be read after it lands in a low-contrast strip; the clock behind it is faked in
    // tests, so the value does not have to shrink to be verified.
    internal TimeSpan StatusNoticeDwell { get; } = TimeSpan.FromSeconds(8);

    internal TimeProvider StatusTimeProvider { get; set; } = TimeProvider.System;

    private string _statusMessageKey = string.Empty;
    private object[] _statusMessageArgs = [];
    private bool _isStatusMessageFailure;
    private long _statusNoticeStartedAt;

    // Resolved at read time, so switching language in Settings retranslates the line that is
    // already on screen instead of leaving a sentence in the previous language until something
    // happens to replace it. Storing the key is also what keeps the funnel free of translated
    // text it would otherwise have to reverse-engineer to decide anything about the message.
    public string StatusMessage => _statusMessageKey.Length == 0
        ? string.Empty
        : _statusMessageArgs.Length == 0
            ? _localization.Get(_statusMessageKey)
            : _localization.Format(_statusMessageKey, _statusMessageArgs);

    // Failure used to be guessed by re-reading the already-translated sentence for words like
    // "failed"/"无法", which made one error prominent in Chinese and invisible in English. Intent
    // is now declared where the message is produced, so the verdict cannot depend on wording or
    // on the active language.
    public bool IsStatusMessageFailure => _isStatusMessageFailure;

    // While locked the lock-screen banner already carries the message; while loading a vault the
    // strip would compete with the progress surface it is meant to replace once that finishes.
    public bool HasFailedStatusMessage => IsUnlocked && !IsLoadingVault && IsStatusMessageFailure;

    // The window owns the actual timer — a ViewModel must not reach for a dispatcher, and a notice
    // written inside a plain unit test has no UI to retire in front of. This says whether the line
    // currently on screen is one that should eventually leave it.
    internal bool IsStatusNoticePending { get; private set; }

    // Tells the shell to re-arm (or stand down) the one-shot clock that retires a notice.
    internal event EventHandler? StatusNoticeScheduleChanged;

    // Prompts, in-progress lines and standing state: stays until replaced.
    private void SetStatusMessage(string messageKey, params object[] args) =>
        WriteStatus(StatusTone.Standing, messageKey, args);

    // An action the user asked for finished. Retires itself.
    private void SetStatusNotice(string messageKey, params object[] args) =>
        WriteStatus(StatusTone.Notice, messageKey, args);

    // An operation the user asked for did not succeed — this is what earns the amber strip.
    private void SetStatusFailure(string messageKey, params object[] args) =>
        WriteStatus(StatusTone.Failure, messageKey, args);

    private void ClearStatusMessage() => WriteStatus(StatusTone.Standing, messageKey: "", args: []);

    // A notice belongs to the screen that produced it. Leaving that screen retires it at once
    // instead of letting it linger until the dwell happens to expire.
    private void RetireNoticeOnNavigation()
    {
        if (IsStatusNoticePending) ClearStatusMessage();
    }

    // Called by the window's one-shot timer. The deadline is re-read here rather than trusted from
    // the schedule, so a tick that arrives early — after the window was minimized, or while the
    // machine was busy — leaves the sentence alone instead of cutting it short.
    internal void RetireExpiredStatusNotice()
    {
        if (!IsStatusNoticePending
            || StatusTimeProvider.GetElapsedTime(_statusNoticeStartedAt) < StatusNoticeDwell)
        {
            return;
        }

        ClearStatusMessage();
    }

    private enum StatusTone
    {
        Standing,
        Notice,
        Failure,
    }

    private void WriteStatus(StatusTone tone, string messageKey, object[] args)
    {
        _statusMessageKey = messageKey;
        _statusMessageArgs = args;
        _isStatusMessageFailure = tone == StatusTone.Failure;
        IsStatusNoticePending = tone == StatusTone.Notice;
        if (IsStatusNoticePending) _statusNoticeStartedAt = StatusTimeProvider.GetTimestamp();
        RaiseStatusMessageState();
        StatusNoticeScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseStatusMessageState()
    {
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasUnlockStatusMessage));
        OnPropertyChanged(nameof(HasFailedStatusMessage));
    }
}
