using CommunityToolkit.Mvvm.Input;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanCopyBrowserIntegrationToken))]
    private async Task CopyBrowserIntegrationTokenAsync()
    {
        if (!CanCopyBrowserIntegrationToken())
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(BrowserIntegrationSessionToken);
        StatusMessage = _localization.Get("BrowserBridgeTokenCopied");
    }

    private bool CanCopyBrowserIntegrationToken() =>
        BrowserBridgeIsRunning && !string.IsNullOrWhiteSpace(BrowserIntegrationSessionToken);
}
