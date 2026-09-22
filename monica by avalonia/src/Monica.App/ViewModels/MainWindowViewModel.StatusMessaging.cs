using Monica.App.Services;

namespace Monica.App.ViewModels;

// Status messaging exists as a single funnel: every user-visible line in the status bar, the
// unlock banner and the amber "this did not go through" strip is written here and nowhere else.
// StatusMessage therefore has no setter at all — `StatusMessage = …` does not compile anywhere,
// including inside this class, so a new call site cannot silently skip classification.
public sealed partial class MainWindowViewModel
{
    private string _statusMessage = string.Empty;
    private bool _isStatusMessageFailure;

    public string StatusMessage => _statusMessage;

    // Failure used to be guessed by re-reading the already-translated sentence for words like
    // "failed"/"无法", which made one error prominent in Chinese and invisible in English. Intent
    // is now declared where the message is produced, so the verdict cannot depend on wording or
    // on the active language.
    public bool IsStatusMessageFailure => _isStatusMessageFailure;

    // While locked the lock-screen banner already carries the message; while loading a vault the
    // strip would compete with the progress surface it is meant to replace once that finishes.
    public bool HasFailedStatusMessage => IsUnlocked && !IsLoadingVault && IsStatusMessageFailure;

    // Informational: progress, results, and prompts the status bar can carry on its own.
    private void SetStatusMessage(string messageKey, params object[] args) =>
        WriteStatus(isFailure: false, messageKey, args);

    // An operation the user asked for did not succeed — this is what earns the amber strip.
    private void SetStatusFailure(string messageKey, params object[] args) =>
        WriteStatus(isFailure: true, messageKey, args);

    private void ClearStatusMessage() => WriteStatus(isFailure: false, messageKey: "", args: []);

    private void WriteStatus(bool isFailure, string messageKey, object[] args)
    {
        _statusMessage = string.IsNullOrEmpty(messageKey)
            ? string.Empty
            : args.Length == 0
                ? _localization.Get(messageKey)
                : _localization.Format(messageKey, args);
        _isStatusMessageFailure = isFailure;
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HasUnlockStatusMessage));
        OnPropertyChanged(nameof(HasFailedStatusMessage));
    }
}
