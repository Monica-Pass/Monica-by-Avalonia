using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: SelectedNoteTab.IsDirty);
            if (!CanSaveNoteTab(SelectedNoteTab))
            {
                StatusMessage = _localization.Get("NoteRequiresContent");
                return;
            }

            var savedNote = await SaveNoteTabAsync(SelectedNoteTab);
            SelectedNote = savedNote;
            RaiseNoteCountState();
            StatusMessage = _localization.Format("SavedNoteFormat", savedNote.Title);
            return;
        }

        if (string.IsNullOrWhiteSpace(NoteTitle) && string.IsNullOrWhiteSpace(NoteContent))
        {
            StatusMessage = _localization.Get("NoteRequiresContent");
            return;
        }

        var sourceNote = SelectedNote;
        var payload = NoteContentCodec.BuildSavePayload(
            NoteTitle,
            NoteContent,
            NoteTagsText,
            NoteIsMarkdown,
            sourceNote is null ? [] : NoteContentCodec.DecodeImagePaths(sourceNote.ImagePaths));

        var item = sourceNote ?? new SecureItem
        {
            ItemType = VaultItemType.Note,
            CreatedAt = DateTimeOffset.UtcNow
        };

        item.Title = payload.Title;
        item.Notes = payload.NotesCache;
        item.ItemData = payload.ItemData;
        item.ImagePaths = payload.ImagePaths;
        item.CategoryId = SelectedNoteCategory?.Id;
        item.IsFavorite = NoteIsFavorite;
        item.ItemType = VaultItemType.Note;
        item.SyncStatus = item.BitwardenVaultId is null ? SyncStatus.None : SyncStatus.Pending;

        await _repository.SaveSecureItemAsync(item);
        await LogOperationAsync(new OperationLog
        {
            ItemType = "NOTE",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = sourceNote is null ? "CREATE" : "UPDATE",
            DeviceName = Environment.MachineName
        });

        if (NoteItems.All(note => note.Id != item.Id))
        {
            NoteItems.Insert(0, item);
        }

        if (SelectedNoteTab is not null)
        {
            SelectedNoteTab.Source = item;
            SelectedNoteTab.Title = item.Title;
            CaptureNoteEditorState(SelectedNoteTab, markDirty: false);
            SelectedNoteTab.IsDirty = false;
        }

        SelectedNote = item;
        RaiseNoteCountState();
        StatusMessage = _localization.Format("SavedNoteFormat", item.Title);
    }

    [RelayCommand]
    private async Task SaveAllNoteTabsAsync()
    {
        if (SelectedNoteTab is not null)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: SelectedNoteTab.IsDirty);
        }

        var dirtyTabs = OpenNoteTabs.Where(tab => tab.IsDirty).ToArray();
        if (dirtyTabs.Length == 0)
        {
            StatusMessage = _localization.Get("NoNotesToSave");
            return;
        }

        var savedCount = 0;
        var skippedCount = 0;
        foreach (var tab in dirtyTabs)
        {
            if (!CanSaveNoteTab(tab))
            {
                skippedCount++;
                continue;
            }

            await SaveNoteTabAsync(tab);
            savedCount++;
        }

        if (SelectedNoteTab?.Source is not null)
        {
            SelectedNote = SelectedNoteTab.Source;
        }

        if (savedCount > 0)
        {
            RaiseNoteCountState();
        }

        StatusMessage = skippedCount == 0
            ? _localization.Format("SavedNotesFormat", savedCount)
            : _localization.Format("SavedNotesWithSkippedFormat", savedCount, skippedCount);
    }

}
