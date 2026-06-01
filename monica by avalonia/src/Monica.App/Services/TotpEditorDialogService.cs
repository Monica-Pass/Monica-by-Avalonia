using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.App.Services;

public interface ITotpEditorDialogService
{
    Task<TotpEditorViewModel?> ShowAsync(SecureItem? item, CancellationToken cancellationToken = default);
}

public sealed class TotpEditorDialogService(
    Func<Window> ownerProvider,
    ILocalizationService localization) : ITotpEditorDialogService
{
    public async Task<TotpEditorViewModel?> ShowAsync(SecureItem? item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var editor = new TotpEditorViewModel(localization, item);
        var dialog = new FAContentDialog
        {
            Title = editor.DialogTitle,
            Content = new TotpEditorDialog { DataContext = editor },
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
