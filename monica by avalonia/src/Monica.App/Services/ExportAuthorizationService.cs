using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Monica.Core.Services;
using Monica.Data;

namespace Monica.App.Services;

public interface IExportAuthorizationService
{
    Task<bool> AuthorizeAsync(bool requireMasterPassword, CancellationToken cancellationToken = default);
}

public sealed class MasterPasswordExportAuthorizationService(
    Func<Window> ownerProvider,
    IVaultCredentialStore credentialStore,
    ICryptoService cryptoService,
    ILocalizationService localization) : IExportAuthorizationService
{
    public async Task<bool> AuthorizeAsync(
        bool requireMasterPassword,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!cryptoService.IsUnlocked)
        {
            return false;
        }

        if (!requireMasterPassword)
        {
            return true;
        }

        var passwordInput = new TextBox
        {
            Width = 420,
            PasswordChar = '●',
            PlaceholderText = localization.Get("MasterPassword")
        };
        var dialog = CreateDialog(passwordInput);
        var result = await dialog.ShowAsync(ownerProvider());
        if (result != FAContentDialogResult.Primary || string.IsNullOrEmpty(passwordInput.Text))
        {
            return false;
        }

        var credential = await credentialStore.GetAsync(cancellationToken);
        return credential is not null && cryptoService.VerifyMasterPassword(passwordInput.Text, credential);
    }

    private FAContentDialog CreateDialog(TextBox passwordInput) => new()
    {
        Title = localization.Get("AuthorizeExportTitle"),
        Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = localization.Get("AuthorizeExportDescription"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 420
                },
                passwordInput
            }
        },
        PrimaryButtonText = localization.Get("AuthorizeExportAction"),
        CloseButtonText = localization.Cancel,
        DefaultButton = FAContentDialogButton.Close
    };
}

internal sealed class SessionExportAuthorizationService(ICryptoService cryptoService) : IExportAuthorizationService
{
    public Task<bool> AuthorizeAsync(bool requireMasterPassword, CancellationToken cancellationToken = default) =>
        Task.FromResult(cryptoService.IsUnlocked);
}
