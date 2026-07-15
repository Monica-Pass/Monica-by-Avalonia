using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Monica.App;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    internal int FilteredPasswordsProjectionBuildCount { get; private set; }

    private void RaisePasswordSelectionState()
    {
        OnPropertyChanged(nameof(SelectedPasswordCount));
        OnPropertyChanged(nameof(SelectedPasswordCountText));
        OnPropertyChanged(nameof(HasSelectedPasswords));
        OnPropertyChanged(nameof(CanStackSelectedPasswords));
        OnPropertyChanged(nameof(AreAllFilteredPasswordsSelected));
        RaiseFilteredPasswordRowsChanged();
        RaiseArchiveSelectionState();
        RaiseRecycleBinSelectionState();
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
        OnPropertyChanged(nameof(PasswordListStatusText));
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
            FilteredPasswordsProjectionBuildCount++;
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
        if (_passwordProjectionNotificationDeferralDepth > 0)
        {
            _passwordSelectionReconciliationPending = true;
            return;
        }

        ReconcileSelectedPasswordDetailsImmediately();
    }

    private void ReconcileSelectedPasswordDetailsImmediately()
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

}
