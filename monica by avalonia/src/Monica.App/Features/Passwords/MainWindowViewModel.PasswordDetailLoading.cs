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
        var attachments = await GetGroupAttachmentsAsync(entry, siblings);
        var history = await GetPasswordHistoryDisplayItemsAsync(entry.Id);

        await _passwordDetailDialogService.ShowAsync(
            entry,
            siblings,
            category,
            boundNote,
            attachments,
            customFields,
            history,
            TryAddPasswordAttachmentAsync,
            SavePasswordAttachmentAsync,
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

        if (_isUnlockedShellHibernated)
        {
            SelectedPasswordDetails = null;
            return;
        }

        if (SelectedPasswordDetails?.Entry.Id == entry.Id)
        {
            return;
        }

        var dispatcher = _viewModelDispatcher;
        var cts = new CancellationTokenSource();
        _selectedPasswordDetailsCts = cts;
        AppDiagnostics.Info($"Password selection changed. id={entry.Id}, version={version}");
        _ = RefreshSelectedPasswordDetailsDeferredAsync(entry, version, dispatcher, cts);
    }

    private async Task RefreshSelectedPasswordDetailsDeferredAsync(
        PasswordEntry entry,
        int version,
        Dispatcher dispatcher,
        CancellationTokenSource cts)
    {
        var stopwatch = Stopwatch.StartNew();
        var cancellationToken = cts.Token;
        try
        {
            _ = ShowSelectedPasswordLoadingDeferredAsync(
                entry.Id,
                version,
                dispatcher,
                cancellationToken);
            await Task.Delay(SelectedPasswordDetailsCoalesceDelay, cancellationToken).ConfigureAwait(false);
            // Snapshot construction is part of the restore contract. Keep it
            // ahead of idle-only work so a busy workspace cannot starve it.
            var sourceSnapshot = await dispatcher.InvokeAsync(
                () =>
                {
                    if (!CanReadVault(cancellationToken) ||
                        !IsCurrentSelectedPasswordDetailsRequest(version) ||
                        SelectedPassword?.Id != entry.Id)
                    {
                        return null;
                    }

                    var snapshotStopwatch = Stopwatch.StartNew();
                    var snapshot = BuildPasswordDetailSourceSnapshot(entry);
                    AppDiagnostics.Info(
                        $"Build selected password detail source snapshot completed in {snapshotStopwatch.ElapsedMilliseconds} ms. " +
                        $"id={entry.Id}, version={version}, siblings={snapshot.Siblings.Count}");
                    return snapshot;
                },
                DispatcherPriority.Background);
            if (sourceSnapshot is null)
            {
                return;
            }

            if (!CanReadVault(cancellationToken))
            {
                return;
            }

            var snapshot = await AppDiagnostics.MeasureAsync(
                $"Load selected password detail data id={entry.Id}",
                () => BuildPasswordDetailSnapshotAsync(sourceSnapshot, cancellationToken));
            if (!CanReadVault(cancellationToken))
            {
                return;
            }

            var details = await AppDiagnostics.MeasureAsync(
                $"Build selected password details VM id={entry.Id}",
                () => Task.Run(
                    () =>
                    {
                        AppDiagnostics.Info(
                            $"Build selected password detail payload ready. id={entry.Id}, version={version}, " +
                            $"siblings={snapshot.Siblings.Count}, attachments={snapshot.Attachments.Count}, " +
                            $"customFields={snapshot.CustomFields.Count}, history={snapshot.History.Count}");
                        return CreatePasswordDetailViewModel(snapshot);
                    },
                    cancellationToken));
            var ownershipTransferred = false;
            try
            {
                await dispatcher.InvokeAsync(() =>
                {
                    if (!CanReadVault(cancellationToken) ||
                        !IsCurrentSelectedPasswordDetailsRequest(version) ||
                        SelectedPassword?.Id != entry.Id)
                    {
                        return;
                    }

                    SelectedPasswordDetails = details;
                    ownershipTransferred = true;
                    IsLoadingSelectedPasswordDetails = false;
                    AppDiagnostics.Info($"Password selection fast details applied in {stopwatch.ElapsedMilliseconds} ms. id={entry.Id}, version={version}");
                    dispatcher.Post(
                        () => AppDiagnostics.Info($"Password selection details UI idle after {stopwatch.ElapsedMilliseconds} ms. id={entry.Id}, version={version}"),
                        DispatcherPriority.ApplicationIdle);
                    _ = LoadSelectedPasswordHistoryDeferredAsync(
                        entry.Id,
                        version,
                        details,
                        dispatcher,
                        cancellationToken);
                }, DispatcherPriority.Background);
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    details.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            AppDiagnostics.Info($"Password selection details cancelled after {stopwatch.ElapsedMilliseconds} ms. id={entry.Id}, version={version}");
        }
        catch (Exception) when (!CanReadVault(cancellationToken))
        {
            AppDiagnostics.Info($"Password selection details stopped at the vault boundary after {stopwatch.ElapsedMilliseconds} ms. id={entry.Id}, version={version}");
        }
        catch (Exception ex)
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (CanReadVault(cancellationToken) &&
                    IsCurrentSelectedPasswordDetailsRequest(version))
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

    private async Task ShowSelectedPasswordLoadingDeferredAsync(
        long entryId,
        int version,
        Dispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(SelectedPasswordDetailsLoadingDelay, cancellationToken).ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
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

    private bool CanReadVault(CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested && _cryptoService.IsUnlocked;

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

}
