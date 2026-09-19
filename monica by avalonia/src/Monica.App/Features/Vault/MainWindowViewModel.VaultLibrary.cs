using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.Core.Categories;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int VaultSearchCoalesceMs = 60;

    private readonly HashSet<string> _collapsedVaultFolderKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<INotifyPropertyChanged, VaultTreeEntryRow> _observedVaultEntries = new(
        ReferenceEqualityComparer.Instance);
    private DispatcherTimer? _vaultSearchDebounce;
    private bool _isVaultTreeAttached;
    private int _vaultTreeRefreshDepth;
    private bool _isRestoringVaultSelection;

    // Swapped wholesale rather than refilled: refilling a bound collection costs one collection-changed
    // event per row, and the tree has to re-evaluate the whole extent for each of them.
    public ObservableCollection<IVaultTreeRow> VaultTreeRows { get; private set; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedVaultSurface))]
    [NotifyPropertyChangedFor(nameof(HasVaultSelection))]
    [NotifyPropertyChangedFor(nameof(CanManageSelectedVaultFolder))]
    [NotifyPropertyChangedFor(nameof(SelectedVaultFolderPath))]
    private IVaultTreeRow? _selectedVaultRow;

    // The rail tag the user picked IS the preset; a second observable would let the navigation and
    // the tree disagree about which slice of the library is on screen.
    public VaultEntryGroup VaultGroup => VaultPresets.GroupOf(SelectedSection);

    [ObservableProperty]
    private bool _vaultFavoritesOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVaultSearchText))]
    [NotifyPropertyChangedFor(nameof(VaultFilteredStatusText))]
    private string _vaultSearchText = "";

    public bool HasVaultSearchText => !string.IsNullOrWhiteSpace(VaultSearchText);

    public string VaultSearchHelpText => _localization.Get("VaultSearchHelp");

    public bool HasVaultFilters => CurrentVaultFilter().NarrowsWithinGroup;

    // A screen reader needs the same "what did my filters leave behind" announcement the four vault
    // pages made, counted over the filter rather than the tree: collapsing a folder hides rows on
    // screen without narrowing what the filter matched.
    public string VaultFilteredStatusText
    {
        get
        {
            var filter = CurrentVaultFilter();
            return filter.NarrowsWithinGroup
                ? _localization.Format(
                    "VaultFilteredStatusFormat",
                    VaultMatchedEntryCount(filter),
                    VaultGroupEntryCount(filter.Group))
                : "";
        }
    }

    private int VaultMatchedEntryCount(VaultTreeFilter filter) =>
        Passwords.Count(entry => filter.MatchesPassword(entry)) +
        VaultSecureItems().Count(item => filter.MatchesSecureItem(item));

    private int VaultGroupEntryCount(VaultEntryGroup group) =>
        VaultMatchedEntryCount(new VaultTreeFilter(group));

    // A type filter already says what the user means, so its header button creates that type in one
    // click; with everything in view the page asks instead of silently picking a kind.
    public bool HasVaultCreatePreset => VaultGroup != VaultEntryGroup.All;

    public string VaultCreateLabel => VaultGroup switch
    {
        VaultEntryGroup.Passwords => _localization.Get("AddPassword"),
        VaultEntryGroup.Notes => _localization.Get("NewSecureNote"),
        VaultEntryGroup.Totp => _localization.Get("AddAuthenticator"),
        VaultEntryGroup.Cards => _localization.Get("AddWalletItem"),
        _ => _localization.Get("LibraryCreate")
    };

    public ICommand? VaultCreateCommand => VaultGroup switch
    {
        VaultEntryGroup.Passwords => AddPasswordCommand,
        VaultEntryGroup.Notes => AddNoteCommand,
        VaultEntryGroup.Totp => AddTotpCommand,
        VaultEntryGroup.Cards => AddWalletItemCommand,
        _ => null
    };

    /// Which existing editor the right-hand slot has to host for the current selection. The library
    /// page owns no editing surface of its own; it routes a row to whichever page already edits it.
    public VaultSurface SelectedVaultSurface => SelectedVaultRow switch
    {
        VaultTreeEntryRow { Password: not null } => VaultSurface.Password,
        VaultTreeEntryRow { Kind: VaultEntryKind.Note } => VaultSurface.Note,
        VaultTreeEntryRow { Kind: VaultEntryKind.Totp } => VaultSurface.Totp,
        VaultTreeEntryRow { Item: not null } => VaultSurface.Card,
        _ => VaultSurface.None
    };

    public bool CanManageSelectedVaultFolder => SelectedVaultRow is VaultTreeFolderRow { CategoryId: > 0 };

    public string? SelectedVaultFolderPath => SelectedVaultRow is VaultTreeFolderRow { Path.Length: > 0 } folder
        ? folder.Path
        : null;

    public bool HasVaultSelection => SelectedVaultSurface != VaultSurface.None;

    public bool HasVaultRows => VaultTreeRows.Count > 0;

    // Website and username only exist on a credential, so the quick filters and those two sort keys
    // are offered by the password preset alone.
    public bool IsVaultPasswordsPreset => VaultGroup == VaultEntryGroup.Passwords;

    public bool IsVaultTotpPreset => VaultGroup == VaultEntryGroup.Totp;

    public bool HasVaultQuickFilters => CurrentVaultQuickFilters().IsOn;

    public string VaultMoreButtonTip =>
        $"{_localization.Get("LibrarySort")}: {GetPasswordSortLabel(SelectedPasswordSort)}";

    // The same empty tree means two different things depending on whether a filter is on.
    public string VaultEmptyStateText =>
        _localization.Get(CurrentVaultFilter().IsNarrowing ? "LibraryNoMatchesHint" : "LibraryEmptyHint");

    private VaultTreeFilter CurrentVaultFilter() =>
        new(VaultGroup, VaultSearchText, VaultFavoritesOnly, SelectedPasswordSort, CurrentVaultQuickFilters())
        {
            CustomFieldMatchIds = LiveMetadataMatches(VaultSearchText, _passwordCustomFieldSearchQuery,
                _passwordCustomFieldSearchMatches),
            AttachmentMatchIds = LiveMetadataMatches(VaultSearchText, _passwordAttachmentSearchQuery,
                _passwordAttachmentSearchMatches),
            SecureItemPayloadText = VaultSecureItemPayloadText
        };

    // The metadata ids arrive a quarter second after the tree has already narrowed on the in-memory
    // fields, so they only count while they still describe the text that produced them; anything older
    // would widen a search the user has since continued typing.
    private static IReadOnlySet<long>? LiveMetadataMatches(
        string searchText,
        string publishedQuery,
        IReadOnlySet<long> matches) =>
        string.IsNullOrWhiteSpace(searchText) ||
        !string.Equals(searchText, publishedQuery, StringComparison.Ordinal)
            ? null
            : matches;

    private Dictionary<SecureItem, VaultPayloadText>? _vaultPayloadTexts;

    private sealed record VaultPayloadText(string ItemData, string Title, string Notes, string Text);

    // Decoding a payload is the only part of a library search that is more than a string compare, and
    // one refresh asks every item twice — the tree and the match count — while a search of a few
    // keystrokes asks the whole library again per letter. The text does not depend on the query, so it
    // is memoized for as long as the library is on screen and dropped with it.
    private string VaultSecureItemPayloadText(SecureItem item)
    {
        var cache = _vaultPayloadTexts ??= [];
        if (cache.TryGetValue(item, out var cached) &&
            cached.ItemData == item.ItemData &&
            cached.Title == item.Title &&
            cached.Notes == item.Notes)
        {
            return cached.Text;
        }

        var payloadText = VaultSearchFields.SecureItemPayloadText(item);
        if (cache.Count > NoteItems.Count + TotpItems.Count + WalletItems.Count)
        {
            // Deleting in bulk while a search is on screen would otherwise leave the dropped entries
            // referenced by keys nobody will ask about again.
            _vaultPayloadTexts = null;
        }
        else
        {
            cache[item] = new VaultPayloadText(item.ItemData, item.Title, item.Notes, payloadText);
        }

        return payloadText;
    }

    private void ReleaseVaultSearchPayloads() => _vaultPayloadTexts = null;

    // The sort order and the quick filters are the vault pages' own state, so the library narrows and
    // reorders exactly as the list it replaced does instead of keeping a second copy of both.
    private VaultQuickFilters CurrentVaultQuickFilters() => IsVaultPasswordsPreset
        ? new VaultQuickFilters(
            QuickFilter2Fa,
            QuickFilterNotes,
            QuickFilterPasskey,
            QuickFilterBoundNote,
            QuickFilterUncategorized,
            QuickFilterLocalOnly,
            QuickFilterAttachments)
        : VaultQuickFilters.None;

    /// Called by the shared filter callbacks: a quick filter or a sort order moves the tree as much
    /// as it moves the password list, and the more menu has to report that something is on.
    private void RaiseVaultFilterState()
    {
        OnPropertyChanged(nameof(HasVaultQuickFilters));
        OnPropertyChanged(nameof(VaultMoreButtonTip));
        RaiseVaultTreeState();
    }

    partial void OnSelectedVaultRowChanged(IVaultTreeRow? value)
    {
        if (_isRestoringVaultSelection)
        {
            return;
        }

        OpenVaultEntry(value);
    }

    partial void OnVaultSearchTextChanged(string value)
    {
        // Two different clocks: the tree narrows on the in-memory fields after a 60 ms coalesce, and
        // the metadata search answers separately once the repository pass over custom fields and
        // attachments finishes.
        QueuePasswordSearchQuery(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            ReleaseVaultSearchPayloads();
        }

        QueueVaultTreeRefresh();
    }

    partial void OnVaultFavoritesOnlyChanged(bool value)
    {
        RaiseVaultTreeState();
    }

    // One reset flips eight filter fields, and each write would rebuild the whole tree again; a
    // scope keeps those writes down to the single rebuild its Dispose flushes.
    private VaultTreeRefreshScope SuppressVaultTreeRefresh() => new(this);

    private readonly struct VaultTreeRefreshScope(MainWindowViewModel owner) : IDisposable
    {
        public void Dispose()
        {
            if (--owner._vaultTreeRefreshDepth > 0)
            {
                return;
            }

            owner._vaultSearchDebounce?.Stop();
            owner.RaiseVaultTreeState();
        }
    }

    // A section switch can change the preset while the library stays on screen, because the four
    // type sections resolve to this page; the shell calls this after it raises SelectedSection.
    public void RefreshVaultPreset()
    {
        OnPropertyChanged(nameof(VaultEmptyStateText));
        if (_isVaultTreeAttached)
        {
            RebuildVaultTree();
        }
    }

    // Only the library page watches the shared collections, and it detaches the moment another
    // section takes over so a bulk write elsewhere never pays for a tree it is not showing.
    // The page host calls this on its own visual-tree attach/detach.
    public void SetVaultTreeActive(bool isActive)
    {
        if (isActive == _isVaultTreeAttached)
        {
            return;
        }

        _isVaultTreeAttached = isActive;
        foreach (var collection in VaultTreeSourceCollections())
        {
            if (isActive)
            {
                collection.CollectionChanged += OnVaultSourceCollectionChanged;
            }
            else
            {
                collection.CollectionChanged -= OnVaultSourceCollectionChanged;
            }
        }

        if (isActive)
        {
            RebuildVaultTree();
            EnsureVaultMetadataSearch();
        }
        else
        {
            ReleaseVaultTree();
        }
    }

    // The tree narrows on the fields it already holds, and only a repository pass can add a
    // custom-field or attachment hit. A search whose pass never ran — because the shell hibernated
    // or another section was up when the page left the screen — would otherwise stay half-applied
    // until the user typed again, so the page asks for it as it arrives.
    private void EnsureVaultMetadataSearch()
    {
        if (_isUnlockedShellHibernated ||
            !IsUnlocked ||
            !VaultPresets.IsLibrarySection(SelectedSection) ||
            string.IsNullOrWhiteSpace(VaultSearchText))
        {
            return;
        }

        // Ids that still describe this text survived the caches they came from, and the rebuild just
        // ran with them; a second scan would only pay for what the tree already shows.
        if (!string.Equals(VaultSearchText, _passwordCustomFieldSearchQuery, StringComparison.Ordinal) ||
            !string.Equals(VaultSearchText, _passwordAttachmentSearchQuery, StringComparison.Ordinal))
        {
            QueuePasswordSearchQuery(VaultSearchText);
        }
    }

    // Which folders were folded up is navigation state the user set, not cached content, so it
    // survives; the rows themselves hold entry references and must not.
    private void ReleaseVaultTree()
    {
        DetachVaultEntryObservers();
        VaultTreeRows.Clear();
        ReleaseVaultSearchPayloads();
        SelectedVaultRow = null;
        OnPropertyChanged(nameof(HasVaultRows));
        OnPropertyChanged(nameof(VaultEmptyStateText));
    }

    // A folder rename changes Category.Name in place, so no collection event would reach the tree.
    private void RaiseVaultTreeState()
    {
        if (_vaultTreeRefreshDepth > 0 || !_isVaultTreeAttached)
        {
            return;
        }

        RebuildVaultTree();
    }

    private IEnumerable<INotifyCollectionChanged> VaultTreeSourceCollections()
    {
        yield return Passwords;
        yield return NoteItems;
        yield return TotpItems;
        yield return WalletItems;
        yield return Categories;
    }

    private void OnVaultSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildVaultTree();

    private void QueueVaultTreeRefresh()
    {
        if (!_isVaultTreeAttached)
        {
            return;
        }

        _vaultSearchDebounce ??= CreateVaultSearchDebounce();
        _vaultSearchDebounce.Stop();
        _vaultSearchDebounce.Start();
    }

    private DispatcherTimer CreateVaultSearchDebounce()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(VaultSearchCoalesceMs)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RebuildVaultTree();
        };
        return timer;
    }

    private void RebuildVaultTree()
    {
        var selectedKey = SelectedVaultRow?.Key;
        var rows = VaultTreeBuilder.Build(
            Categories.ToArray(),
            Passwords.ToArray(),
            VaultSecureItems(),
            _collapsedVaultFolderKeys,
            CurrentVaultFilter());

        DetachVaultEntryObservers();
        var rebuilt = new ObservableCollection<IVaultTreeRow>();
        foreach (var row in rows)
        {
            rebuilt.Add(row);
            ObserveVaultEntry(row);
        }

        // The bound list can answer a replaced collection with its own first row, and a row nobody
        // clicked must not open an editor: the swap happens with opens suppressed and
        // RestoreVaultSelection decides afterwards what is actually selected.
        _isRestoringVaultSelection = true;
        try
        {
            VaultTreeRows = rebuilt;
            OnPropertyChanged(nameof(VaultTreeRows));
        }
        finally
        {
            _isRestoringVaultSelection = false;
        }

        RestoreVaultSelection(selectedKey);
        OnPropertyChanged(nameof(HasVaultRows));
        OnPropertyChanged(nameof(VaultEmptyStateText));
        OnPropertyChanged(nameof(HasVaultFilters));
        OnPropertyChanged(nameof(VaultFilteredStatusText));
    }

    private SecureItem[] VaultSecureItems()
    {
        // A password-typed secure item has no row of its own; the builder drops those on the floor.
        var items = new List<SecureItem>(NoteItems.Count + TotpItems.Count + WalletItems.Count);
        items.AddRange(NoteItems);
        items.AddRange(TotpItems);
        items.AddRange(WalletItems);
        return items.ToArray();
    }

    private void ObserveVaultEntry(IVaultTreeRow row)
    {
        if (row is not VaultTreeEntryRow entry)
        {
            return;
        }

        var observed = (INotifyPropertyChanged?)entry.Password ?? entry.Item;
        if (observed is not null)
        {
            observed.PropertyChanged += OnVaultEntryPropertyChanged;
            _observedVaultEntries[observed] = entry;
        }
    }

    private void DetachVaultEntryObservers()
    {
        foreach (var observed in _observedVaultEntries.Keys)
        {
            observed.PropertyChanged -= OnVaultEntryPropertyChanged;
        }

        _observedVaultEntries.Clear();
    }

    private void OnVaultEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // TOTP codes tick every second; only the properties the row actually prints are worth a rebuild.
        if (e.PropertyName is nameof(PasswordEntry.Title) or nameof(PasswordEntry.Username) or
                nameof(PasswordEntry.Website) or nameof(PasswordEntry.IsFavorite) or
                nameof(SecureItem.Title) or nameof(SecureItem.IsFavorite))
        {
            RebuildVaultTree();
            return;
        }

        // A row reads its own check mark off the entry, so a selection needs no new rows: the row that
        // is already on screen is told to read it again, and only the header notices the set moved.
        if (e.PropertyName is nameof(PasswordEntry.IsSelected) or nameof(SecureItem.IsSelected))
        {
            if (sender is INotifyPropertyChanged observed &&
                _observedVaultEntries.TryGetValue(observed, out var row))
            {
                row.RaiseIsSelectedChanged();
            }

            RaiseVaultBatchState();
        }
    }

    private void RestoreVaultSelection(string? selectedKey)
    {
        var match = string.IsNullOrEmpty(selectedKey)
            ? null
            : VaultTreeRows.FirstOrDefault(row => string.Equals(row.Key, selectedKey, StringComparison.Ordinal));

        if (ReferenceEquals(match, SelectedVaultRow))
        {
            return;
        }

        // Nothing of the user's survived the rebuild, so the row the list selected for itself goes
        // back the way it came: no highlight, no editor.
        _isRestoringVaultSelection = true;
        SelectedVaultRow = match;
        _isRestoringVaultSelection = false;
    }

    /// The row only decides *which* editor opens; the editor's own page keeps owning the selection
    /// state, so the library never has to duplicate a load path.
    private void OpenVaultEntry(IVaultTreeRow? row)
    {
        switch (row)
        {
            case VaultTreeEntryRow { Password: { } password }:
                SelectedPassword = password;
                break;
            case VaultTreeEntryRow { Item: { } item, Kind: VaultEntryKind.Note }:
                OpenNoteCommand.Execute(item);
                break;
            case VaultTreeEntryRow { Item: { } item, Kind: VaultEntryKind.Totp }:
                ShowTotpDetailsCommand.Execute(item);
                break;
            case VaultTreeEntryRow { Item: { } item }:
                ShowWalletDetailsCommand.Execute(item);
                break;
        }
    }

    [RelayCommand]
    private void ToggleVaultFolderExpansion(IVaultTreeRow? row)
    {
        if (row is not { HasChildren: true })
        {
            return;
        }

        if (!_collapsedVaultFolderKeys.Add(row.Key))
        {
            _collapsedVaultFolderKeys.Remove(row.Key);
        }

        RebuildVaultTree();
    }

    [RelayCommand]
    private async Task CreateVaultFolderAsync()
    {
        if (await CreateLocalCategoryAsync(SelectedVaultFolderPath, NewFolderName) is not null)
        {
            NewFolderName = "";
        }
    }

    [RelayCommand]
    private async Task RenameSelectedVaultFolderAsync()
    {
        if (await RenameLocalCategoryAsync(GetSelectedVaultFolderCategory(), NewFolderName) is not null)
        {
            NewFolderName = "";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedVaultFolderAsync() =>
        await DeleteLocalCategoryAsync(GetSelectedVaultFolderCategory());

    [RelayCommand(CanExecute = nameof(CanMoveVaultFolder))]
    private async Task MoveVaultFolderAsync(FolderMoveRequest? request)
    {
        if (request?.Source is not VaultTreeFolderRow { CategoryId: > 0 } source ||
            request.Target is not VaultTreeFolderRow { Path.Length: > 0 } target)
        {
            return;
        }

        await MoveLocalCategoryAsync(FindCategory(source.CategoryId.Value), LocalCategoryPath.Normalize(target.Path));
    }

    private bool CanMoveVaultFolder(FolderMoveRequest? request) =>
        request?.Source is VaultTreeFolderRow { CategoryId: > 0 } source &&
        request.Target is VaultTreeFolderRow { Path.Length: > 0 } target &&
        FindCategory(source.CategoryId.Value) is { } category &&
        LocalCategoryPath.PlanSubtreeMove(Categories, category, LocalCategoryPath.Normalize(target.Path)) is not null;

    [RelayCommand]
    private void ClearVaultSearch() => VaultSearchText = "";

    [RelayCommand]
    private void ToggleVaultFavorites() => VaultFavoritesOnly = !VaultFavoritesOnly;

    [RelayCommand]
    private void EditSelectedVaultEntry()
    {
        if (SelectedVaultRow is not VaultTreeEntryRow row)
        {
            return;
        }

        if (row.Password is { } password)
        {
            EditPasswordCommand.Execute(password);
        }
        else if (row.Item is { } item)
        {
            switch (row.Kind)
            {
                case VaultEntryKind.Note:
                    OpenNoteCommand.Execute(item);
                    break;
                case VaultEntryKind.Totp:
                    EditTotpCommand.Execute(item);
                    break;
                default:
                    ShowWalletDetailsCommand.Execute(item);
                    break;
            }
        }
    }

    [RelayCommand]
    private async Task MoveSelectedVaultEntryAsync()
    {
        if (SelectedVaultRow is not VaultTreeEntryRow row)
        {
            return;
        }

        var currentCategoryId = row.Password?.CategoryId ?? row.Item?.CategoryId;
        var choice = await _categoryPickerDialogService.ShowAsync(Categories.ToList(), currentCategoryId);
        if (choice is null)
        {
            return;
        }

        if (row.Password is { } password)
        {
            password.CategoryId = choice.Id;
            await _repository.SavePasswordAsync(password);
            await SynchronizeBoundTotpAsync(password);
            await LogVaultCategoryMoveAsync("PASSWORD", password.Id, password.Title);
        }
        else if (row.Item is { } item)
        {
            item.CategoryId = choice.Id;
            await _repository.SaveSecureItemAsync(item);
            await LogVaultCategoryMoveAsync("SECURE_ITEM", item.Id, item.Title);
        }
        else
        {
            return;
        }

        // CategoryId is a plain property, so no change notification reaches the tree on its own.
        RebuildVaultTree();
        StatusMessage = _localization.Format("MovedSelectedPasswordsToFolderFormat", 1, choice.Name);
    }

    private Task LogVaultCategoryMoveAsync(string itemType, long itemId, string itemTitle) =>
        LogOperationAsync(new OperationLog
        {
            ItemType = itemType,
            ItemId = itemId,
            ItemTitle = itemTitle,
            OperationType = "MOVE_CATEGORY",
            DeviceName = Environment.MachineName
        });

    [RelayCommand]
    private void DeleteSelectedVaultEntry()
    {
        if (SelectedVaultRow is not VaultTreeEntryRow row)
        {
            return;
        }

        if (row.Password is { } password)
        {
            DeletePasswordCommand.Execute(password);
            return;
        }

        if (row.Item is { } item)
        {
            switch (row.Kind)
            {
                case VaultEntryKind.Note:
                    DeleteNoteCommand.Execute(item);
                    break;
                case VaultEntryKind.Totp:
                    DeleteTotpCommand.Execute(item);
                    break;
                default:
                    DeleteWalletItemCommand.Execute(item);
                    break;
            }
        }
    }

    private Category? GetSelectedVaultFolderCategory() =>
        SelectedVaultRow is VaultTreeFolderRow { CategoryId: > 0 } folder ? FindCategory(folder.CategoryId.Value) : null;

    private Category? FindCategory(long categoryId) => Categories.FirstOrDefault(item => item.Id == categoryId);
}
