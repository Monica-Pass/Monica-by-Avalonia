using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void ShowTotpDetails(SecureItem? item)
    {
        if (item is not null)
        {
            SelectedTotpItem = item;
        }
    }

    [RelayCommand]
    private void SelectTotpFilter(string? key)
    {
        SelectedTotpFilterKey = string.IsNullOrWhiteSpace(key) ? TotpFilterAll : key;
    }

    [RelayCommand]
    private void ClearTotpFilters()
    {
        SearchText = "";
        SelectedTotpFilterKey = TotpFilterAll;
        RaiseTotpFilterState();
        StatusMessage = _localization.Get("ClearedTotpFilters");
    }

    [RelayCommand]
    private async Task AddTotpAsync()
    {
        var editor = await _totpEditorDialogService.ShowAsync(null);
        if (editor is null)
        {
            return;
        }

        var item = editor.ApplyTo();
        RefreshTotpDisplay(item);
        await _repository.SaveSecureItemAsync(item);
        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "CREATE",
            DeviceName = Environment.MachineName
        });
        TrackTotpSelection(item);
        TotpItems.Insert(0, item);
        SelectedTotpItem = item;
        RaiseCounts();
        RaiseTotpFilterState(reconcileSelection: false);
        RaiseTotpSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("SavedTotpFormat", item.Title);
    }

    [RelayCommand]
    private async Task ScanTotpQrAsync()
    {
        StatusMessage = _localization.Get("TotpScanQrFallback");
        await AddTotpAsync();
    }

    [RelayCommand]
    private async Task CopyTotpAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        RefreshTotpDisplay(item);
        await _clipboardService.SetSensitiveTextAsync(item.TotpCode);
        StatusMessage = _localization.Format("CopiedTotpFormat", item.Title);
    }

    [RelayCommand]
    private async Task EditTotpAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        var editor = await _totpEditorDialogService.ShowAsync(item);
        if (editor is null)
        {
            return;
        }

        if (item.BoundPasswordId is { } passwordId)
        {
            var password = Passwords.FirstOrDefault(entry => entry.Id == passwordId)
                ?? (await _repository.GetPasswordsAsync()).FirstOrDefault(entry => entry.Id == passwordId);
            if (password is null)
            {
                StatusMessage = _localization.Get("BoundPasswordMissing");
                return;
            }

            password.AuthenticatorKey = editor.ToAuthenticatorKey();
            password.Title = editor.Title.Trim();
            password.Username = editor.AccountName.Trim();
            password.IsFavorite = editor.IsFavorite;
            await _repository.SavePasswordAsync(password);
            await SynchronizeBoundTotpAsync(password);
            RefreshPasswordTotpDisplay(password);
            await LoadTotpItemsAsync();
        }
        else
        {
            editor.ApplyTo(item);
            RefreshTotpDisplay(item);
            await _repository.SaveSecureItemAsync(item);
            SelectedTotpItem = item;
            SelectedTotpDetails = new TotpItemDetailsViewModel(_localization, item);
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = editor.Title.Trim(),
            OperationType = "UPDATE",
            DeviceName = Environment.MachineName
        });
        ClearTotpSelection();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("SavedTotpFormat", editor.Title.Trim());
    }

    [RelayCommand]
    private async Task ToggleTotpFavoriteAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        var next = !item.IsFavorite;
        await SetTotpFavoriteAsync(item, next);
        if (SelectedTotpItem?.Id == item.Id)
        {
            SelectedTotpDetails = new TotpItemDetailsViewModel(_localization, item);
        }

        RaiseTotpFilterState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format(next ? "FavoritedTotpFormat" : "UnfavoritedTotpFormat", item.Title);
    }

    [RelayCommand]
    private async Task DeleteTotpAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!await ConfirmMoveItemToRecycleBinAsync(item.Title))
        {
            return;
        }

        await DeleteTotpItemAsync(item, updateStatus: true);
    }

    [RelayCommand]
    private void ToggleTotpSelection(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        RaiseTotpSelectionState();
    }

    [RelayCommand]
    private void ClearTotpSelection()
    {
        foreach (var item in TotpItems.Where(item => item.IsSelected))
        {
            item.IsSelected = false;
        }

        RaiseTotpSelectionState();
    }

    [RelayCommand]
    private async Task FavoriteSelectedTotpAsync()
    {
        var selected = TotpItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            if (!item.IsFavorite)
            {
                await SetTotpFavoriteAsync(item, true);
            }
        }

        foreach (var item in selected)
        {
            item.IsSelected = false;
        }

        if (SelectedTotpItem is not null && selected.Any(item => item.Id == SelectedTotpItem.Id))
        {
            SelectedTotpDetails = new TotpItemDetailsViewModel(_localization, SelectedTotpItem);
        }

        RaiseTotpFilterState();
        RaiseTotpSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("FavoritedTotpCountFormat", selected.Length);
    }

    [RelayCommand]
    private async Task DeleteSelectedTotpAsync()
    {
        var selected = TotpItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        if (!await ConfirmMoveSelectedItemsToRecycleBinAsync(selected.Length))
        {
            return;
        }

        foreach (var item in selected)
        {
            await DeleteTotpItemAsync(item, updateStatus: false);
        }

        RaiseTotpSelectionState();
        await LoadTimelineAsync();
        StatusMessage = _localization.Format("MovedSelectedTotpToRecycleBinFormat", selected.Length);
    }
}
