using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Monica.App;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
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

}
