using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Monica.App;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ReplacePasswordGroup(IReadOnlyList<PasswordEntry> previousEntries, IReadOnlyList<PasswordEntry> updatedEntries)
    {
        foreach (var previous in previousEntries)
        {
            Passwords.Remove(previous);
            var current = Passwords.FirstOrDefault(item => item.Id == previous.Id);
            if (current is not null)
            {
                Passwords.Remove(current);
            }
        }

        for (var index = updatedEntries.Count - 1; index >= 0; index--)
        {
            updatedEntries[index].IsSelected = false;
            TrackPasswordSelection(updatedEntries[index]);
            Passwords.Insert(0, updatedEntries[index]);
        }

        RefreshPasswordSelectionStateFromPasswords();
    }

    private static IReadOnlyList<CustomField> BindCustomFields(long entryId, IReadOnlyList<CustomField> fields)
    {
        return fields
            .Select((field, index) => new CustomField
            {
                EntryId = entryId,
                Title = field.Title,
                Value = field.Value,
                IsProtected = field.IsProtected,
                SortOrder = index
            })
            .ToArray();
    }

    private void SetPasswordCustomFields(long entryId, IReadOnlyList<CustomField> fields)
    {
        var next = _passwordCustomFields.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (fields.Count == 0)
        {
            next.Remove(entryId);
        }
        else
        {
            next[entryId] = fields;
        }

        _passwordCustomFields = next;
        RefreshPasswordCustomFieldSearchMatch(entryId, fields);
    }

    private void RefreshPasswordCustomFieldSearchMatch(long entryId, IReadOnlyList<CustomField> fields)
    {
        var query = _passwordCustomFieldSearchQuery.Trim();
        if (query.Length == 0)
        {
            return;
        }

        var matches = _passwordCustomFieldSearchMatches.ToHashSet();
        if (fields.Any(field => ContainsAny(query, field.Title, field.Value)))
        {
            matches.Add(entryId);
        }
        else
        {
            matches.Remove(entryId);
        }

        _passwordCustomFieldSearchMatches = matches;
    }

    private IReadOnlyList<Attachment> GetPasswordAttachments(long entryId)
    {
        return _passwordAttachments.TryGetValue(entryId, out var attachments)
            ? attachments
            : [];
    }

    private IReadOnlyList<Attachment> GetGroupAttachments(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings) =>
        GetGroupAttachments(entry, siblings, _passwordAttachments);

    private static IReadOnlyList<Attachment> GetGroupAttachments(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        IReadOnlyDictionary<long, IReadOnlyList<Attachment>> attachmentsByPasswordId)
    {
        var siblingIds = siblings.Count == 0
            ? [entry.Id]
            : siblings.Select(item => item.Id).ToArray();
        return siblingIds
            .SelectMany(id => attachmentsByPasswordId.TryGetValue(id, out var attachments)
                ? attachments
                : Array.Empty<Attachment>())
            .OrderByDescending(attachment => attachment.CreatedAt)
            .ThenByDescending(attachment => attachment.Id)
            .ToArray();
    }

    private void SetPasswordAttachments(long entryId, IReadOnlyList<Attachment> attachments)
    {
        var next = _passwordAttachments.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (attachments.Count == 0)
        {
            next.Remove(entryId);
        }
        else
        {
            next[entryId] = attachments;
        }

        _passwordAttachments = next;
    }

    private void RefreshPasswordAttachmentState(PasswordEntry entry)
        => PasswordPresentationState.RefreshAttachment(entry, _passwordAttachments);

    private Task<IReadOnlyList<CustomField>> GetGroupCustomFieldsAsync(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        CancellationToken cancellationToken = default) =>
        GetGroupCustomFieldsAsync(entry, siblings, _passwordCustomFields, cancellationToken);

    private async Task<IReadOnlyList<CustomField>> GetGroupCustomFieldsAsync(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>> customFieldsByPasswordId,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in siblings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<CustomField> fields;
            if (customFieldsByPasswordId.TryGetValue(candidate.Id, out var cachedFields))
            {
                fields = cachedFields;
            }
            else
            {
                try
                {
                    fields = await _repository.GetCustomFieldsAsync(candidate.Id, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Custom fields enrich the detail view, but their storage being temporarily
                    // unavailable must not hide the password's primary details.
                    AppDiagnostics.Error($"Load password custom fields failed. id={candidate.Id}", ex);
                    fields = [];
                }
            }

            if (fields.Count > 0 || candidate.Id == entry.Id)
            {
                return fields;
            }
        }

        return [];
    }

    private IReadOnlyList<CustomField> GetCachedGroupCustomFields(PasswordEntry entry, IReadOnlyList<PasswordEntry> siblings) =>
        GetCachedGroupCustomFields(entry, siblings, _passwordCustomFields);

    private static IReadOnlyList<CustomField> GetCachedGroupCustomFields(
        PasswordEntry entry,
        IReadOnlyList<PasswordEntry> siblings,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>> customFieldsByPasswordId)
    {
        foreach (var candidate in siblings)
        {
            var fields = customFieldsByPasswordId.TryGetValue(candidate.Id, out var cachedFields)
                ? cachedFields
                : [];
            if (fields.Count > 0 || candidate.Id == entry.Id)
            {
                return fields;
            }
        }

        return [];
    }
}
