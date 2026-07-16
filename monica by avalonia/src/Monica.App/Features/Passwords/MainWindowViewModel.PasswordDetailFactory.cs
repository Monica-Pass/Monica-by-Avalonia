using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task<PasswordDetailViewModel> BuildPasswordDetailViewModelAsync(
        PasswordEntry entry,
        bool includeHistory = true,
        bool allowCustomFieldRepositoryFallback = true)
    {
        var siblings = GetPasswordDetailSiblings(entry);
        var customFields = allowCustomFieldRepositoryFallback
            ? await GetGroupCustomFieldsAsync(entry, siblings)
            : GetCachedGroupCustomFields(entry, siblings);
        var category = entry.CategoryId is null
            ? null
            : Categories.FirstOrDefault(item => item.Id == entry.CategoryId);
        var boundNote = entry.BoundNoteId is null
            ? null
            : NoteItems.FirstOrDefault(item => item.Id == entry.BoundNoteId);
        var attachments = await GetGroupAttachmentsAsync(entry, siblings);
        var history = includeHistory
            ? await GetPasswordHistoryDisplayItemsAsync(entry.Id)
            : [];

        return new PasswordDetailViewModel(
            _localization,
            _clipboardService,
            _cryptoService,
            _totpService,
            entry,
            siblings,
            category,
            boundNote,
            attachments,
            customFields,
            history,
            TryAddPasswordAttachmentAsync,
            SavePasswordAttachmentAsync,
            DeletePasswordAttachmentAsync,
            DeletePasswordHistoryAsync,
            ClearPasswordHistoryAsync);
    }

    private PasswordDetailSourceSnapshot BuildPasswordDetailSourceSnapshot(PasswordEntry entry)
    {
        var siblings = GetPasswordDetailSiblings(entry);
        var category = entry.CategoryId is null
            ? null
            : Categories.FirstOrDefault(item => item.Id == entry.CategoryId);
        var boundNote = entry.BoundNoteId is null
            ? null
            : NoteItems.FirstOrDefault(item => item.Id == entry.BoundNoteId);

        return new PasswordDetailSourceSnapshot(
            entry,
            siblings,
            category,
            boundNote,
            _passwordCustomFields);
    }

    private async Task<PasswordDetailSnapshot> BuildPasswordDetailSnapshotAsync(
        PasswordDetailSourceSnapshot source,
        CancellationToken cancellationToken)
    {
        var entry = source.Entry;
        var customFields = await GetGroupCustomFieldsAsync(
            entry,
            source.Siblings,
            source.PasswordCustomFields,
            cancellationToken);
        var attachments = await GetGroupAttachmentsAsync(entry, source.Siblings, cancellationToken);

        return new PasswordDetailSnapshot(
            entry,
            source.Siblings,
            source.Category,
            source.BoundNote,
            attachments,
            customFields,
            []);
    }

    private PasswordDetailViewModel CreatePasswordDetailViewModel(PasswordDetailSnapshot snapshot) =>
        new(
            _localization,
            _clipboardService,
            _cryptoService,
            _totpService,
            snapshot.Entry,
            snapshot.Siblings,
            snapshot.Category,
            snapshot.BoundNote,
            snapshot.Attachments,
            snapshot.CustomFields,
            snapshot.History,
            TryAddPasswordAttachmentAsync,
            SavePasswordAttachmentAsync,
            DeletePasswordAttachmentAsync,
            DeletePasswordHistoryAsync,
            ClearPasswordHistoryAsync);

    private IReadOnlyList<PasswordEntry> GetPasswordDetailSiblings(PasswordEntry entry)
    {
        IEnumerable<PasswordEntry> candidates = entry.IsDeleted
            ? DeletedPasswords
            : entry.IsArchived
                ? ArchivedPasswords
                : Passwords;
        return GetPasswordDetailSiblings(entry, candidates).ToArray();
    }

    private static IEnumerable<PasswordEntry> GetPasswordDetailSiblings(PasswordEntry entry, IEnumerable<PasswordEntry> candidates)
    {
        var key = BuildSiblingGroupKey(entry);
        return candidates
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    [RelayCommand]
    private async Task OpenQuickAccessPasswordAsync(PasswordQuickAccessItem? item)
    {
        if (item is null)
        {
            return;
        }

        await ShowPasswordDetailsAsync(item.Entry);
    }
}
