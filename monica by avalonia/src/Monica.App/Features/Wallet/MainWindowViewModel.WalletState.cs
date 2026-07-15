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

    private void RaiseWalletFilterState(bool reconcileSelection = true)
    {
        _filteredWalletItemsDirty = true;
        OnPropertyChanged(nameof(FilteredWalletItems));
        OnPropertyChanged(nameof(HasFilteredWalletItems));
        OnPropertyChanged(nameof(HasWalletSearchText));
        OnPropertyChanged(nameof(WalletFilteredStatusText));
        OnPropertyChanged(nameof(WalletEmptyStateText));
        if (reconcileSelection)
        {
            ReconcileSelectedWalletItem();
        }
    }

    private void ReconcileSelectedWalletItem()
    {
        var visibleItems = FilteredWalletItems;
        SelectedWalletItem = visibleItems.FirstOrDefault(item => item.Id == SelectedWalletItem?.Id)
            ?? visibleItems.FirstOrDefault();
    }

    private static bool MatchesWalletSearch(SecureItem item, string query)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        return item.ItemType == VaultItemType.BankCard
            ? MatchesBankCardSearch(item, term)
            : MatchesDocumentSearch(item, term);
    }

    private static bool MatchesBankCardSearch(SecureItem item, string term)
    {
        var data = WalletItemDataCodec.DecodeBankCard(item);
        return ContainsAny(
            term,
            item.Title,
            item.Notes,
            data.CardNumber,
            data.CardholderName,
            data.BankName,
            data.Brand,
            data.BillingAddress);
    }

    private static bool MatchesDocumentSearch(SecureItem item, string term)
    {
        var data = WalletItemDataCodec.DecodeDocument(item);
        return ContainsAny(
            term,
            item.Title,
            item.Notes,
            data.DocumentNumber,
            data.FullName,
            data.IssuedBy,
            data.Nationality,
            data.AdditionalInfo);
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
