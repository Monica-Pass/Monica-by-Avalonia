using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Monica.App;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RecordPasswordQuickAccessAsync(PasswordEntry entry)
    {
        if (entry.Id <= 0 || entry.IsDeleted || entry.IsArchived)
        {
            return;
        }

        var updatedRecord = await _repository.RecordPasswordQuickAccessAsync(entry.Id);
        if (updatedRecord is null)
        {
            return;
        }

        _passwordQuickAccessRecords = BuildBoundedPasswordQuickAccessCache(
            _passwordQuickAccessRecords.Values
                .Where(record => record.PasswordId != updatedRecord.PasswordId)
                .Append(updatedRecord));
        RaisePasswordQuickAccessState();
    }

    private static IReadOnlyDictionary<long, PasswordQuickAccessRecord> BuildBoundedPasswordQuickAccessCache(
        IEnumerable<PasswordQuickAccessRecord> records)
    {
        var validRecords = records
            .Where(record => record.OpenCount > 0 && record.PasswordId > 0)
            .ToArray();
        var recent = validRecords
            .OrderByDescending(record => record.LastOpenedAt)
            .ThenByDescending(record => record.OpenCount)
            .Take(PasswordQuickAccessLimit);
        var frequent = validRecords
            .OrderByDescending(record => record.OpenCount)
            .ThenByDescending(record => record.LastOpenedAt)
            .Take(PasswordQuickAccessLimit);
        return recent
            .Concat(frequent)
            .DistinctBy(record => record.PasswordId)
            .ToDictionary(record => record.PasswordId);
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
