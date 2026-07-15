using Monica.Core.Models;

namespace Monica.App.ViewModels;

internal static class PasswordQuickAccessCache
{
    internal const int RankingLimit = 6;

    internal static IReadOnlyDictionary<long, PasswordQuickAccessRecord> Create(
        IEnumerable<PasswordQuickAccessRecord> records)
    {
        var validRecords = records
            .Where(record => record.OpenCount > 0 && record.PasswordId > 0)
            .ToArray();
        var recent = validRecords
            .OrderByDescending(record => record.LastOpenedAt)
            .ThenByDescending(record => record.OpenCount)
            .Take(RankingLimit);
        var frequent = validRecords
            .OrderByDescending(record => record.OpenCount)
            .ThenByDescending(record => record.LastOpenedAt)
            .Take(RankingLimit);
        return recent
            .Concat(frequent)
            .DistinctBy(record => record.PasswordId)
            .ToDictionary(record => record.PasswordId);
    }
}
