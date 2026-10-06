using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Data.Passkeys;

namespace Monica.App.ViewModels;

public sealed partial class PasskeyWorkspaceViewModel : ObservableObject
{
    private readonly IPasskeyStore? _store;
    private readonly IConfirmationDialogService _confirmation;
    private readonly Func<bool> _isUnlocked;
    private readonly Func<CancellationToken> _sessionToken;
    private readonly List<PasskeyListItem> _allItems = [];
    private CancellationTokenSource? _operationCancellation;
    private int _lifetimeVersion;
    private bool _isLoaded;
    private string _errorKey = "";

    public PasskeyWorkspaceViewModel(IPasskeyStore? store, IConfirmationDialogService confirmation,
        ILocalizationService localization, Func<bool> isUnlocked, Func<CancellationToken> sessionToken)
    {
        _store = store;
        _confirmation = confirmation;
        L = localization;
        _isUnlocked = isUnlocked;
        _sessionToken = sessionToken;
    }

    public ILocalizationService L { get; }
    public ObservableCollection<PasskeyListItem> Items { get; } = [];
    public bool HasSelection => SelectedItem is not null;
    public bool HasError => ErrorText.Length > 0;
    public bool IsEmpty => !IsBusy && !HasError && _isLoaded && Items.Count == 0;
    public string CountText => L.Format("PasskeyCountFormat", Items.Count, _allItems.Count);
    public string EmptyText => L.Get(string.IsNullOrWhiteSpace(SearchText) ? "PasskeyEmpty" : "PasskeyNoMatches");
    public string DeleteText => L.Get(SelectedItem?.IsPlatformManaged == true ? "PasskeyRemoveRecord" : "Delete");

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection)), NotifyPropertyChangedFor(nameof(DeleteText))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private PasskeyListItem? _selectedItem;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(EmptyText))]
    private string _searchText = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand)), NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError)), NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _errorText = "";

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnErrorTextChanged(string value) { if (value.Length == 0) _errorKey = ""; }
    private bool CanRefresh() => _store is not null && _isUnlocked() && !IsBusy;
    private bool CanDelete() => CanRefresh() && SelectedItem is not null;

    public Task EnsureLoadedAsync() => !_isLoaded && CanRefresh() ? RefreshAsync() : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (!CanRefresh()) return;
        var lifetime = _lifetimeVersion;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_sessionToken());
        _operationCancellation = operation;
        IsBusy = true;
        ErrorText = "";
        try
        {
            var entries = await _store!.ListAllAsync(operation.Token);
            if (!IsCurrent(lifetime, operation.Token)) return;
            var selectedId = SelectedItem?.Id;
            ClearItems();
            _allItems.AddRange(entries.Select(entry => new PasskeyListItem(entry, L)));
            _isLoaded = true;
            ApplyFilter();
            SelectedItem = Items.FirstOrDefault(item => item.Id == selectedId);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (IsCurrent(lifetime, operation.Token)) SetError("PasskeyLoadFailed");
        }
        finally
        {
            if (lifetime == _lifetimeVersion)
            {
                _operationCancellation = null;
                IsBusy = false;
                RaiseListState();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    public async Task DeleteSelectedAsync()
    {
        if (!CanDelete() || SelectedItem is not { } item) return;
        var lifetime = _lifetimeVersion;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_sessionToken());
        _operationCancellation = operation;
        IsBusy = true;
        ErrorText = "";
        try
        {
            var messageKey = item.IsPlatformManaged ? "PasskeyRemovePlatformConfirm" : "PasskeyDeleteConfirm";
            var approved = await _confirmation.ConfirmAsync(L.Get("Passkeys"), L.Format(messageKey, item.RpId, item.UserName),
                item.IsPlatformManaged ? L.Get("PasskeyRemoveRecord") : L.Delete, L.Cancel, operation.Token);
            if (!approved || !IsCurrent(lifetime, operation.Token)) return;
            await _store!.DeleteAsync(item.Id, operation.Token);
            if (!IsCurrent(lifetime, operation.Token)) return;
            _allItems.Remove(item);
            if (ReferenceEquals(SelectedItem, item)) SelectedItem = null;
            item.ClearSensitiveState();
            ApplyFilter();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (IsCurrent(lifetime, operation.Token)) SetError("PasskeyDeleteFailed");
        }
        finally
        {
            if (lifetime == _lifetimeVersion)
            {
                _operationCancellation = null;
                IsBusy = false;
                RaiseListState();
            }
        }
    }

    [RelayCommand] private void ClearSearch() => SearchText = "";
    [RelayCommand] private void BackToList() => SelectedItem = null;

    public void ClearSensitiveState()
    {
        _lifetimeVersion++;
        _operationCancellation?.Cancel();
        _operationCancellation = null;
        ClearItems();
        _isLoaded = false;
        SearchText = ErrorText = "";
        IsBusy = false;
        NotifyAccessChanged();
        RaiseListState();
    }

    public void NotifyAccessChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    public void RefreshLocalization()
    {
        foreach (var item in _allItems) item.RefreshLocalization(L);
        if (HasError) ErrorText = L.Get(_errorKey);
        RaiseListState();
        OnPropertyChanged(nameof(DeleteText));
    }

    private bool IsCurrent(int lifetime, CancellationToken token) => lifetime == _lifetimeVersion && _isUnlocked() && !token.IsCancellationRequested;
    private void SetError(string key)
    {
        _errorKey = key;
        ErrorText = L.Get(key);
    }
    private void ClearItems()
    {
        SelectedItem = null;
        foreach (var item in _allItems) item.ClearSensitiveState();
        _allItems.Clear();
        Items.Clear();
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var selected = SelectedItem;
        Items.Clear();
        foreach (var item in _allItems.Where(item => item.Matches(query))) Items.Add(item);
        SelectedItem = selected is not null && Items.Contains(selected) ? selected : null;
        RaiseListState();
    }

    private void RaiseListState()
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }
}
