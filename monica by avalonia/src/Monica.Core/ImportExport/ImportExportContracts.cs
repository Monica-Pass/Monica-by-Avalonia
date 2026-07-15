using Monica.Core.Models;

namespace Monica.Core.ImportExport;

public interface IImportExportService
{
    string ExportJson(
        IEnumerable<PasswordEntry> passwords,
        IEnumerable<SecureItem> secureItems,
        IEnumerable<Category>? categories = null,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>>? passwordCustomFields = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>? passwordHistory = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordAttachmentExport>>? passwordAttachments = null,
        IReadOnlyDictionary<long, IReadOnlyList<SecureItemAttachmentExport>>? secureItemAttachments = null);
    MonicaExportPackage ImportJson(string json);
    string ExportPasswordCsv(IEnumerable<PasswordEntry> passwords);
    string ExportTotpCsv(IEnumerable<SecureItem> secureItems);
    string ExportNoteCsv(IEnumerable<SecureItem> secureItems);
    string ExportWalletCsv(IEnumerable<SecureItem> secureItems);
    string ExportAegisJson(IEnumerable<SecureItem> secureItems);
    IReadOnlyList<SecureItem> ImportTotpCsv(string csv);
    IReadOnlyList<SecureItem> ImportNoteCsv(string csv);
    bool IsEncryptedAegisJson(string json);
    IReadOnlyList<SecureItem> ImportAegisJson(string json, string? password = null);
    IReadOnlyList<PasswordEntry> ImportPasswordCsv(string csv);
}

public sealed record MonicaExportPackage(
    int SchemaVersion,
    IReadOnlyList<PasswordEntry> Passwords,
    IReadOnlyList<SecureItem> SecureItems,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<PasswordCustomFieldExportGroup> PasswordCustomFields,
    IReadOnlyList<PasswordHistoryExportGroup> PasswordHistory,
    IReadOnlyList<PasswordAttachmentExportGroup> PasswordAttachments,
    IReadOnlyList<SecureItemAttachmentExportGroup> SecureItemAttachments);

public sealed record PasswordCustomFieldExportGroup(long PasswordId, IReadOnlyList<CustomField> Fields);

public sealed record PasswordHistoryExportGroup(long PasswordId, IReadOnlyList<PasswordHistoryEntry> Entries);

public sealed record PasswordAttachmentExportGroup(long PasswordId, IReadOnlyList<PasswordAttachmentExport> Attachments);

public sealed record PasswordAttachmentExport(Attachment Metadata, string ContentBase64);

public sealed record SecureItemAttachmentExportGroup(long SecureItemId, IReadOnlyList<SecureItemAttachmentExport> Attachments);

public sealed record SecureItemAttachmentExport(Attachment Metadata, string ContentBase64);
