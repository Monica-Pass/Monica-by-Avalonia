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
    private async Task AddPasswordAsync()
    {
        var initialPassword = string.IsNullOrWhiteSpace(GeneratedPassword) ? "" : GeneratedPassword;
        var editor = await _passwordEditorDialogService.ShowAsync(
            null,
            Categories.ToList(),
            initialPassword,
            notes: NoteItems.ToList());
        if (editor is null)
        {
            return;
        }

        var entries = editor
            .BuildEntries(ProtectPasswords(editor.GetPasswordRows()))
            .ToList();
        foreach (var entry in entries)
        {
            await _repository.SavePasswordAsync(entry);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = entry.Id,
                ItemTitle = entry.Title,
                OperationType = "CREATE",
                DeviceName = Environment.MachineName
            });
        }

        var customFields = BindCustomFields(entries[0].Id, editor.GetCustomFields());
        await _repository.ReplaceCustomFieldsAsync(entries[0].Id, customFields);
        SetPasswordCustomFields(entries[0].Id, customFields);
        foreach (var entry in entries)
        {
            RefreshPasswordTotpDisplay(entry);
        }

        await SynchronizeBoundTotpAsync(entries[0]);
        ReplacePasswordGroup([], entries);
        RefreshBoundTotpPresentation(entries);
        InvalidateSecurityAnalysis();
        RaiseCounts();
        RaiseFilteredPasswordsChanged();
        StatusMessage = _localization.Format("CreatedPasswordFormat", entries[0].Title);
    }

    [RelayCommand]
    private async Task EditPasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetPasswordSiblings(entry).ToList();
        var customFields = await GetGroupCustomFieldsAsync(entry, siblings);
        var editor = await _passwordEditorDialogService.ShowAsync(
            entry,
            Categories.ToList(),
            UnprotectPassword(entry.Password),
            siblings.Select(item => UnprotectPassword(item.Password)).ToArray(),
            NoteItems.ToList(),
            customFields);
        if (editor is null)
        {
            return;
        }

        var passwordRows = editor.GetPasswordRows();
        var storedPasswords = ProtectPasswords(passwordRows);
        var updatedEntries = new List<PasswordEntry>();
        for (var index = 0; index < storedPasswords.Count; index++)
        {
            var source = index < siblings.Count ? siblings[index] : null;
            var oldPlainPassword = source is null ? "" : UnprotectPassword(source.Password);
            var updated = editor.BuildEntryFrom(source, storedPasswords[index]);
            await _repository.SavePasswordAsync(updated);
            await SavePasswordHistorySnapshotIfChangedAsync(updated.Id, oldPlainPassword, passwordRows[index]);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = updated.Id,
                ItemTitle = updated.Title,
                OperationType = source is null ? "CREATE" : "UPDATE",
                DeviceName = Environment.MachineName
            });
            updatedEntries.Add(updated);
        }

        foreach (var removed in siblings.Skip(storedPasswords.Count))
        {
            await _repository.SoftDeletePasswordAsync(removed.Id);
        }

        var updatedCustomFields = BindCustomFields(updatedEntries[0].Id, editor.GetCustomFields());
        await _repository.ReplaceCustomFieldsAsync(updatedEntries[0].Id, updatedCustomFields);
        SetPasswordCustomFields(updatedEntries[0].Id, updatedCustomFields);
        foreach (var updated in updatedEntries)
        {
            RefreshPasswordTotpDisplay(updated);
        }

        await SynchronizeBoundTotpAsync(updatedEntries[0]);
        ReplacePasswordGroup(siblings, updatedEntries);
        RefreshBoundTotpPresentation(siblings.Concat(updatedEntries));
        InvalidateSecurityAnalysis();
        RaiseCounts();
        RaiseFilteredPasswordsChanged();
        StatusMessage = _localization.Format("UpdatedPasswordFormat", updatedEntries[0].Title);
    }

    [RelayCommand]
    private async Task CopyPasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var text = entry.Password;
        if (_cryptoService.IsUnlocked)
        {
            try
            {
                text = _cryptoService.DecryptString(entry.Password);
            }
            catch
            {
                text = entry.Password;
            }
        }

        await _clipboardService.SetSensitiveTextAsync(text);
        StatusMessage = _localization.Format("CopiedPasswordFormat", entry.Title);
    }

    [RelayCommand]
    private async Task CopyUsernameAsync(PasswordEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Username))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(entry.Username);
        StatusMessage = _localization.Format("CopiedUsernameFormat", entry.Title);
    }

    [RelayCommand]
    private async Task CopyWebsiteAsync(PasswordEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Website))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(entry.Website);
        StatusMessage = _localization.Format("CopiedWebsiteFormat", entry.Title);
    }

    [RelayCommand]
    private async Task ShowPasswordDetailsAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        await RecordPasswordQuickAccessAsync(entry);
        var siblings = GetPasswordDetailSiblings(entry);
        var customFields = await GetGroupCustomFieldsAsync(entry, siblings);
        var category = entry.CategoryId is null
            ? null
            : Categories.FirstOrDefault(item => item.Id == entry.CategoryId);
        var boundNote = entry.BoundNoteId is null
            ? null
            : NoteItems.FirstOrDefault(item => item.Id == entry.BoundNoteId);
        var attachments = GetGroupAttachments(entry, siblings);
        var history = await GetPasswordHistoryDisplayItemsAsync(entry.Id);

        await _passwordDetailDialogService.ShowAsync(
            entry,
            siblings,
            category,
            boundNote,
            attachments,
            customFields,
            history,
            AddPasswordAttachmentAsync,
            DeletePasswordAttachmentAsync,
            DeletePasswordHistoryAsync,
            ClearPasswordHistoryAsync);
    }

    private void QueueSelectedPasswordDetailsRefresh(PasswordEntry? entry)
    {
        var version = Interlocked.Increment(ref _selectedPasswordDetailsVersion);
        CancelSelectedPasswordDetailsRefresh();
        IsLoadingSelectedPasswordDetails = false;
        SelectedPasswordDetailsError = null;
        OnPropertyChanged(nameof(HasCurrentSelectedPasswordDetails));

        if (entry is null)
        {
            SelectedPasswordDetails = null;
            return;
        }

        if (SelectedPasswordDetails?.Entry.Id == entry.Id)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _selectedPasswordDetailsCts = cts;
        AppDiagnostics.Info($"Password selection changed. id={entry.Id}, version={version}");
        _ = RefreshSelectedPasswordDetailsDeferredAsync(entry, version, cts);
    }

    private async Task RefreshSelectedPasswordDetailsDeferredAsync(PasswordEntry entry, int version, CancellationTokenSource cts)
    {
        var stopwatch = Stopwatch.StartNew();
        var cancellationToken = cts.Token;
        try
        {
            _ = ShowSelectedPasswordLoadingDeferredAsync(entry.Id, version, cancellationToken);
            await Task.Delay(SelectedPasswordDetailsCoalesceDelay, cancellationToken).ConfigureAwait(false);
            var sourceSnapshot = await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (cancellationToken.IsCancellationRequested ||
                        !IsCurrentSelectedPasswordDetailsRequest(version) ||
                        SelectedPassword?.Id != entry.Id)
                    {
                        return null;
                    }

                    var snapshotStopwatch = Stopwatch.StartNew();
                    var snapshot = BuildPasswordDetailSourceSnapshot(entry);
                    AppDiagnostics.Info(
                        $"Build selected password detail source snapshot completed in {snapshotStopwatch.ElapsedMilliseconds} ms. " +
                        $"id={entry.Id}, version={version}, candidates={snapshot.SiblingCandidates.Count}, " +
                        $"categories={snapshot.Categories.Count}, notes={snapshot.NoteItems.Count}");
                    return snapshot;
                },
                DispatcherPriority.ApplicationIdle);
            if (sourceSnapshot is null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var details = await AppDiagnostics.MeasureAsync(
                $"Build selected password details VM id={entry.Id}",
                () => Task.Run(
                    () =>
                    {
                        var snapshot = BuildCachedPasswordDetailSnapshot(sourceSnapshot);
                        AppDiagnostics.Info(
                            $"Build selected password detail payload ready. id={entry.Id}, version={version}, " +
                            $"siblings={snapshot.Siblings.Count}, attachments={snapshot.Attachments.Count}, " +
                            $"customFields={snapshot.CustomFields.Count}, history={snapshot.History.Count}");
                        return CreatePasswordDetailViewModel(snapshot);
                    },
                    cancellationToken));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested ||
                    !IsCurrentSelectedPasswordDetailsRequest(version) ||
                    SelectedPassword?.Id != entry.Id)
                {
                    return;
                }

                SelectedPasswordDetails = details;
                IsLoadingSelectedPasswordDetails = false;
                AppDiagnostics.Info($"Password selection fast details applied in {stopwatch.ElapsedMilliseconds} ms. id={entry.Id}, version={version}");
                Dispatcher.UIThread.Post(
                    () => AppDiagnostics.Info($"Password selection details UI idle after {stopwatch.ElapsedMilliseconds} ms. id={entry.Id}, version={version}"),
                    DispatcherPriority.ApplicationIdle);
                _ = LoadSelectedPasswordHistoryDeferredAsync(entry.Id, version, details);
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
            AppDiagnostics.Info($"Password selection details cancelled after {stopwatch.ElapsedMilliseconds} ms. id={entry.Id}, version={version}");
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsCurrentSelectedPasswordDetailsRequest(version))
                {
                    IsLoadingSelectedPasswordDetails = false;
                    SelectedPasswordDetailsError = _localization.Format("PasswordDetailsLoadFailedFormat", ex.Message);
                    StatusMessage = SelectedPasswordDetailsError;
                }
            }, DispatcherPriority.Background);
            AppDiagnostics.Error($"Password selection details failed after {stopwatch.ElapsedMilliseconds} ms. id={entry.Id}, version={version}", ex);
        }
        finally
        {
            if (ReferenceEquals(_selectedPasswordDetailsCts, cts))
            {
                _selectedPasswordDetailsCts = null;
            }

            cts.Dispose();
        }
    }

    private async Task ShowSelectedPasswordLoadingDeferredAsync(long entryId, int version, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SelectedPasswordDetailsLoadingDelay, cancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested ||
                    !IsCurrentSelectedPasswordDetailsRequest(version) ||
                    SelectedPassword?.Id != entryId ||
                    SelectedPasswordDetails?.Entry.Id == entryId)
                {
                    return;
                }

                IsLoadingSelectedPasswordDetails = true;
                AppDiagnostics.Info(
                    $"Password selection loading state shown after {SelectedPasswordDetailsLoadingDelay.TotalMilliseconds:0} ms. " +
                    $"id={entryId}, version={version}");
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadSelectedPasswordHistoryDeferredAsync(long entryId, int version, PasswordDetailViewModel details)
    {
        try
        {
            var history = await AppDiagnostics.MeasureAsync(
                $"Load selected password history id={entryId}",
                async () => await Task.Run(async () => await GetPasswordHistoryDisplayItemsAsync(entryId)));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsCurrentSelectedPasswordDetailsRequest(version) &&
                    SelectedPassword?.Id == entryId &&
                    ReferenceEquals(SelectedPasswordDetails, details))
                {
                    details.SetPasswordHistory(history);
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error($"Load selected password history failed. id={entryId}, version={version}", ex);
        }
    }

    private bool IsCurrentSelectedPasswordDetailsRequest(int version) =>
        Volatile.Read(ref _selectedPasswordDetailsVersion) == version;

    private void CancelSelectedPasswordDetailsRefresh()
    {
        var cts = _selectedPasswordDetailsCts;
        if (cts is null)
        {
            return;
        }

        _selectedPasswordDetailsCts = null;
        cts.Cancel();
    }

    [RelayCommand]
    private void RetrySelectedPasswordDetails()
    {
        var entry = SelectedPassword;
        if (entry is null)
        {
            return;
        }

        SelectedPasswordDetails = null;
        QueueSelectedPasswordDetailsRefresh(entry);
    }

    [RelayCommand]
    private void CloseSelectedPasswordDetails()
    {
        SelectedPassword = null;
        SelectedPasswordDetailsError = null;
    }

    private async Task<PasswordDetailViewModel> BuildPasswordDetailViewModelAsync(
        PasswordEntry entry,
        bool includeHistory = true,
        bool allowCustomFieldRepositoryFallback = true)
    {
        var siblings = GetPasswordDetailSiblings(entry);
        var customFields = allowCustomFieldRepositoryFallback
            ? await GetGroupCustomFieldsAsync(entry, siblings)
            : GetCachedGroupCustomFields(entry, siblings);
        var category = entry.CategoryId is null
            ? null
            : Categories.FirstOrDefault(item => item.Id == entry.CategoryId);
        var boundNote = entry.BoundNoteId is null
            ? null
            : NoteItems.FirstOrDefault(item => item.Id == entry.BoundNoteId);
        var attachments = GetGroupAttachments(entry, siblings);
        var history = includeHistory
            ? await GetPasswordHistoryDisplayItemsAsync(entry.Id)
            : [];

        return new PasswordDetailViewModel(
            _localization,
            _clipboardService,
            _cryptoService,
            _totpService,
            entry,
            siblings,
            category,
            boundNote,
            attachments,
            customFields,
            history,
            AddPasswordAttachmentAsync,
            DeletePasswordAttachmentAsync,
            DeletePasswordHistoryAsync,
            ClearPasswordHistoryAsync);
    }

    private PasswordDetailSourceSnapshot BuildPasswordDetailSourceSnapshot(PasswordEntry entry)
    {
        var candidates = entry.IsDeleted
            ? DeletedPasswords.ToArray()
            : entry.IsArchived
                ? ArchivedPasswords.ToArray()
                : Passwords.ToArray();

        return new PasswordDetailSourceSnapshot(
            entry,
            candidates,
            Categories.ToArray(),
            NoteItems.ToArray(),
            _passwordAttachments,
            _passwordCustomFields);
    }

    private PasswordDetailSnapshot BuildCachedPasswordDetailSnapshot(PasswordDetailSourceSnapshot source)
    {
        var entry = source.Entry;
        var siblings = GetPasswordDetailSiblings(entry, source.SiblingCandidates).ToArray();
        var category = entry.CategoryId is null
            ? null
            : source.Categories.FirstOrDefault(item => item.Id == entry.CategoryId);
        var boundNote = entry.BoundNoteId is null
            ? null
            : source.NoteItems.FirstOrDefault(item => item.Id == entry.BoundNoteId);

        return new PasswordDetailSnapshot(
            entry,
            siblings,
            category,
            boundNote,
            GetGroupAttachments(entry, siblings, source.PasswordAttachments),
            GetCachedGroupCustomFields(entry, siblings, source.PasswordCustomFields),
            []);
    }

    private PasswordDetailViewModel CreatePasswordDetailViewModel(PasswordDetailSnapshot snapshot) =>
        new(
            _localization,
            _clipboardService,
            _cryptoService,
            _totpService,
            snapshot.Entry,
            snapshot.Siblings,
            snapshot.Category,
            snapshot.BoundNote,
            snapshot.Attachments,
            snapshot.CustomFields,
            snapshot.History,
            AddPasswordAttachmentAsync,
            DeletePasswordAttachmentAsync,
            DeletePasswordHistoryAsync,
            ClearPasswordHistoryAsync);

    private IReadOnlyList<PasswordEntry> GetPasswordDetailSiblings(PasswordEntry entry)
    {
        var candidates = entry.IsDeleted
            ? DeletedPasswords.ToArray()
            : entry.IsArchived
                ? ArchivedPasswords.ToArray()
                : Passwords.ToArray();
        return GetPasswordDetailSiblings(entry, candidates).ToArray();
    }

    private static IEnumerable<PasswordEntry> GetPasswordDetailSiblings(PasswordEntry entry, IReadOnlyList<PasswordEntry> candidates)
    {
        var key = BuildSiblingGroupKey(entry);
        return candidates
            .Where(item => BuildSiblingGroupKey(item) == key)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id);
    }

    [RelayCommand]
    private async Task OpenQuickAccessPasswordAsync(PasswordQuickAccessItem? item)
    {
        if (item is null)
        {
            return;
        }

        await ShowPasswordDetailsAsync(item.Entry);
    }
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

    [RelayCommand]
    private async Task FavoriteSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        foreach (var entry in selected)
        {
            if (!entry.IsFavorite)
            {
                entry.IsFavorite = true;
                await _repository.SavePasswordAsync(entry);
                await LogOperationAsync(new OperationLog
                {
                    ItemType = "PASSWORD",
                    ItemId = entry.Id,
                    ItemTitle = entry.Title,
                    OperationType = "FAVORITE",
                    DeviceName = Environment.MachineName
                });
            }
        }

        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        RaiseFilteredPasswordsChanged();
        InvalidateSecurityAnalysis();
        StatusMessage = _localization.Format("FavoritedPasswordCountFormat", selected.Length);
    }

    [RelayCommand]
    private async Task DeleteSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        if (!await _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteSelectedPasswordsConfirmationTitle"),
            _localization.Format("DeleteSelectedPasswordsConfirmationMessageFormat", selected.Length),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel))
        {
            return;
        }

        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        var handled = new HashSet<long>();
        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
            }

            await DeletePasswordGroupAsync(entry, siblings, updateStatus: false);
        }

        RefreshPasswordSelectionStateFromPasswords();
        StatusMessage = _localization.Format("MovedSelectedPasswordsToRecycleBinFormat", selected.Length);
    }

    [RelayCommand]
    private async Task ArchiveSelectedPasswordsAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var handled = new HashSet<long>();
        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
            }

            await ArchivePasswordGroupAsync(entry, siblings, updateStatus: false);
        }

        RefreshPasswordSelectionStateFromPasswords();
        StatusMessage = _localization.Format("ArchivedSelectedPasswordsFormat", selected.Length);
    }

    [RelayCommand]
    private async Task MoveSelectedPasswordsToCategoryAsync()
    {
        var selected = Passwords.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        var currentCategoryId = selected
            .Select(item => item.CategoryId)
            .Distinct()
            .Count() == 1
                ? selected[0].CategoryId
                : null;
        var choice = await _categoryPickerDialogService.ShowAsync(Categories.ToList(), currentCategoryId);
        if (choice is null)
        {
            return;
        }

        var handled = new HashSet<long>();
        foreach (var entry in selected)
        {
            if (!handled.Add(entry.Id))
            {
                continue;
            }

            var siblings = GetPasswordSiblings(entry).ToArray();
            foreach (var sibling in siblings)
            {
                handled.Add(sibling.Id);
                sibling.CategoryId = choice.Id;
                await _repository.SavePasswordAsync(sibling);
                await SynchronizeBoundTotpAsync(sibling);
                await LogOperationAsync(new OperationLog
                {
                    ItemType = "PASSWORD",
                    ItemId = sibling.Id,
                    ItemTitle = sibling.Title,
                    OperationType = "MOVE_CATEGORY",
                    DeviceName = Environment.MachineName
                });
            }
        }

        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        RefreshBoundTotpPresentation(selected);
        RefreshPasswordFolderFilters(choice.Id);
        RaiseFilteredPasswordsChanged();
        StatusMessage = _localization.Format("MovedSelectedPasswordsToFolderFormat", selected.Length, choice.Name);
    }
    [RelayCommand]
    private async Task StackSelectedPasswordsAsync()
    {
        var selected = Passwords
            .Where(item => item.IsSelected)
            .OrderBy(item => item.Id == 0 ? long.MaxValue : item.Id)
            .ToArray();
        if (selected.Length < 2)
        {
            return;
        }

        var replicaGroupId = selected
            .Select(item => item.ReplicaGroupId)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? $"manual-{Guid.NewGuid():N}";
        UpdatePasswordSelectionsInBatch(() =>
        {
            foreach (var entry in selected)
            {
                entry.IsSelected = false;
            }
        });

        foreach (var entry in selected)
        {
            entry.ReplicaGroupId = replicaGroupId;
            await _repository.SavePasswordAsync(entry);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = entry.Id,
                ItemTitle = entry.Title,
                OperationType = "STACK",
                DeviceName = Environment.MachineName
            });
        }

        RaiseFilteredPasswordsChanged();
        InvalidateSecurityAnalysis();
        StatusMessage = _localization.Format("StackedPasswordCountFormat", selected.Length);
    }

    [RelayCommand]
    private async Task CopyPasswordTotpAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        RefreshPasswordTotpDisplay(entry);
        await _clipboardService.SetSensitiveTextAsync(entry.TotpCode);
        StatusMessage = _localization.Format("CopiedTotpFormat", entry.Title);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        entry.IsFavorite = !entry.IsFavorite;
        await _repository.SavePasswordAsync(entry);
        await LogOperationAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = entry.Id,
            ItemTitle = entry.Title,
            OperationType = "FAVORITE",
            DeviceName = Environment.MachineName
        });
        InvalidateSecurityAnalysis();
        RaiseFilteredPasswordsChanged();
    }

    [RelayCommand]
    private async Task DeletePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (!await _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeletePasswordConfirmationTitle"),
            _localization.Format("DeletePasswordConfirmationMessageFormat", entry.Title),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel))
        {
            return;
        }

        var siblings = entry.IsArchived
            ? GetArchivedPasswordSiblings(entry).ToList()
            : GetPasswordSiblings(entry).ToList();
        await DeletePasswordGroupAsync(entry, siblings, updateStatus: true);
    }

    private Task<bool> ConfirmMoveItemToRecycleBinAsync(string itemTitle) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteItemConfirmationTitle"),
            _localization.Format("DeleteItemConfirmationMessageFormat", itemTitle),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel);

    private Task<bool> ConfirmMoveSelectedItemsToRecycleBinAsync(int count) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteSelectedItemsConfirmationTitle"),
            _localization.Format("DeleteSelectedItemsConfirmationMessageFormat", count),
            _localization.Get("MoveToRecycleBin"),
            _localization.Cancel);

    private Task<bool> ConfirmDeleteFolderAsync(string name, int affectedPasswordCount) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteFolderConfirmationTitle"),
            _localization.Format("DeleteFolderConfirmationMessageFormat", name, affectedPasswordCount),
            _localization.Get("DeleteFolder"),
            _localization.Cancel);

    private Task<bool> ConfirmDeleteAttachmentAsync(string fileName) =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeleteAttachmentConfirmationTitle"),
            _localization.Format("DeleteAttachmentConfirmationMessageFormat", fileName),
            _localization.Get("Delete"),
            _localization.Cancel);

    private Task<bool> ConfirmDeletePasswordHistoryAsync() =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("DeletePasswordHistoryConfirmationTitle"),
            _localization.Get("DeletePasswordHistoryConfirmationMessage"),
            _localization.Get("Delete"),
            _localization.Cancel);

    private Task<bool> ConfirmClearPasswordHistoryAsync() =>
        _confirmationDialogService.ConfirmAsync(
            _localization.Get("ClearPasswordHistoryConfirmationTitle"),
            _localization.Get("ClearPasswordHistoryConfirmationMessage"),
            _localization.Get("ClearPasswordHistory"),
            _localization.Cancel);

    [RelayCommand]
    private async Task ArchivePasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetPasswordSiblings(entry).ToList();
        await ArchivePasswordGroupAsync(entry, siblings, updateStatus: true);
    }

    private async Task ArchivePasswordGroupAsync(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings, bool updateStatus)
    {
        foreach (var item in siblings)
        {
            item.IsArchived = true;
            item.ArchivedAt = DateTimeOffset.UtcNow;
            item.IsSelected = false;
            await _repository.SavePasswordAsync(item);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "ARCHIVE",
                DeviceName = Environment.MachineName
            });
            Passwords.Remove(item);
            var current = Passwords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                current.IsSelected = false;
                Passwords.Remove(current);
            }

            TrackPasswordSelection(item);
            ArchivedPasswords.Insert(0, item);
        }

        RefreshBoundTotpPresentation(siblings);
        RaiseCounts();
        RefreshPasswordSelectionStateFromPasswords();
        RaiseFilteredPasswordsChanged();
        InvalidateSecurityAnalysis();
        if (updateStatus)
        {
            StatusMessage = _localization.Format("ArchivedPasswordFormat", entry.Title);
        }
    }

    private async Task DeletePasswordGroupAsync(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings, bool updateStatus)
    {
        foreach (var item in siblings)
        {
            await _repository.SoftDeletePasswordAsync(item.Id);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = item.Id,
                ItemTitle = item.Title,
                OperationType = "DELETE",
                DeviceName = Environment.MachineName
            });
            item.IsSelected = false;
            Passwords.Remove(item);
            var current = Passwords.FirstOrDefault(password => password.Id == item.Id);
            if (current is not null)
            {
                current.IsSelected = false;
                Passwords.Remove(current);
            }

            ArchivedPasswords.Remove(item);
            var archived = ArchivedPasswords.FirstOrDefault(password => password.Id == item.Id);
            if (archived is not null)
            {
                archived.IsSelected = false;
                ArchivedPasswords.Remove(archived);
            }

            item.IsDeleted = true;
            item.DeletedAt = DateTimeOffset.UtcNow;
            item.IsArchived = false;
            item.ArchivedAt = null;
            TrackPasswordSelection(item);
            DeletedPasswords.Insert(0, item);
        }

        RefreshBoundTotpPresentation(siblings);
        RaiseCounts();
        RefreshPasswordSelectionStateFromPasswords();
        RaiseFilteredPasswordsChanged();
        InvalidateSecurityAnalysis();
        if (updateStatus)
        {
            StatusMessage = _localization.Format("MovedToRecycleBinFormat", entry.Title);
        }
    }

}
