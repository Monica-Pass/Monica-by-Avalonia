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
        PublishPasswordSearchMatches(value, [], []);
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
        var dispatcher = _viewModelDispatcher;
        _passwordSearchDebounceCts = cts;
        _ = ApplyPasswordSearchQueryAsync(value, dispatcher, cts);
    }

    private async Task ApplyPasswordSearchQueryAsync(
        string value,
        Dispatcher dispatcher,
        CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(250, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            IReadOnlyList<long> customFieldMatches;
            IReadOnlyList<long> attachmentMatches;
            if (string.IsNullOrWhiteSpace(value))
            {
                customFieldMatches = [];
                attachmentMatches = [];
            }
            else
            {
                var metadataMatches = await Task.Run(
                    () => _repository.SearchPasswordMetadataAsync(value, cts.Token),
                    cts.Token);
                customFieldMatches = metadataMatches.CustomFieldMatchIds;
                attachmentMatches = metadataMatches.AttachmentMatchIds;
            }

            cts.Token.ThrowIfCancellationRequested();
            await PublishPasswordSearchQueryAsync(
                value,
                customFieldMatches,
                attachmentMatches,
                dispatcher,
                cts);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception) when (cts.Token.IsCancellationRequested || !_cryptoService.IsUnlocked)
        {
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Password custom-field search failed", ex);
            await PublishPasswordSearchQueryAsync(value, [], [], dispatcher, cts);
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

    private async Task PublishPasswordSearchQueryAsync(
        string query,
        IEnumerable<long> customFieldMatches,
        IEnumerable<long> attachmentMatches,
        Dispatcher dispatcher,
        CancellationTokenSource cts)
    {
        await dispatcher.InvokeAsync(() =>
        {
            if (!cts.Token.IsCancellationRequested &&
                ReferenceEquals(_passwordSearchDebounceCts, cts))
            {
                PublishPasswordSearchMatches(query, customFieldMatches, attachmentMatches);
                if (string.Equals(PasswordSearchQuery, query, StringComparison.Ordinal))
                {
                    RefreshPasswordFilters();
                }
                else
                {
                    PasswordSearchQuery = query;
                }
            }
        });
    }

    private void PublishPasswordSearchMatches(
        string query,
        IEnumerable<long> customFieldMatches,
        IEnumerable<long> attachmentMatches)
    {
        _passwordCustomFieldSearchQuery = query;
        _passwordCustomFieldSearchMatches = customFieldMatches.ToHashSet();
        _passwordAttachmentSearchQuery = query;
        _passwordAttachmentSearchMatches = attachmentMatches.ToHashSet();
    }

    private void ReconcilePasswordSearchQuery(string query)
    {
        if (!string.Equals(query, _passwordCustomFieldSearchQuery, StringComparison.Ordinal) ||
            !string.Equals(query, _passwordAttachmentSearchQuery, StringComparison.Ordinal))
        {
            PublishPasswordSearchMatches(query, [], []);
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
