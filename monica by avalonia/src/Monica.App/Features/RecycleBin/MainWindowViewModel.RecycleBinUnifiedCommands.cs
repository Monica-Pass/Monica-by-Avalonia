using CommunityToolkit.Mvvm.Input;
using Monica.App.Features.RecycleBin;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isCleaningExpiredRecycleBinItems;

    [RelayCommand]
    private void SetRecycleBinRetention(string? value)
    {
        if (int.TryParse(value, out var days) && days is 7 or 30 or 90 or 365)
        {
            RecycleBinRetentionDays = days;
        }
    }

    [RelayCommand]
    private async Task RestoreRecycleBinItemAsync(RecycleBinDisplayItem? item)
    {
        if (item is null) return;

        if (item.Password is not null)
        {
            await RestorePasswordAsync(item.Password);
        }
        else if (item.SecureItem is not null)
        {
            var secure = item.SecureItem;
            await _repository.RestoreSecureItemAsync(secure.Id);
            secure.IsDeleted = false;
            secure.DeletedAt = null;
            DeletedSecureItems.Remove(secure);
            await LogOperationAsync(new OperationLog
            {
                ItemType = secure.ItemType.ToString().ToUpperInvariant(),
                ItemId = secure.Id,
                ItemTitle = secure.Title,
                OperationType = "RESTORE",
                DeviceName = Environment.MachineName
            });
            StatusMessage = _localization.Format("RestoredRecycleBinItemFormat", secure.Title);
            RaiseRecycleBinCountStateForUnifiedItems();
        }

        SelectedRecycleBinItem = null;
        RecycleBinNarrowShowsList = true;
    }

    [RelayCommand]
    private async Task DeleteRecycleBinItemPermanentlyAsync(RecycleBinDisplayItem? item)
    {
        if (item is null || !await ConfirmPermanentDeleteAsync(item.Title)) return;

        await DeleteRecycleBinItemPermanentlyCoreAsync(item);
        SelectedRecycleBinItem = null;
        RecycleBinNarrowShowsList = true;
        RaiseRecycleBinCountStateForUnifiedItems();
        StatusMessage = _localization.Format("DeletedRecycleBinItemPermanentlyFormat", item.Title);
    }

    [RelayCommand]
    private void ShowRecycleBinItemDetails(RecycleBinDisplayItem? item)
    {
        if (item is null) return;
        SelectedRecycleBinItem = item;
        if (item.Password is not null) SelectedDeletedPassword = item.Password;
        RecycleBinNarrowShowsList = false;
    }

    private async Task DeleteRecycleBinItemPermanentlyCoreAsync(RecycleBinDisplayItem item)
    {
        if (item.Password is not null)
        {
            var siblings = GetDeletedPasswordSiblings(item.Password).ToList();
            foreach (var sibling in siblings)
            {
                await _repository.DeletePasswordPermanentlyAsync(sibling.Id);
                await LogOperationAsync(new OperationLog
                {
                    ItemType = "PASSWORD",
                    ItemId = sibling.Id,
                    ItemTitle = sibling.Title,
                    OperationType = "PURGE",
                    DeviceName = Environment.MachineName
                });
                DeletedPasswords.Remove(sibling);
            }
        }
        else if (item.SecureItem is not null)
        {
            var secure = item.SecureItem;
            await _repository.DeleteSecureItemPermanentlyAsync(secure.Id);
            await LogOperationAsync(new OperationLog
            {
                ItemType = secure.ItemType.ToString().ToUpperInvariant(),
                ItemId = secure.Id,
                ItemTitle = secure.Title,
                OperationType = "PURGE",
                DeviceName = Environment.MachineName
            });
            DeletedSecureItems.Remove(secure);
        }
    }

    private void RaiseRecycleBinCountStateForUnifiedItems()
    {
        RefreshRecycleBinCountState();
        OnPropertyChanged(nameof(HasSelectedDeletedPasswords));
        OnPropertyChanged(nameof(SelectedDeletedPasswordCount));
        OnPropertyChanged(nameof(SelectedDeletedPasswordCountText));
    }

    private void QueueExpiredRecycleBinCleanup()
    {
        if (!IsUnlocked || _isCleaningExpiredRecycleBinItems) return;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RecycleBinRetentionDays);
        if (RecycleBinItems.All(item => item.DeletedAt is null || item.DeletedAt > cutoff)) return;
        _ = CleanupExpiredRecycleBinItemsAsync(cutoff);
    }

    private async Task CleanupExpiredRecycleBinItemsAsync(DateTimeOffset cutoff)
    {
        _isCleaningExpiredRecycleBinItems = true;
        try
        {
            var expired = RecycleBinItems
                .Where(item => item.DeletedAt is not null && item.DeletedAt <= cutoff)
                .ToArray();
            foreach (var item in expired)
            {
                await DeleteRecycleBinItemPermanentlyCoreAsync(item);
            }

            if (expired.Length > 0)
            {
                RaiseRecycleBinCountStateForUnifiedItems();
                StatusMessage = _localization.Format("RecycleBinAutoCleanedFormat", expired.Length);
            }
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Recycle bin automatic cleanup failed", ex);
            StatusMessage = _localization.Get("RecycleBinAutoCleanupFailed");
        }
        finally
        {
            _isCleaningExpiredRecycleBinItems = false;
        }
    }
}
