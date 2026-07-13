using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<SecureItem> WalletItems { get; } = new ObservableRangeCollection<SecureItem>();

    [ObservableProperty]
    private SecureItem? _selectedWalletItem;

    [ObservableProperty]
    private WalletItemDetailsViewModel? _selectedWalletDetails;

    public string WalletCountText => _localization.Format("WalletCountFormat", WalletItems.Count);
    public int SelectedWalletCount => WalletItems.Count(item => item.IsSelected);
    public string SelectedWalletCountText => _localization.Format("SelectedWalletCountFormat", SelectedWalletCount);
    public bool HasSelectedWalletItems => SelectedWalletCount > 0;
    public bool HasSelectedWalletItem => SelectedWalletItem is not null;
    public bool HasWalletItems => WalletItems.Count > 0;

    partial void OnSelectedWalletItemChanged(SecureItem? value)
    {
        SelectedWalletDetails = value is null ? null : new WalletItemDetailsViewModel(_localization, value);
        OnPropertyChanged(nameof(HasSelectedWalletItem));
    }
}
