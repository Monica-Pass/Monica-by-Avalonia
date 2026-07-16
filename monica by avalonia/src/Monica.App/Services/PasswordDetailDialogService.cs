using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.App.Services;

public interface IPasswordDetailDialogService
{
    Task ShowAsync(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        Category? category,
        SecureItem? boundNote,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyList<CustomField> customFields,
        IReadOnlyList<PasswordHistoryDisplayItem> passwordHistory,
        Func<PasswordEntry, Task>? addAttachment,
        Func<Attachment, Task<PasswordAttachmentSaveResult>>? saveAttachment,
        Func<Attachment, Task<bool>>? deleteAttachment,
        Func<PasswordHistoryEntry, Task<bool>>? deletePasswordHistory,
        Func<long, Task<bool>>? clearPasswordHistory,
        CancellationToken cancellationToken = default);
}

public sealed class PasswordDetailDialogService(
    Func<Window> ownerProvider,
    ILocalizationService localization,
    IClipboardService clipboardService,
    ICryptoService cryptoService,
    ITotpService totpService) : IPasswordDetailDialogService
{
    public async Task ShowAsync(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        Category? category,
        SecureItem? boundNote,
        IReadOnlyList<Attachment> attachments,
        IReadOnlyList<CustomField> customFields,
        IReadOnlyList<PasswordHistoryDisplayItem> passwordHistory,
        Func<PasswordEntry, Task>? addAttachment,
        Func<Attachment, Task<PasswordAttachmentSaveResult>>? saveAttachment,
        Func<Attachment, Task<bool>>? deleteAttachment,
        Func<PasswordHistoryEntry, Task<bool>>? deletePasswordHistory,
        Func<long, Task<bool>>? clearPasswordHistory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var details = new PasswordDetailViewModel(
            localization,
            clipboardService,
            cryptoService,
            totpService,
            entry,
            siblings,
            category,
            boundNote,
            attachments,
            customFields,
            passwordHistory,
            addAttachment,
            saveAttachment,
            deleteAttachment,
            deletePasswordHistory,
            clearPasswordHistory);

        PasswordDetailDialog? detailView = null;
        try
        {
            detailView = new PasswordDetailDialog { DataContext = details };
            var dialog = new FAContentDialog
            {
                Title = details.DialogTitle,
                Content = detailView,
                CloseButtonText = localization.Get("Close"),
                DefaultButton = FAContentDialogButton.Close
            };

            await dialog.ShowAsync(ownerProvider());
        }
        finally
        {
            if (detailView is not null)
            {
                detailView.DataContext = null;
            }

            details.Dispose();
        }
    }
}
