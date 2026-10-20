using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Monica.App.Services;

public interface IPasswordPromptDialogService
{
    Task<string?> PromptAsync(
        string title,
        string description,
        string action,
        string? error = null,
        CancellationToken cancellationToken = default);
}

public sealed class PasswordPromptDialogService(
    Func<Window> ownerProvider,
    ILocalizationService localization) : IPasswordPromptDialogService
{
    public async Task<string?> PromptAsync(
        string title,
        string description,
        string action,
        string? error = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var passwordInput = new TextBox
        {
            Width = 420,
            PasswordChar = '●'
        };
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = description,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                }
            }
        };
        if (!string.IsNullOrEmpty(error))
        {
            content.Children.Add(new TextBlock
            {
                Text = error,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420,
                Foreground = Brushes.Firebrick
            });
        }

        content.Children.Add(passwordInput);
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = action,
            CloseButtonText = localization.Cancel,
            DefaultButton = FAContentDialogButton.Close
        };

        try
        {
            var result = await dialog.ShowAsync(ownerProvider());
            if (result != FAContentDialogResult.Primary)
                return null;
            var password = passwordInput.Text;
            return string.IsNullOrEmpty(password) ? null : password;
        }
        finally
        {
            passwordInput.Text = string.Empty;
        }
    }
}
