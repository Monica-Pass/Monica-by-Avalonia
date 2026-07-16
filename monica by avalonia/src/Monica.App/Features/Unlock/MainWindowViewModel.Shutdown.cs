namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly object _shutdownSync = new();
    private Task? _shutdownTask;

    internal Task PrepareForShutdownAsync()
    {
        lock (_shutdownSync)
        {
            return _shutdownTask ??= PrepareForShutdownCoreAsync();
        }
    }

    private async Task PrepareForShutdownCoreAsync()
    {
        if (IsUnlocked || _cryptoService.IsUnlocked)
        {
            await LockAsync();
            return;
        }

        var settingsSaveCompletion = SuspendSettingsSaveAsync();
        _vaultSessionService.MarkLocked();
        ClearSensitiveSessionState();
        await settingsSaveCompletion;
        await ClearSettingsSensitiveCacheAsync();
        await ClearOwnedClipboardAsync();
    }
}
