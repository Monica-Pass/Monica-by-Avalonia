using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;

namespace Monica.App.Services;

public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText,
        string? closeButtonText = null,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmTypedAsync(
        string title,
        string message,
        string requiredPhrase,
        string instruction,
        string primaryButtonText,
        string? closeButtonText = null,
        CancellationToken cancellationToken = default);
}

public sealed class ConfirmationDialogService(Func<Window> ownerProvider, ILocalizationService localization) : IConfirmationDialogService
{
    public async Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText,
        string? closeButtonText = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 420
        };
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = messageText,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText ?? localization.Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        return await ShowConfirmationAsync(dialog, () =>
        {
            messageText.Text = "";
            dialog.Title = null;
            dialog.Content = null;
        }, cancellationToken);
    }

    public async Task<bool> ConfirmTypedAsync(
        string title,
        string message,
        string requiredPhrase,
        string instruction,
        string primaryButtonText,
        string? closeButtonText = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var input = new TextBox
        {
            PlaceholderText = requiredPhrase,
            Width = 420
        };
        var messageText = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 420
        };
        var instructionText = new TextBlock
        {
            Text = instruction,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 420
        };

        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    messageText,
                    instructionText,
                    input
                }
            },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText ?? localization.Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        return await ShowConfirmationAsync(dialog, () =>
        {
            input.Text = "";
            input.PlaceholderText = "";
            messageText.Text = instructionText.Text = "";
            dialog.Title = null;
            dialog.Content = null;
        }, cancellationToken, () => string.Equals(input.Text?.Trim(), requiredPhrase, StringComparison.Ordinal));
    }

    private async Task<bool> ShowConfirmationAsync(
        FAContentDialog dialog, Action clearSensitiveState, CancellationToken cancellationToken,
        Func<bool>? validateConfirmation = null)
    {
        var finished = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var showing = dialog.ShowAsync(ownerProvider());
            // ShowAsync initializes the dialog and its overlay before its first await. Register
            // afterwards so cancellation between the initial check and showing also closes it.
            using var registration = cancellationToken.Register(() =>
            {
                void CancelDialog()
                {
                    if (finished) return;
                    // Hide has a close animation. Erase both the retained controls and their
                    // presentation immediately so locked account details cannot linger in it.
                    clearSensitiveState();
                    dialog.IsEnabled = false;
                    if (!showing.IsCompleted) dialog.Hide(FAContentDialogResult.None);
                }

                if (Dispatcher.UIThread.CheckAccess()) CancelDialog();
                else Dispatcher.UIThread.Post(CancelDialog);
            });

            var result = await showing;
            cancellationToken.ThrowIfCancellationRequested();
            return result == FAContentDialogResult.Primary && (validateConfirmation?.Invoke() ?? true);
        }
        finally
        {
            finished = true;
            clearSensitiveState();
        }
    }
}
