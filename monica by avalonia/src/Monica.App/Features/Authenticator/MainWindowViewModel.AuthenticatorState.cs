using System.ComponentModel;
using Monica.App;
using Monica.App.Services;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly ITotpService _totpService;
    private readonly ITotpEditorDialogService _totpEditorDialogService;

    public void RefreshTotpDisplay(SecureItem item)
    {
        var data = TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            item.TotpCode = "------";
            item.TotpTimeRemaining = "";
            item.TotpProgress = 0;
            return;
        }

        item.TotpCode = _totpService.GenerateCode(data.Secret, data.Period, data.Digits, data.OtpType, data.Counter);
        item.TotpTimeRemaining = $"{_totpService.GetRemainingSeconds(data.Period)}s";
        item.TotpProgress = _totpService.GetProgress(data.Period);
    }

    private void RaiseTotpSelectionState()
    {
        OnPropertyChanged(nameof(SelectedTotpCount));
        OnPropertyChanged(nameof(SelectedTotpCountText));
        OnPropertyChanged(nameof(HasSelectedTotpItems));
    }

    private void RaiseTotpFilterState(bool reconcileSelection = true)
    {
        RefreshTotpFilterChoices();
        OnPropertyChanged(nameof(FilteredTotpItems));
        OnPropertyChanged(nameof(HasFilteredTotpItems));
        OnPropertyChanged(nameof(HasTotpFilterOrSearch));
        OnPropertyChanged(nameof(TotpExpiringSoonCount));
        OnPropertyChanged(nameof(TotpConsoleStatusText));
        OnPropertyChanged(nameof(TotpFilteredStatusText));
        OnPropertyChanged(nameof(TotpEmptyStateText));

        if (reconcileSelection)
        {
            ReconcileSelectedTotpItem();
        }
    }

    private void RefreshTotpFilterChoices()
    {
        var choices = new List<TotpFilterChoice>
        {
            BuildTotpFilterChoice(TotpFilterAll, _localization.Get("TotpFilterAll"), TotpItems.Count),
            BuildTotpFilterChoice(TotpFilterFavorites, _localization.Get("QuickFilterFavorite"), TotpItems.Count(item => item.IsFavorite)),
            BuildTotpFilterChoice(TotpFilterExpiringSoon, _localization.Get("TotpFilterExpiringSoon"), TotpItems.Count(IsTotpExpiringSoon)),
            BuildTotpFilterChoice(TotpFilterUnbound, _localization.Get("TotpFilterUnbound"), TotpItems.Count(item => item.BoundPasswordId is null))
        };

        var issuerChoices = TotpItems
            .GroupBy(ResolveTotpIssuer, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Take(8)
            .Select(group => BuildTotpFilterChoice($"{TotpFilterIssuerPrefix}{group.Key}", group.Key, group.Count(), level: 1));

        choices.AddRange(issuerChoices);
        ReplaceItems(TotpFilterChoices, choices);
    }

    private TotpFilterChoice BuildTotpFilterChoice(string key, string label, int count, int level = 0) =>
        new(key, label, count, level, string.Equals(SelectedTotpFilterKey, key, StringComparison.OrdinalIgnoreCase));

    private void ReconcileSelectedTotpItem()
    {
        var visibleItems = FilteredTotpItems;
        SelectedTotpItem =
            visibleItems.FirstOrDefault(item => item.Id == SelectedTotpItem?.Id) ??
            visibleItems.FirstOrDefault();
    }

    private void TrackTotpSelection(SecureItem item)
    {
        item.PropertyChanged -= SecureItemPropertyChanged;
        item.PropertyChanged += SecureItemPropertyChanged;
    }

    private void SecureItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecureItem.IsSelected))
        {
            RaiseTotpSelectionState();
        }
    }

    private async Task LoadTotpItemsAsync(IReadOnlyList<SecureItem>? preloadedTotps = null)
    {
        var selectedId = SelectedTotpItem?.Id;
        var storedTotps = preloadedTotps ?? await AppDiagnostics.MeasureAsync(
            "Load TOTP secure items",
            () => _repository.GetSecureItemsAsync(VaultItemType.Totp));
        var activePasswordIds = Passwords.Select(item => item.Id).ToHashSet();
        var seenVirtualPasswordIds = new HashSet<long>();
        var nextItems = new List<SecureItem>();

        foreach (var item in storedTotps)
        {
            if (item.BoundPasswordId is { } boundPasswordId && !activePasswordIds.Contains(boundPasswordId))
            {
                continue;
            }

            TrackTotpSelection(item);
            RefreshTotpDisplay(item);
            nextItems.Add(item);
            if (item.BoundPasswordId is { } passwordId)
            {
                seenVirtualPasswordIds.Add(passwordId);
            }
        }

        foreach (var password in Passwords.Where(item => item.HasAuthenticator && !seenVirtualPasswordIds.Contains(item.Id)))
        {
            var virtualItem = BuildVirtualTotpItem(password);
            TrackTotpSelection(virtualItem);
            RefreshTotpDisplay(virtualItem);
            nextItems.Add(virtualItem);
        }

        ReplaceItems(TotpItems, nextItems);
        SelectedTotpItem = nextItems.FirstOrDefault(item => item.Id == selectedId)
            ?? nextItems.FirstOrDefault();
        OnPropertyChanged(nameof(HasTotpItems));
        RaiseTotpFilterState();
        RaiseTotpSelectionState();
    }

    private async Task SetTotpFavoriteAsync(SecureItem item, bool isFavorite)
    {
        item.IsFavorite = isFavorite;
        if (item.BoundPasswordId is { } passwordId)
        {
            var password = Passwords.FirstOrDefault(entry => entry.Id == passwordId)
                ?? (await _repository.GetPasswordsAsync()).FirstOrDefault(entry => entry.Id == passwordId);
            if (password is not null)
            {
                password.IsFavorite = isFavorite;
                await _repository.SavePasswordAsync(password);
                await SynchronizeBoundTotpAsync(password);
                var synchronized = (await _repository.GetSecureItemsByBoundPasswordIdAsync(password.Id))
                    .FirstOrDefault(secureItem => secureItem.ItemType == VaultItemType.Totp);
                if (synchronized is not null)
                {
                    synchronized.IsFavorite = isFavorite;
                    await _repository.SaveSecureItemAsync(synchronized);
                }
            }
        }
        else if (item.Id > 0)
        {
            await _repository.SaveSecureItemAsync(item);
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "FAVORITE",
            DeviceName = Environment.MachineName
        });
    }

    private async Task DeleteTotpItemAsync(SecureItem item, bool updateStatus)
    {
        if (item.BoundPasswordId is { } passwordId)
        {
            var password = Passwords.FirstOrDefault(entry => entry.Id == passwordId)
                ?? (await _repository.GetPasswordsAsync()).FirstOrDefault(entry => entry.Id == passwordId);
            if (password is not null)
            {
                password.IsFavorite = item.IsFavorite;
                password.AuthenticatorKey = "";
                await _repository.SavePasswordAsync(password);
                await SynchronizeBoundTotpAsync(password);
                RefreshPasswordTotpDisplay(password);
            }
        }
        else if (item.Id > 0)
        {
            await _repository.SoftDeleteSecureItemAsync(item.Id);
        }

        TotpItems.Remove(item);
        item.IsSelected = false;
        if (SelectedTotpItem?.Id == item.Id)
        {
            SelectedTotpItem = TotpItems.FirstOrDefault();
        }

        await _repository.LogAsync(new OperationLog
        {
            ItemType = "TOTP",
            ItemId = item.Id,
            ItemTitle = item.Title,
            OperationType = "DELETE",
            DeviceName = Environment.MachineName
        });
        RaiseCounts();
        RaiseTotpFilterState();
        RaiseTotpSelectionState();
        if (updateStatus)
        {
            await LoadTimelineAsync();
            StatusMessage = _localization.Format("MovedToRecycleBinFormat", item.Title);
        }
    }

    private bool MatchesTotpFilters(SecureItem item)
    {
        var filterKey = string.IsNullOrWhiteSpace(SelectedTotpFilterKey) ? TotpFilterAll : SelectedTotpFilterKey;
        if (filterKey.StartsWith(TotpFilterIssuerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var issuer = filterKey[TotpFilterIssuerPrefix.Length..];
            if (!string.Equals(ResolveTotpIssuer(item), issuer, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        else
        {
            switch (filterKey)
            {
                case TotpFilterFavorites when !item.IsFavorite:
                case TotpFilterExpiringSoon when !IsTotpExpiringSoon(item):
                case TotpFilterUnbound when item.BoundPasswordId is not null:
                    return false;
            }
        }

        return MatchesTotpSearch(item, SearchText);
    }

    private static bool MatchesTotpSearch(SecureItem item, string query)
    {
        var term = query.Trim();
        if (term.Length == 0)
        {
            return true;
        }

        var data = ResolveTotpData(item);
        return ContainsAny(
            term,
            item.Title,
            item.Notes,
            item.TotpCode,
            data?.Issuer ?? "",
            data?.AccountName ?? "",
            data?.OtpType ?? "");
    }

    private bool IsTotpExpiringSoon(SecureItem item)
    {
        var data = ResolveTotpData(item);
        if (data is not null)
        {
            return _totpService.GetRemainingSeconds(data.Period) <= 10;
        }

        return item.TotpProgress >= 66;
    }

    private static string ResolveTotpIssuer(SecureItem item)
    {
        var data = ResolveTotpData(item);
        if (!string.IsNullOrWhiteSpace(data?.Issuer))
        {
            return data.Issuer.Trim();
        }

        return string.IsNullOrWhiteSpace(item.Title) ? "TOTP" : item.Title.Trim();
    }

    private static TotpData? ResolveTotpData(SecureItem item) =>
        TotpDataResolver.ParseStoredItemData(item.ItemData, item.Title, item.Notes);
}
