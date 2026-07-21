using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Monica.App.Features;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.App.Services;

public interface ITotpEditorDialogService
{
    Task<TotpEditorViewModel?> ShowAsync(SecureItem? item, CancellationToken cancellationToken = default);
}

public sealed class TotpEditorDialogService(
    Func<Window> ownerProvider,
    ILocalizationService localization,
    IMonicaRepository repository) : ITotpEditorDialogService
{
    public async Task<TotpEditorViewModel?> ShowAsync(SecureItem? item, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var categories = await repository.GetCategoriesAsync(cancellationToken);
        var editor = new TotpEditorViewModel(localization, item, categories);
        var editorView = VaultEditorDialogWarmup.TakeTotpEditorView();
        editorView.DataContext = editor;
        var dialog = new FAContentDialog
        {
            Title = editor.DialogTitle,
            Content = editorView,
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

        try
        {
            var result = await dialog.ShowAsync(ownerProvider());
            return result == FAContentDialogResult.Primary ? editor : null;
        }
        finally
        {
            editorView.DataContext = null;
            Dispatcher.UIThread.Post(
                VaultEditorDialogWarmup.EnsureTotpWarmed,
                DispatcherPriority.Background);
        }
    }
}
