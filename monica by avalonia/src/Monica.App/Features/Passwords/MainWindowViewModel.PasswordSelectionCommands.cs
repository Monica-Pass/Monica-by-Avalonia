using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void TogglePasswordStackRow(PasswordListRow? row)
    {
        if (row is null || !row.IsStackHeader)
        {
            return;
        }

        if (row.IsExpanded)
        {
            _expandedPasswordStackKeys.Remove(row.Key);
        }
        else
        {
            _expandedPasswordStackKeys.Add(row.Key);
        }

        RaiseFilteredPasswordRowsChanged();
    }

    [RelayCommand]
    private void TogglePasswordSelection(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.IsSelected = !entry.IsSelected;
    }

    [RelayCommand]
    private void TogglePasswordRowSelection(PasswordListRow? row)
    {
        if (row is null)
        {
            return;
        }

        if (row.IsStackHeader)
        {
            var nextValue = !row.IsGroupSelected;
            UpdatePasswordSelectionsInBatch(() =>
            {
                foreach (var member in row.Members)
                {
                    member.IsSelected = nextValue;
                }
            });
            return;
        }

        row.Entry.IsSelected = !row.Entry.IsSelected;
    }

    [RelayCommand]
    private void ClearPasswordSelection()
    {
        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in Passwords.Where(item => item.IsSelected))
            {
                entry.IsSelected = false;
            }
        });
        ReconcileSelectedPasswordDetails();
    }

    [RelayCommand]
    private void ClearPasswordSearch()
    {
        SetPasswordSearchImmediately("");
    }

    [RelayCommand]
    private void ClearPasswordFilters()
    {
        SetPasswordSearchImmediately("");
        QuickFilterFavorite = false;
        QuickFilter2Fa = false;
        QuickFilterNotes = false;
        QuickFilterPasskey = false;
        QuickFilterBoundNote = false;
        QuickFilterUncategorized = false;
        QuickFilterLocalOnly = false;
        QuickFilterAttachments = false;
        SelectedPasswordFolderFilter = PasswordFolderFilters.FirstOrDefault(item =>
            string.Equals(item.SelectionKey, "system:all", StringComparison.OrdinalIgnoreCase)) ??
            PasswordFolderFilters.FirstOrDefault();
        RefreshPasswordFilters();
        StatusMessage = _localization.Get("ClearedPasswordFilters");
    }

    private void SetPasswordSearchImmediately(string value)
    {
        CancelPasswordSearchDebounce();
        _isApplyingPasswordSearchImmediately = true;
        try
        {
            PasswordSearchText = value;
            PasswordSearchQuery = value;
        }
        finally
        {
            _isApplyingPasswordSearchImmediately = false;
        }

        RaisePasswordFilterState();
    }

    [RelayCommand]
    private void SetPasswordSort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        SelectedPasswordSort = value switch
        {
            "updated-desc" or "title-asc" or "website-asc" or "username-asc" or "created-desc" or "favorites-first" => value,
            _ => SelectedPasswordSort
        };
    }

    private void QueuePasswordSearchQuery(string value)
    {
        CancelPasswordSearchDebounce();
        if (_isUnlockedShellHibernated)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _passwordSearchDebounceCts = cts;
        _ = ApplyPasswordSearchQueryAsync(value, cts);
    }

    private async Task ApplyPasswordSearchQueryAsync(string value, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(250, cts.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ReferenceEquals(_passwordSearchDebounceCts, cts))
                {
                    PasswordSearchQuery = value;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_passwordSearchDebounceCts, cts))
            {
                _passwordSearchDebounceCts = null;
            }
            cts.Dispose();
        }
    }

    private void CancelPasswordSearchDebounce()
    {
        var cts = _passwordSearchDebounceCts;
        if (cts is null)
        {
            return;
        }

        _passwordSearchDebounceCts = null;
        cts.Cancel();
    }

}
