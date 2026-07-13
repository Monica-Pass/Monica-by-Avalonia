using System.ComponentModel;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly IWalletItemEditorDialogService _walletItemEditorDialogService;

    private void RaiseWalletSelectionState()
    {
        OnPropertyChanged(nameof(SelectedWalletCount));
        OnPropertyChanged(nameof(SelectedWalletCountText));
        OnPropertyChanged(nameof(HasSelectedWalletItems));
    }

    private void TrackWalletSelection(SecureItem item)
    {
        item.PropertyChanged -= WalletItemPropertyChanged;
        item.PropertyChanged += WalletItemPropertyChanged;
    }

    private void WalletItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecureItem.IsSelected))
        {
            RaiseWalletSelectionState();
        }
    }

    private async Task DeleteWalletItemCoreAsync(SecureItem item, bool updateStatus)
    {
        if (item.Id > 0)
        {
            await _repository.SoftDeleteSecureItemAsync(item.Id);
        }

        WalletItems.Remove(item);
        item.IsSelected = false;
        if (SelectedWalletItem?.Id == item.Id)
        {
            SelectedWalletItem = WalletItems.FirstOrDefault();
        }

        await LogOperationAsync(new OperationLog
        {
            ItemType = "WALLET",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "DELETE",
            DeviceName = Environment.MachineName
        });
        RaiseCounts();
        RaiseWalletSelectionState();
        if (updateStatus)
        {
            StatusMessage = _localization.Format("MovedToRecycleBinFormat", item.Title);
        }
    }
}
