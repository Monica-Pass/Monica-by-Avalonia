using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task<SecureItem> SaveNoteTabAsync(NoteEditorTab tab)
    {
        var sourceNote = tab.Source;
        var payload = NoteContentCodec.BuildSavePayload(
            tab.DraftTitle,
            tab.DraftContent,
            tab.DraftTagsText,
            tab.DraftIsMarkdown,
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
        item.IsFavorite = tab.DraftIsFavorite;
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

        tab.Source = item;
        tab.Title = item.Title;
        tab.DraftTitle = item.Title;
        tab.DraftIsFavorite = item.IsFavorite;
        tab.IsDirty = false;
        return item;
    }

    [RelayCommand]
    private async Task ToggleNoteFavoriteAsync(SecureItem? item)
    {
        item ??= SelectedNote;
        if (item is null)
        {
            NoteIsFavorite = !NoteIsFavorite;
            MarkSelectedNoteTabDirty();
            return;
        }

        var next = !item.IsFavorite;
        item.IsFavorite = next;
        if (SelectedNote?.Id == item.Id)
        {
            NoteIsFavorite = next;
        }

        await _repository.SaveSecureItemAsync(item);
        if (SelectedNoteTab?.Source?.Id == item.Id)
        {
            CaptureNoteEditorState(SelectedNoteTab, markDirty: false);
        }

        RaiseNoteTreeState();
        StatusMessage = _localization.Format("SavedNoteFormat", item.Title);
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(SecureItem? item)
    {
        item ??= SelectedNote;
        if (item is null)
        {
            return;
        }

        if (!await ConfirmMoveItemToRecycleBinAsync(item.Title))
        {
            return;
        }

        await _repository.SoftDeleteSecureItemAsync(item.Id);
        NoteItems.Remove(item);
        var deletedTabs = OpenNoteTabs
            .Where(tab => tab.Source?.Id == item.Id)
            .ToArray();
        foreach (var tab in deletedTabs)
        {
            CloseNoteTab(tab);
        }

        if (deletedTabs.Length == 0 && SelectedNote?.Id == item.Id)
        {
            SelectedNote = null;
        }

        RaiseNoteCountState();
        StatusMessage = _localization.Format("MovedToRecycleBinFormat", item.Title);
    }
}
