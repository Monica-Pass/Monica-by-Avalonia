using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.App.Services;

public interface IWalletItemEditorDialogService
{
    Task<WalletItemEditorViewModel?> ShowAsync(SecureItem? item, VaultItemType? newItemType = null, CancellationToken cancellationToken = default);
}

public sealed class WalletItemEditorDialogService(
    Func<Window> ownerProvider,
    ILocalizationService localization) : IWalletItemEditorDialogService
{
    public async Task<WalletItemEditorViewModel?> ShowAsync(SecureItem? item, VaultItemType? newItemType = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var editor = new WalletItemEditorViewModel(localization, item, newItemType);
        var dialog = new FAContentDialog
        {
            Title = editor.DialogTitle,
            Content = new WalletItemEditorDialog { DataContext = editor },
            PrimaryButtonText = localization.Save,
            CloseButtonText = localization.Cancel,
            DefaultButton = FAContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!editor.Validate())
            {
                args.Cancel = true;
            }
        };

        var result = await dialog.ShowAsync(ownerProvider());
        return result == FAContentDialogResult.Primary ? editor : null;
    }
}
