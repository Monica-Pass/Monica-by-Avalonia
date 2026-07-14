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
        var attachments = GetGroupAttachments(entry, siblings);
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
            AddPasswordAttachmentAsync,
            DeletePasswordAttachmentAsync,
            DeletePasswordHistoryAsync,
            ClearPasswordHistoryAsync);
    }

    private PasswordDetailSourceSnapshot BuildPasswordDetailSourceSnapshot(PasswordEntry entry)
    {
        var candidates = entry.IsDeleted
            ? DeletedPasswords.ToArray()
            : entry.IsArchived
                ? ArchivedPasswords.ToArray()
                : Passwords.ToArray();

        return new PasswordDetailSourceSnapshot(
            entry,
            candidates,
            Categories.ToArray(),
            NoteItems.ToArray(),
            _passwordAttachments,
            _passwordCustomFields);
    }

    private PasswordDetailSnapshot BuildCachedPasswordDetailSnapshot(PasswordDetailSourceSnapshot source)
    {
        var entry = source.Entry;
        var siblings = GetPasswordDetailSiblings(entry, source.SiblingCandidates).ToArray();
        var category = entry.CategoryId is null
            ? null
            : source.Categories.FirstOrDefault(item => item.Id == entry.CategoryId);
        var boundNote = entry.BoundNoteId is null
            ? null
            : source.NoteItems.FirstOrDefault(item => item.Id == entry.BoundNoteId);

        return new PasswordDetailSnapshot(
            entry,
            siblings,
            category,
            boundNote,
            GetGroupAttachments(entry, siblings, source.PasswordAttachments),
            GetCachedGroupCustomFields(entry, siblings, source.PasswordCustomFields),
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
            AddPasswordAttachmentAsync,
            DeletePasswordAttachmentAsync,
            DeletePasswordHistoryAsync,
            ClearPasswordHistoryAsync);

    private IReadOnlyList<PasswordEntry> GetPasswordDetailSiblings(PasswordEntry entry)
    {
        var candidates = entry.IsDeleted
            ? DeletedPasswords.ToArray()
            : entry.IsArchived
                ? ArchivedPasswords.ToArray()
                : Passwords.ToArray();
        return GetPasswordDetailSiblings(entry, candidates).ToArray();
    }

    private static IEnumerable<PasswordEntry> GetPasswordDetailSiblings(PasswordEntry entry, IReadOnlyList<PasswordEntry> candidates)
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
