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
        SetStatusNotice("BrowserBridgeTokenCopied");
    }

    private bool CanCopyBrowserIntegrationToken() =>
        BrowserBridgeIsRunning && !string.IsNullOrWhiteSpace(BrowserIntegrationSessionToken);
}
