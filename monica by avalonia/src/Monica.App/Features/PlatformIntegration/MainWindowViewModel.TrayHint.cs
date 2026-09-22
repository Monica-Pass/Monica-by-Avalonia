using Monica.App.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // Long enough to read two sentences while the pointer travels toward the notification area, short
    // enough that it is gone before anyone thinks to dismiss it.
    internal TimeSpan TrayHintDwell { get; } = TimeSpan.FromSeconds(8);

    // ReloadSettingsAfterUnlockAsync and the startup load both replace the in-memory settings object
    // with the file copy, and the file copy lags writes by the save debounce, so the persisted flag
    // alone cannot promise one notice per run. The measured symptom was the bubble reappearing on the
    // second minimize after a re-show reloaded settings.
    private bool _trayHintSurfacedThisSession;

    // Whether the bubble still owes this install an explanation. Deliberately not a bound property:
    // nothing on the settings page toggles it, it is written once by the first hide.
    internal bool ShouldSurfaceTrayHint() =>
        MinimizeToTray && !_trayHintSurfacedThisSession && !_settingsService.Current.TrayHintShown;

    internal void MarkTrayHintSurfaced()
    {
        _trayHintSurfacedThisSession = true;
        UpdateSettings(settings => settings.TrayHintShown = true);
    }

    internal string TrayHintTitleText => _localization.TrayHintTitle;

    internal string TrayHintBodyText => _localization.TrayHintBody;

    // Same sentence the tray menu item uses, so the bubble names the control it is pointing at.
    internal string TrayHintShowActionText => _localization.Get("ShowMonica");
}
