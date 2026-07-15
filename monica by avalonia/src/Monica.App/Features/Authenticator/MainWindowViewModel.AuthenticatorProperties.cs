using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string TotpFilterAll = "all";
    private const string TotpFilterFavorites = "favorites";
    private const string TotpFilterExpiringSoon = "expiring-soon";
    private const string TotpFilterUnbound = "unbound";
    private const string TotpFilterIssuerPrefix = "issuer:";
    private bool _suppressSelectedTotpRefresh;
    private IReadOnlyList<SecureItem> _filteredTotpItems = [];
    private bool _filteredTotpItemsDirty = true;

    internal int FilteredTotpProjectionBuildCount { get; private set; }

    public ObservableCollection<SecureItem> TotpItems { get; } = new ObservableRangeCollection<SecureItem>();
    public ObservableCollection<TotpFilterChoice> TotpFilterChoices { get; } = [];

    [ObservableProperty]
    private SecureItem? _selectedTotpItem;

    [ObservableProperty]
    private TotpItemDetailsViewModel? _selectedTotpDetails;

    [ObservableProperty]
    private string _selectedTotpFilterKey = TotpFilterAll;

    [ObservableProperty]
    private string _totpSearchText = "";

    [ObservableProperty]
    private bool _totpNarrowShowsList = true;

    public string TotpCountText => _localization.Format("TotpCountFormat", TotpItems.Count);
    public int TotpExpiringSoonCount => TotpItems.Count(IsTotpExpiringSoon);
    public string TotpConsoleStatusText => _localization.Format("TotpConsoleStatusFormat", TotpItems.Count, TotpExpiringSoonCount);
    public string TotpFilteredStatusText => _localization.Format("TotpFilteredStatusFormat", FilteredTotpItems.Count, TotpItems.Count);
    public string TotpScanQrText => _localization.Get("TotpScanQr");
    public string TotpManualAddText => _localization.Get("TotpManualAdd");
    public string TotpMoreActionsText => _localization.Get("MoreActions");
    public string TotpFilterTitleText => _localization.Get("TotpFilterTitle");
    public string TotpIssuerGroupsText => _localization.Get("TotpIssuerGroups");
    public string TotpNoFilteredResultsText => _localization.Get("TotpNoFilteredResults");
    public string TotpEmptyStateText => HasTotpFilterOrSearch && HasTotpItems
        ? _localization.Get("TotpNoFilteredResults")
        : _localization.Get("TotpEmptyHint");
    public string ClearTotpFiltersText => _localization.Get("ClearTotpFilters");
    public string TotpShowHiddenText => _localization.Get("ShowHidden");
    public string TotpHelpText => _localization.Get("Help");
    public int SelectedTotpCount => TotpItems.Count(item => item.IsSelected);
    public string SelectedTotpCountText => _localization.Format("SelectedTotpCountFormat", SelectedTotpCount);
    public bool HasSelectedTotpItems => SelectedTotpCount > 0;
    public bool HasSelectedTotpItem => SelectedTotpItem is not null;
    public bool HasTotpItems => TotpItems.Count > 0;
    public IReadOnlyList<SecureItem> FilteredTotpItems => BuildFilteredTotpItems();
    public bool HasFilteredTotpItems => FilteredTotpItems.Count > 0;
    public bool HasTotpFilterOrSearch =>
        !string.Equals(SelectedTotpFilterKey, TotpFilterAll, StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrWhiteSpace(TotpSearchText);
    public bool HasTotpSearchText => !string.IsNullOrWhiteSpace(TotpSearchText);

    partial void OnSelectedTotpItemChanged(SecureItem? value)
    {
        if (value is not null && !_suppressSelectedTotpRefresh)
        {
            RefreshTotpDisplay(value);
        }

        SelectedTotpDetails = value is null || _isUnlockedShellHibernated
            ? null
            : new TotpItemDetailsViewModel(_localization, value);
        OnPropertyChanged(nameof(HasSelectedTotpItem));
    }

    partial void OnSelectedTotpFilterKeyChanged(string value) => RaiseTotpFilterState();

    partial void OnTotpSearchTextChanged(string value) => RaiseTotpFilterState();

    private IReadOnlyList<SecureItem> BuildFilteredTotpItems()
    {
        if (_filteredTotpItemsDirty)
        {
            _filteredTotpItems = TotpItems.Where(MatchesTotpFilters).ToArray();
            _filteredTotpItemsDirty = false;
            FilteredTotpProjectionBuildCount++;
        }

        return _filteredTotpItems;
    }
}
