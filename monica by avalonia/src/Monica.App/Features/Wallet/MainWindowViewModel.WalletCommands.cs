using CommunityToolkit.Mvvm.Input;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task AddWalletItemAsync()
    {
        var editor = await _walletItemEditorDialogService.ShowAsync(
            null,
            VaultItemType.BankCard);
        if (editor is null)
        {
            return;
        }

        var item = editor.ApplyTo();
        await _repository.SaveSecureItemAsync(item);
        await LogOperationAsync(new OperationLog
        {
            ItemType = "WALLET",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "CREATE",
            DeviceName = Environment.MachineName
        });
        TrackWalletSelection(item);
        WalletItems.Insert(0, item);
        SelectedWalletItem = item;
        WalletNarrowShowsList = false;
        RaiseWalletCountState();
        StatusMessage = _localization.Format("SavedWalletItemFormat", item.Title);
    }

    [RelayCommand]
    private async Task EditWalletItemAsync(SecureItem? item)
    {
        item ??= SelectedWalletItem;
        if (item is null)
        {
            return;
        }

        var editor = await _walletItemEditorDialogService.ShowAsync(item);
        if (editor is null)
        {
            return;
        }

        editor.ApplyTo(item);
        await _repository.SaveSecureItemAsync(item);
        await LogOperationAsync(new OperationLog
        {
            ItemType = "WALLET",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "UPDATE",
            DeviceName = Environment.MachineName
        });
        SelectedWalletItem = item;
        SelectedWalletDetails = new WalletItemDetailsViewModel(_localization, item);
        RaiseWalletCountState();
        StatusMessage = _localization.Format("SavedWalletItemFormat", item.Title);
    }

    [RelayCommand]
    private void ShowWalletDetails(SecureItem? item)
    {
        if (item is not null)
        {
            SelectedWalletItem = item;
            WalletNarrowShowsList = false;
        }
    }

    [RelayCommand]
    private void ShowWalletList() => WalletNarrowShowsList = true;

    [RelayCommand]
    private void ClearWalletSearch()
    {
        if (!HasWalletSearchText)
        {
            return;
        }

        WalletSearchText = "";
        StatusMessage = _localization.Get("ClearedWalletSearch");
    }

    [RelayCommand]
    private async Task CopyWalletFieldAsync(WalletFieldDisplayItem? field)
    {
        if (field is null || string.IsNullOrWhiteSpace(field.Value))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(field.Value);
        StatusMessage = _localization.Format("CopiedWalletFieldFormat", field.Label);
    }

    [RelayCommand]
    private async Task CopySelectedWalletPrimaryFieldAsync()
    {
        var field = SelectedWalletDetails?.Fields.FirstOrDefault(item => item.IsSensitive)
            ?? SelectedWalletDetails?.Fields.FirstOrDefault();
        await CopyWalletFieldAsync(field);
    }

    [RelayCommand]
    private async Task DeleteWalletItemAsync(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (!await ConfirmMoveItemToRecycleBinAsync(item.Title))
        {
            return;
        }

        await DeleteWalletItemCoreAsync(item, updateStatus: true);
    }

    [RelayCommand]
    private void ToggleWalletSelection(SecureItem? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
        RaiseWalletSelectionState();
    }

    [RelayCommand]
    private void ClearWalletSelection()
    {
        foreach (var item in WalletItems.Where(item => item.IsSelected))
        {
            item.IsSelected = false;
        }

        RaiseWalletSelectionState();
    }

    [RelayCommand]
    private async Task DeleteSelectedWalletItemsAsync()
    {
        var selected = WalletItems.Where(item => item.IsSelected).ToArray();
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
            await DeleteWalletItemCoreAsync(item, updateStatus: false);
        }

        RaiseWalletSelectionState();
        StatusMessage = _localization.Format("MovedSelectedWalletItemsToRecycleBinFormat", selected.Length);
    }
}
