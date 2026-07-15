using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<SecureItem> _filteredWalletItems = [];
    private bool _filteredWalletItemsDirty = true;

    internal int FilteredWalletProjectionBuildCount { get; private set; }

    public ObservableCollection<SecureItem> WalletItems { get; } = new ObservableRangeCollection<SecureItem>();

    [ObservableProperty]
    private SecureItem? _selectedWalletItem;

    [ObservableProperty]
    private WalletItemDetailsViewModel? _selectedWalletDetails;

    [ObservableProperty]
    private string _walletSearchText = "";

    [ObservableProperty]
    private bool _walletNarrowShowsList = true;

    public string WalletCountText => _localization.Format("WalletCountFormat", WalletItems.Count);
    public int SelectedWalletCount => WalletItems.Count(item => item.IsSelected);
    public string SelectedWalletCountText => _localization.Format("SelectedWalletCountFormat", SelectedWalletCount);
    public bool HasSelectedWalletItems => SelectedWalletCount > 0;
    public bool HasSelectedWalletItem => SelectedWalletItem is not null;
    public bool HasWalletItems => WalletItems.Count > 0;
    public bool HasWalletSearchText => !string.IsNullOrWhiteSpace(WalletSearchText);
    public IReadOnlyList<SecureItem> FilteredWalletItems => BuildFilteredWalletItems();
    public bool HasFilteredWalletItems => FilteredWalletItems.Count > 0;
    public string WalletFilteredStatusText => _localization.Format(
        "WalletFilteredStatusFormat",
        FilteredWalletItems.Count,
        WalletItems.Count);
    public string WalletEmptyStateText => HasWalletSearchText && HasWalletItems
        ? _localization.Get("WalletNoResults")
        : _localization.Get("WalletEmptyHint");

    partial void OnSelectedWalletItemChanged(SecureItem? value)
    {
        SelectedWalletDetails = value is null ? null : new WalletItemDetailsViewModel(_localization, value);
        OnPropertyChanged(nameof(HasSelectedWalletItem));
    }

    partial void OnWalletSearchTextChanged(string value) => RaiseWalletFilterState();

    private IReadOnlyList<SecureItem> BuildFilteredWalletItems()
    {
        if (_filteredWalletItemsDirty)
        {
            _filteredWalletItems = WalletItems
                .Where(item => MatchesWalletSearch(item, WalletSearchText))
                .ToArray();
            _filteredWalletItemsDirty = false;
            FilteredWalletProjectionBuildCount++;
        }

        return _filteredWalletItems;
    }
}
