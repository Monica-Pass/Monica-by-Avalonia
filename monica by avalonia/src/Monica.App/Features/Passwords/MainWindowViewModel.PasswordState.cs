using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Monica.App;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void RaisePasswordSelectionState()
    {
        OnPropertyChanged(nameof(SelectedPasswordCount));
        OnPropertyChanged(nameof(SelectedPasswordCountText));
        OnPropertyChanged(nameof(HasSelectedPasswords));
        OnPropertyChanged(nameof(CanStackSelectedPasswords));
        OnPropertyChanged(nameof(AreAllFilteredPasswordsSelected));
        RaiseFilteredPasswordRowsChanged();
    }

    private void RefreshPasswordSelectionStateFromPasswords()
    {
        _selectedPasswordCount = Passwords.Count(item => item.IsSelected);
        RaisePasswordSelectionState();
    }

    private void UpdatePasswordSelectionsInBatch(Action updateSelections)
    {
        var wasSuppressed = _suppressPasswordSelectionStateNotifications;
        _suppressPasswordSelectionStateNotifications = true;
        try
        {
            updateSelections();
        }
        finally
        {
            _suppressPasswordSelectionStateNotifications = wasSuppressed;
        }

        if (!wasSuppressed)
        {
            RefreshPasswordSelectionStateFromPasswords();
        }
    }

    private void RaisePasswordFilterState()
    {
        OnPropertyChanged(nameof(HasPasswordFilters));
        OnPropertyChanged(nameof(PasswordFilterSummaryText));
        OnPropertyChanged(nameof(PasswordEmptyStateText));
        OnPropertyChanged(nameof(ShowClearPasswordFiltersInEmptyState));
    }

    private void RaisePasswordFolderFilterCollections()
    {
        OnPropertyChanged(nameof(SystemPasswordFolderFilters));
        OnPropertyChanged(nameof(RegularPasswordFolderFilters));
        OnPropertyChanged(nameof(HasRegularPasswordFolderFilters));
    }

    private IReadOnlyList<PasswordEntry> GetFilteredPasswords()
    {
        if (_filteredPasswordsDirty)
        {
            var stopwatch = Stopwatch.StartNew();
            _filteredPasswords = ApplyPasswordSort(Passwords.Where(MatchesPasswordFilters)).ToArray();
            AppDiagnostics.Info($"Rebuild filtered password list completed in {stopwatch.ElapsedMilliseconds} ms. count={_filteredPasswords.Count}");
            _filteredPasswordsDirty = false;
        }

        return _filteredPasswords;
    }

    private IReadOnlyList<PasswordListRow> GetFilteredPasswordRows()
    {
        if (!_filteredPasswordRowsDirty)
        {
            return _filteredPasswordRows;
        }

        var visiblePasswords = FilteredPasswords.ToArray();
        var groupsByKey = visiblePasswords
            .GroupBy(BuildSiblingGroupKey)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var handledGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<PasswordListRow>(visiblePasswords.Length);

        foreach (var entry in visiblePasswords)
        {
            var groupKey = BuildSiblingGroupKey(entry);
            var members = groupsByKey[groupKey];
            if (members.Length < 2)
            {
                rows.Add(new PasswordListRow(
                    $"password:{entry.Id}",
                    entry,
                    [entry],
                    isStackHeader: false,
                    isStackChild: false,
                    isFirstStackChild: false,
                    isLastStackChild: false,
                    isExpanded: false));
                continue;
            }

            if (!handledGroupKeys.Add(groupKey))
            {
                continue;
            }
            var lead = members.FirstOrDefault(item => item.IsGroupCover) ?? members[0];
            var rowKey = $"stack:{groupKey}";
            var isExpanded = _expandedPasswordStackKeys.Contains(rowKey);
            rows.Add(new PasswordListRow(
                rowKey,
                lead,
                members,
                isStackHeader: true,
                isStackChild: false,
                isFirstStackChild: false,
                isLastStackChild: false,
                isExpanded));

            if (!isExpanded)
            {
                continue;
            }

            for (var index = 0; index < members.Length; index++)
            {
                var member = members[index];
                rows.Add(new PasswordListRow(
                    $"{rowKey}:password:{member.Id}",
                    member,
                    [member],
                    isStackHeader: false,
                    isStackChild: true,
                    isFirstStackChild: index == 0,
                    isLastStackChild: index == members.Length - 1,
                    isExpanded: false));
            }
        }

        _filteredPasswordRows = rows;
        _filteredPasswordRowsDirty = false;
        return _filteredPasswordRows;
    }

    private void RaiseFilteredPasswordsChanged()
    {
        _filteredPasswordsDirty = true;
        _filteredPasswordRowsDirty = true;
        OnPropertyChanged(nameof(FilteredPasswords));
        OnPropertyChanged(nameof(FilteredPasswordRows));
        OnPropertyChanged(nameof(VisiblePasswordNavigationEntries));
        OnPropertyChanged(nameof(HasFilteredPasswordRows));
        OnPropertyChanged(nameof(PasswordEmptyStateText));
        OnPropertyChanged(nameof(ShowAddPasswordInEmptyState));
        OnPropertyChanged(nameof(ShowClearPasswordFiltersInEmptyState));
        SyncSelectedPasswordListRow(SelectedPassword);
    }

    private void RaiseFilteredPasswordRowsChanged()
    {
        _filteredPasswordRowsDirty = true;
        OnPropertyChanged(nameof(FilteredPasswordRows));
        OnPropertyChanged(nameof(VisiblePasswordNavigationEntries));
        OnPropertyChanged(nameof(HasFilteredPasswordRows));
        SyncSelectedPasswordListRow(SelectedPassword);
    }

    private void RefreshPasswordFilters()
    {
        RefreshPasswordFolderFilters();
        RaiseFilteredPasswordsChanged();
        OnPropertyChanged(nameof(FilteredArchivedPasswords));
        OnPropertyChanged(nameof(FilteredDeletedPasswords));
        RaisePasswordFilterState();
        RaisePasswordSelectionState();
        ReconcileSelectedPasswordDetails();
    }

    private void ReconcileSelectedPasswordDetails()
    {
        var visiblePasswords = FilteredPasswords.ToArray();
        if (SelectedPassword is not null && visiblePasswords.All(item => item.Id != SelectedPassword.Id))
        {
            SelectedPassword = null;
        }

        SyncSelectedPasswordListRow(SelectedPassword);
    }

    private void SyncSelectedPasswordListRow(PasswordEntry? selectedPassword)
    {
        _isSyncingSelectedPasswordListRow = true;
        try
        {
            SelectedPasswordListRow = selectedPassword is null
                ? null
                : FindPasswordListRowForSelection(selectedPassword);
        }
        finally
        {
            _isSyncingSelectedPasswordListRow = false;
        }
    }

    private PasswordListRow? FindPasswordListRowForSelection(PasswordEntry selectedPassword)
    {
        var rows = FilteredPasswordRows;
        return rows.FirstOrDefault(row => row.IsPasswordEntryRow && row.Entry.Id == selectedPassword.Id) ??
            rows.FirstOrDefault(row => row.IsStackHeader && row.Members.Any(item => item.Id == selectedPassword.Id));
    }
    private void TrackPasswordSelection(PasswordEntry entry)
    {
        entry.PropertyChanged -= PasswordEntryPropertyChanged;
        entry.PropertyChanged += PasswordEntryPropertyChanged;
    }

    private void PasswordEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PasswordEntry.IsSelected))
        {
            if (_suppressPasswordSelectionStateNotifications)
            {
                return;
            }

            if (sender is PasswordEntry entry)
            {
                if (Passwords.Contains(entry))
                {
                    var delta = entry.IsSelected ? 1 : -1;
                    _selectedPasswordCount = Math.Clamp(_selectedPasswordCount + delta, 0, Passwords.Count);
                }
                else
                {
                    _selectedPasswordCount = Passwords.Count(item => item.IsSelected);
                }
            }

            RaisePasswordSelectionState();
        }
    }

    private async Task LoadPasswordQuickAccessAsync()
    {
        _passwordQuickAccessRecords = (await _repository.GetPasswordQuickAccessRecordsAsync())
            .Where(record => record.OpenCount > 0 && record.PasswordId > 0)
            .ToDictionary(record => record.PasswordId);
        RaisePasswordQuickAccessState();
    }

    private async Task RecordPasswordQuickAccessAsync(PasswordEntry entry)
    {
        if (entry.Id <= 0 || entry.IsDeleted || entry.IsArchived)
        {
            return;
        }

        await _repository.RecordPasswordQuickAccessAsync(entry.Id);
        var next = _passwordQuickAccessRecords.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (next.TryGetValue(entry.Id, out var existing))
        {
            existing.OpenCount++;
            existing.LastOpenedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            next[entry.Id] = new PasswordQuickAccessRecord
            {
                PasswordId = entry.Id,
                OpenCount = 1,
                LastOpenedAt = DateTimeOffset.UtcNow
            };
        }

        _passwordQuickAccessRecords = next;
        RaisePasswordQuickAccessState();
    }

    private IEnumerable<PasswordQuickAccessItem> BuildQuickAccessItems(QuickAccessSort sort)
    {
        var records = sort == QuickAccessSort.Frequent
            ? _passwordQuickAccessRecords.Values
                .OrderByDescending(record => record.OpenCount)
                .ThenByDescending(record => record.LastOpenedAt)
            : _passwordQuickAccessRecords.Values
                .OrderByDescending(record => record.LastOpenedAt)
                .ThenByDescending(record => record.OpenCount);

        return records
            .Select(record =>
            {
                var entry = Passwords.FirstOrDefault(item => item.Id == record.PasswordId);
                return entry is null
                    ? null
                    : new PasswordQuickAccessItem(
                        entry,
                        record.OpenCount,
                        record.LastOpenedAt.ToString("g", _localization.Culture),
                        BuildQuickAccessSubtitle(entry));
            })
            .OfType<PasswordQuickAccessItem>()
            .Take(PasswordQuickAccessLimit)
            .ToArray();
    }

    private static string BuildQuickAccessSubtitle(PasswordEntry entry) => BuildPasswordSubtitle(entry);

    private static string BuildPasswordSubtitle(PasswordEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Website)
            ? entry.Username
            : string.IsNullOrWhiteSpace(entry.Username)
                ? entry.Website
                : $"{entry.Username} - {entry.Website}";
    }

    private void RaisePasswordQuickAccessState()
    {
        OnPropertyChanged(nameof(RecentPasswordQuickAccessItems));
        OnPropertyChanged(nameof(FrequentPasswordQuickAccessItems));
        OnPropertyChanged(nameof(HasPasswordQuickAccessItems));
    }

    private IEnumerable<PasswordEntry> ApplyPasswordSort(IEnumerable<PasswordEntry> items)
    {
        return SelectedPasswordSort switch
        {
            "title-asc" => items
                .OrderBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.Id),
            "website-asc" => items
                .OrderBy(item => NormalizeSortText(item.Website), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id),
            "username-asc" => items
                .OrderBy(item => NormalizeSortText(item.Username), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id),
            "created-desc" => items
                .OrderByDescending(item => item.CreatedAt)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id),
            "favorites-first" => items
                .OrderByDescending(item => item.IsFavorite)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id),
            _ => items
                .OrderByDescending(item => item.UpdatedAt)
                .ThenBy(item => NormalizeSortText(item.Title), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Id)
        };
    }

    private static string NormalizeSortText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? "\uffff" : text;
    }

    private string GetPasswordSortLabel(string value)
    {
        return value switch
        {
            "title-asc" => SortTitleText,
            "website-asc" => SortWebsiteText,
            "username-asc" => SortUsernameText,
            "created-desc" => SortCreatedText,
            "favorites-first" => SortFavoritesText,
            _ => SortUpdatedText
        };
    }

    private Category? GetSelectedPasswordFolderCategory()
    {
        var selectedId = SelectedPasswordFolderFilter?.Id;
        return selectedId is > 0
            ? Categories.FirstOrDefault(item => item.Id == selectedId.Value)
            : null;
    }

    private void RefreshPasswordFolderFilters(long? preferredCategoryId = null)
    {
        if (preferredCategoryId is > 0)
        {
            var preferredCategory = Categories.FirstOrDefault(category => category.Id == preferredCategoryId.Value);
            if (preferredCategory is not null)
            {
                ExpandPasswordFolderPath(preferredCategory.Name);
            }
        }

        var selectedKey = preferredCategoryId is not null
            ? CategorySelectionKey(preferredCategoryId.Value)
            : SelectedPasswordFolderFilter?.SelectionKey;
        var folderCountPasswords = Passwords.Where(MatchesPasswordNonFolderFilters).ToArray();
        PasswordFolderFilters.Clear();
        PasswordFolderFilters.Add(new PasswordFolderFilterChoice(
            null,
            _localization.Get("AllFolders"),
            folderCountPasswords.Length,
            IsSystemNode: true,
            SelectionKey: "system:all"));
        PasswordFolderFilters.Add(new PasswordFolderFilterChoice(
            -2,
            _localization.Get("QuickFilterFavorite"),
            folderCountPasswords.Count(password => password.IsFavorite),
            IsSystemNode: true,
            SelectionKey: "system:favorites"));

        foreach (var root in BuildPasswordFolderTree(folderCountPasswords))
        {
            AddVisiblePasswordFolder(root);
        }

        PasswordFolderFilters.Add(new PasswordFolderFilterChoice(
            -1,
            _localization.Get("NoFolder"),
            folderCountPasswords.Count(password => password.CategoryId is null),
            IsSystemNode: true,
            SelectionKey: "system:none"));

        SelectedPasswordFolderFilter =
            PasswordFolderFilters.FirstOrDefault(item => string.Equals(item.SelectionKey, selectedKey, StringComparison.OrdinalIgnoreCase)) ??
            PasswordFolderFilters.FirstOrDefault(item => item.Id == preferredCategoryId) ??
            PasswordFolderFilters.FirstOrDefault();
        RaiseFilteredPasswordsChanged();
        OnPropertyChanged(nameof(CanManageSelectedPasswordFolder));
        RaisePasswordFolderFilterCollections();
        RaisePasswordFilterState();
    }

    private IReadOnlyList<PasswordFolderTreeNode> BuildPasswordFolderTree(IReadOnlyList<PasswordEntry> folderCountPasswords)
    {
        var roots = new List<PasswordFolderTreeNode>();
        var nodes = new Dictionary<string, PasswordFolderTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in Categories.OrderBy(item => item.SortOrder).ThenBy(item => item.Name))
        {
            var pathParts = SplitFolderPath(category.Name);
            if (pathParts.Length == 0)
            {
                pathParts = [category.Name];
            }

            PasswordFolderTreeNode? parent = null;
            for (var index = 0; index < pathParts.Length; index++)
            {
                var key = string.Join("/", pathParts.Take(index + 1));
                if (!nodes.TryGetValue(key, out var node))
                {
                    node = new PasswordFolderTreeNode(key, pathParts[index], index);
                    nodes[key] = node;
                    if (parent is null)
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        parent.Children.Add(node);
                    }
                }

                if (index == pathParts.Length - 1)
                {
                    node.Category = category;
                    node.ExactCount = folderCountPasswords.Count(password => password.CategoryId == category.Id);
                }

                parent = node;
            }
        }

        foreach (var root in roots)
        {
            UpdatePasswordFolderDescendantCount(root);
        }

        SortPasswordFolderNodes(roots);
        return roots;
    }

    private static int UpdatePasswordFolderDescendantCount(PasswordFolderTreeNode node)
    {
        node.DescendantCount = node.ExactCount + node.Children.Sum(UpdatePasswordFolderDescendantCount);
        return node.DescendantCount;
    }

    private static void SortPasswordFolderNodes(List<PasswordFolderTreeNode> nodes)
    {
        nodes.Sort((left, right) =>
        {
            var leftSort = left.Category?.SortOrder ?? int.MaxValue;
            var rightSort = right.Category?.SortOrder ?? int.MaxValue;
            var sortCompare = leftSort.CompareTo(rightSort);
            return sortCompare != 0
                ? sortCompare
                : string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var node in nodes)
        {
            SortPasswordFolderNodes(node.Children);
        }
    }

    private void AddVisiblePasswordFolder(PasswordFolderTreeNode node)
    {
        var hasChildren = node.Children.Count > 0;
        var isExpanded = hasChildren && !_collapsedPasswordFolderKeys.Contains(PathSelectionKey(node.Key));
        PasswordFolderFilters.Add(new PasswordFolderFilterChoice(
            node.Category?.Id,
            node.Category?.Name ?? node.Key,
            node.DescendantCount,
            node.DisplayName,
            node.Level,
            SelectionKey: node.Category is null ? PathSelectionKey(node.Key) : CategorySelectionKey(node.Category.Id),
            PathPrefix: node.Key,
            HasChildren: hasChildren,
            IsExpanded: isExpanded));

        if (!isExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AddVisiblePasswordFolder(child);
        }
    }

    private static string CategorySelectionKey(long id) => $"category:{id}";

    private static string PathSelectionKey(string path) => $"path:{path}";

    private void ExpandPasswordFolderPath(string name)
    {
        var pathParts = SplitFolderPath(name);
        for (var index = 0; index < pathParts.Length - 1; index++)
        {
            _collapsedPasswordFolderKeys.Remove(PathSelectionKey(string.Join("/", pathParts.Take(index + 1))));
        }
    }

    private static string[] SplitFolderPath(string value) =>
        value.Split(['/', '\\'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private string ProtectPassword(string password)
    {
        return _cryptoService.IsUnlocked ? _cryptoService.EncryptString(password) : password;
    }

    private IReadOnlyList<string> ProtectPasswords(IReadOnlyList<string> passwords)
    {
        if (passwords.Count == 0)
        {
            return [ProtectPassword("")];
        }

        return passwords.Select(ProtectPassword).ToArray();
    }

    private string UnprotectPassword(string storedPassword)
    {
        if (!_cryptoService.IsUnlocked)
        {
            return storedPassword;
        }

        try
        {
            return _cryptoService.DecryptString(storedPassword);
        }
        catch
        {
            return storedPassword;
        }
    }

    private async Task SavePasswordHistorySnapshotIfChangedAsync(long entryId, string oldPlainPassword, string newPlainPassword)
    {
        if (entryId <= 0 ||
            string.IsNullOrWhiteSpace(oldPlainPassword) ||
            string.Equals(oldPlainPassword, newPlainPassword, StringComparison.Ordinal))
        {
            return;
        }

        var latestHistory = (await _repository.GetPasswordHistoryAsync(entryId)).FirstOrDefault();
        if (latestHistory is not null &&
            string.Equals(UnprotectPassword(latestHistory.Password), oldPlainPassword, StringComparison.Ordinal))
        {
            return;
        }

        await _repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = entryId,
            Password = ProtectPassword(oldPlainPassword),
            LastUsedAt = DateTimeOffset.UtcNow
        });
        await _repository.TrimPasswordHistoryAsync(entryId, PasswordHistoryLimit);
    }

    private async Task<IReadOnlyList<PasswordHistoryDisplayItem>> GetPasswordHistoryDisplayItemsAsync(long entryId)
    {
        var history = await _repository.GetPasswordHistoryAsync(entryId);
        return history
            .Select(item =>
            {
                var password = TryUnprotectHistoryPassword(item.Password);
                return new PasswordHistoryDisplayItem(item, password.DisplayValue, password.CanCopy);
            })
            .ToArray();
    }

    private (string DisplayValue, bool CanCopy) TryUnprotectHistoryPassword(string storedPassword)
    {
        if (string.IsNullOrWhiteSpace(storedPassword))
        {
            return (_localization.Get("PasswordHistoryUnavailable"), false);
        }

        if (!_cryptoService.IsUnlocked)
        {
            return ("********", false);
        }

        try
        {
            return (_cryptoService.DecryptString(storedPassword), true);
        }
        catch
        {
            return (storedPassword, true);
        }
    }

    private async Task<bool> DeletePasswordHistoryAsync(PasswordHistoryEntry entry)
    {
        if (!await ConfirmDeletePasswordHistoryAsync())
        {
            return false;
        }

        await _repository.DeletePasswordHistoryAsync(entry.Id);
        StatusMessage = _localization.Get("DeletedPasswordHistoryEntry");
        return true;
    }

    private async Task<bool> ClearPasswordHistoryAsync(long entryId)
    {
        if (!await ConfirmClearPasswordHistoryAsync())
        {
            return false;
        }

        await _repository.ClearPasswordHistoryAsync(entryId);
        StatusMessage = _localization.Get("ClearedPasswordHistory");
        return true;
    }

    private PasswordEntry ClonePasswordForExport(PasswordEntry source, bool includeCategory = true)
    {
        var clone = ClonePassword(source);
        clone.Password = UnprotectPassword(source.Password);
        if (!includeCategory)
        {
            clone.CategoryId = null;
        }

        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        return clone;
    }

    private PasswordEntry ClonePasswordForImport(PasswordEntry source, IReadOnlyDictionary<long, long>? categoryIdMap = null)
    {
        var clone = ClonePassword(source);
        clone.Id = 0;
        clone.Password = ProtectPassword(UnprotectPassword(source.Password));
        clone.MdbxDatabaseId = null;
        clone.MdbxFolderId = null;
        if (clone.CategoryId is { } categoryId)
        {
            clone.CategoryId = categoryIdMap?.TryGetValue(categoryId, out var importedCategoryId) == true
                ? importedCategoryId
                : null;
        }

        clone.IsDeleted = false;
        clone.DeletedAt = null;
        clone.IsArchived = false;
        clone.ArchivedAt = null;
        clone.BitwardenLocalModified = true;
        return clone;
    }
    private static PasswordEntry ClonePassword(PasswordEntry source)
    {
        return new PasswordEntry
        {
            Id = source.Id,
            Title = source.Title,
            Website = source.Website,
            Username = source.Username,
            Password = source.Password,
            Notes = source.Notes,
            IsFavorite = source.IsFavorite,
            SortOrder = source.SortOrder,
            IsGroupCover = source.IsGroupCover,
            AppPackageName = source.AppPackageName,
            AppName = source.AppName,
            Email = source.Email,
            Phone = source.Phone,
            AddressLine = source.AddressLine,
            City = source.City,
            State = source.State,
            ZipCode = source.ZipCode,
            Country = source.Country,
            CreditCardNumber = source.CreditCardNumber,
            CreditCardHolder = source.CreditCardHolder,
            CreditCardExpiry = source.CreditCardExpiry,
            CreditCardCvv = source.CreditCardCvv,
            CategoryId = source.CategoryId,
            BoundNoteId = source.BoundNoteId,
            KeepassDatabaseId = source.KeepassDatabaseId,
            KeepassGroupPath = source.KeepassGroupPath,
            KeepassEntryUuid = source.KeepassEntryUuid,
            KeepassGroupUuid = source.KeepassGroupUuid,
            MdbxDatabaseId = source.MdbxDatabaseId,
            MdbxFolderId = source.MdbxFolderId,
            AuthenticatorKey = source.AuthenticatorKey,
            PasskeyBindings = source.PasskeyBindings,
            SshKeyData = source.SshKeyData,
            LoginType = source.LoginType,
            SsoProvider = source.SsoProvider,
            SsoRefEntryId = source.SsoRefEntryId,
            WifiMetadata = source.WifiMetadata,
            CustomIconType = source.CustomIconType,
            CustomIconValue = source.CustomIconValue,
            CustomIconUpdatedAt = source.CustomIconUpdatedAt,
            IsDeleted = source.IsDeleted,
            DeletedAt = source.DeletedAt,
            IsArchived = source.IsArchived,
            ArchivedAt = source.ArchivedAt,
            ReplicaGroupId = source.ReplicaGroupId,
            BitwardenVaultId = source.BitwardenVaultId,
            BitwardenCipherId = source.BitwardenCipherId,
            BitwardenFolderId = source.BitwardenFolderId,
            BitwardenRevisionDate = source.BitwardenRevisionDate,
            BitwardenCipherType = source.BitwardenCipherType,
            BitwardenLocalModified = source.BitwardenLocalModified,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        };
    }

    private static CustomField CloneCustomFieldForImport(CustomField source, long importedPasswordId)
    {
        return new CustomField
        {
            Id = 0,
            EntryId = importedPasswordId,
            Title = source.Title,
            Value = source.Value,
            IsProtected = source.IsProtected,
            SortOrder = source.SortOrder
        };
    }

    private PasswordHistoryEntry ClonePasswordHistoryForExport(PasswordHistoryEntry source)
    {
        return new PasswordHistoryEntry
        {
            Id = source.Id,
            EntryId = source.EntryId,
            Password = UnprotectPassword(source.Password),
            LastUsedAt = source.LastUsedAt
        };
    }

    private PasswordHistoryEntry ClonePasswordHistoryForImport(PasswordHistoryEntry source, long importedPasswordId)
    {
        return new PasswordHistoryEntry
        {
            Id = 0,
            EntryId = importedPasswordId,
            Password = ProtectPassword(UnprotectPassword(source.Password)),
            LastUsedAt = source.LastUsedAt
        };
    }

    private static Attachment CloneAttachmentForExport(Attachment source)
    {
        return new Attachment
        {
            Id = source.Id,
            OwnerType = source.OwnerType,
            OwnerId = source.OwnerId,
            FileName = source.FileName,
            ContentType = source.ContentType,
            StoragePath = source.StoragePath,
            SizeBytes = source.SizeBytes,
            CreatedAt = source.CreatedAt,
            BitwardenVaultId = source.BitwardenVaultId,
            KeepassBinaryRef = source.KeepassBinaryRef
        };
    }

    private static Attachment CloneAttachmentForImport(Attachment source, long importedPasswordId)
    {
        return new Attachment
        {
            Id = 0,
            OwnerType = "PASSWORD",
            OwnerId = importedPasswordId,
            FileName = source.FileName,
            ContentType = source.ContentType,
            StoragePath = "",
            SizeBytes = source.SizeBytes,
            CreatedAt = source.CreatedAt == default ? DateTimeOffset.UtcNow : source.CreatedAt,
            BitwardenVaultId = source.BitwardenVaultId,
            KeepassBinaryRef = source.KeepassBinaryRef
        };
    }

    private void ReplacePasswordGroup(IReadOnlyList<PasswordEntry> previousEntries, IReadOnlyList<PasswordEntry> updatedEntries)
    {
        foreach (var previous in previousEntries)
        {
            Passwords.Remove(previous);
            var current = Passwords.FirstOrDefault(item => item.Id == previous.Id);
            if (current is not null)
            {
                Passwords.Remove(current);
            }
        }

        for (var index = updatedEntries.Count - 1; index >= 0; index--)
        {
            updatedEntries[index].IsSelected = false;
            TrackPasswordSelection(updatedEntries[index]);
            Passwords.Insert(0, updatedEntries[index]);
        }

        RefreshPasswordSelectionStateFromPasswords();
    }

    private static IReadOnlyList<CustomField> BindCustomFields(long entryId, IReadOnlyList<CustomField> fields)
    {
        return fields
            .Select((field, index) => new CustomField
            {
                EntryId = entryId,
                Title = field.Title,
                Value = field.Value,
                IsProtected = field.IsProtected,
                SortOrder = index
            })
            .ToArray();
    }

    private void SetPasswordCustomFields(long entryId, IReadOnlyList<CustomField> fields)
    {
        var next = _passwordCustomFields.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (fields.Count == 0)
        {
            next.Remove(entryId);
        }
        else
        {
            next[entryId] = fields;
        }

        _passwordCustomFields = next;
    }

    private IReadOnlyList<Attachment> GetPasswordAttachments(long entryId)
    {
        return _passwordAttachments.TryGetValue(entryId, out var attachments)
            ? attachments
            : [];
    }

    private IReadOnlyList<Attachment> GetGroupAttachments(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings) =>
        GetGroupAttachments(entry, siblings, _passwordAttachments);

    private static IReadOnlyList<Attachment> GetGroupAttachments(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        IReadOnlyDictionary<long, IReadOnlyList<Attachment>> attachmentsByPasswordId)
    {
        var siblingIds = siblings.Count == 0
            ? [entry.Id]
            : siblings.Select(item => item.Id).ToArray();
        return siblingIds
            .SelectMany(id => attachmentsByPasswordId.TryGetValue(id, out var attachments)
                ? attachments
                : Array.Empty<Attachment>())
            .OrderByDescending(attachment => attachment.CreatedAt)
            .ThenByDescending(attachment => attachment.Id)
            .ToArray();
    }

    private void SetPasswordAttachments(long entryId, IReadOnlyList<Attachment> attachments)
    {
        var next = _passwordAttachments.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (attachments.Count == 0)
        {
            next.Remove(entryId);
        }
        else
        {
            next[entryId] = attachments;
        }

        _passwordAttachments = next;
    }

    private void RefreshPasswordAttachmentState(PasswordEntry entry)
    {
        entry.HasAttachments = GetPasswordAttachments(entry.Id).Count > 0;
    }

    private async Task<IReadOnlyList<CustomField>> GetGroupCustomFieldsAsync(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings)
    {
        foreach (var candidate in siblings)
        {
            var fields = _passwordCustomFields.TryGetValue(candidate.Id, out var cachedFields)
                ? cachedFields
                : await _repository.GetCustomFieldsAsync(candidate.Id);
            if (fields.Count > 0 || candidate.Id == entry.Id)
            {
                return fields;
            }
        }

        return [];
    }

    private IReadOnlyList<CustomField> GetCachedGroupCustomFields(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings) =>
        GetCachedGroupCustomFields(entry, siblings, _passwordCustomFields);

    private static IReadOnlyList<CustomField> GetCachedGroupCustomFields(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>> customFieldsByPasswordId)
    {
        foreach (var candidate in siblings)
        {
            var fields = customFieldsByPasswordId.TryGetValue(candidate.Id, out var cachedFields)
                ? cachedFields
                : [];
            if (fields.Count > 0 || candidate.Id == entry.Id)
            {
                return fields;
            }
        }

        return [];
    }
}
