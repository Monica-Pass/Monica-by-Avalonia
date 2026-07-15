using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void EnsureNoteTreeProjection()
    {
        if (!_noteTreeProjectionDirty)
        {
            return;
        }

        FilteredNoteProjectionBuildCount++;
        var terms = ParseNoteSearchTerms(NoteSearchText);
        var favoriteCount = 0;
        var matchingNotes = new List<NoteTreeProjectionItem>(NoteItems.Count);

        foreach (var item in NoteItems)
        {
            if (item.IsFavorite)
            {
                favoriteCount++;
            }

            var decoded = DecodeNoteForProjection(item);
            var tags = GetNoteTreeTags(decoded);
            if (MatchesNoteSearch(item, decoded.Content, tags, terms))
            {
                matchingNotes.Add(new NoteTreeProjectionItem(item, tags));
            }
        }

        var orderedNotes = matchingNotes
            .OrderByDescending(note => note.Item.IsFavorite)
            .ThenByDescending(note => note.Item.UpdatedAt)
            .ThenBy(note => note.Item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _filteredNoteItems = orderedNotes.Select(note => note.Item).ToArray();
        _favoriteNoteItems = orderedNotes
            .Where(note => note.Item.IsFavorite)
            .Select(note => note.Item)
            .ToArray();
        _noteTreeGroups = BuildNoteTreeGroups(orderedNotes);
        _favoriteNoteCount = favoriteCount;
        _noteTreeProjectionDirty = false;
    }

    private IReadOnlyList<NoteTreeGroup> BuildNoteTreeGroups(IReadOnlyList<NoteTreeProjectionItem> notes)
    {
        NoteTreeGroupProjectionBuildCount++;
        var taggedGroups = new SortedDictionary<string, List<SecureItem>>(StringComparer.OrdinalIgnoreCase);
        var untagged = new List<SecureItem>();

        foreach (var note in notes)
        {
            if (note.Tags.Count == 0)
            {
                untagged.Add(note.Item);
                continue;
            }

            foreach (var tag in note.Tags)
            {
                if (!taggedGroups.TryGetValue(tag, out var bucket))
                {
                    bucket = [];
                    taggedGroups[tag] = bucket;
                }

                bucket.Add(note.Item);
            }
        }

        var groups = taggedGroups
            .Select(pair => new NoteTreeGroup(pair.Key, pair.Value.Count, pair.Value, IsUntagged: false))
            .ToList();

        if (untagged.Count > 0)
        {
            groups.Add(new NoteTreeGroup(
                _localization.Get("NoteUntagged"),
                untagged.Count,
                untagged,
                IsUntagged: true));
        }

        return groups;
    }

    private DecodedNoteContent DecodeNoteForProjection(SecureItem item)
    {
        NotePayloadDecodeCount++;
        try
        {
            return NoteContentCodec.DecodeFromItem(item);
        }
        catch
        {
            return new DecodedNoteContent("", [], false);
        }
    }

    private static IReadOnlyList<string> GetNoteTreeTags(DecodedNoteContent decoded) =>
        decoded.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string[] ParseNoteSearchTerms(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split(
                [' ', '\t', '\r', '\n', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool MatchesNoteSearch(
        SecureItem item,
        string decodedContent,
        IReadOnlyList<string> decodedTags,
        IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return true;
        }

        return terms.All(term =>
            ContainsOrdinalIgnoreCase(item.Title, term) ||
            ContainsOrdinalIgnoreCase(item.Notes, term) ||
            ContainsOrdinalIgnoreCase(decodedContent, term) ||
            decodedTags.Any(tag => ContainsOrdinalIgnoreCase(tag, term)));
    }

    private static bool ContainsOrdinalIgnoreCase(string? source, string value) =>
        !string.IsNullOrEmpty(source) &&
        source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private sealed record NoteTreeProjectionItem(SecureItem Item, IReadOnlyList<string> Tags);
}
