using Monica.Core.ImportExport;
using Monica.Core.Models;

namespace Monica.Tests;

internal sealed class DelegatingBitwardenImportExportService(
    Func<string, BitwardenJsonImportSnapshot> importBitwardenJson) : IImportExportService
{
    private readonly ImportExportService _inner = new();

    public BitwardenJsonImportSnapshot ImportBitwardenJson(string json) => importBitwardenJson(json);

    public string ExportJson(
        IEnumerable<PasswordEntry> passwords,
        IEnumerable<SecureItem> secureItems,
        IEnumerable<Category>? categories = null,
        IReadOnlyDictionary<long, IReadOnlyList<CustomField>>? passwordCustomFields = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordHistoryEntry>>? passwordHistory = null,
        IReadOnlyDictionary<long, IReadOnlyList<PasswordAttachmentExport>>? passwordAttachments = null,
        IReadOnlyDictionary<long, IReadOnlyList<SecureItemAttachmentExport>>? secureItemAttachments = null) =>
        _inner.ExportJson(
            passwords,
            secureItems,
            categories,
            passwordCustomFields,
            passwordHistory,
            passwordAttachments,
            secureItemAttachments);

    public MonicaExportPackage ImportJson(string json) => _inner.ImportJson(json);
    public string ExportPasswordCsv(IEnumerable<PasswordEntry> passwords) => _inner.ExportPasswordCsv(passwords);
    public string ExportTotpCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportTotpCsv(secureItems);
    public string ExportNoteCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportNoteCsv(secureItems);
    public string ExportWalletCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportWalletCsv(secureItems);
    public string ExportAegisJson(IEnumerable<SecureItem> secureItems) => _inner.ExportAegisJson(secureItems);
    public IReadOnlyList<SecureItem> ImportTotpCsv(string csv) => _inner.ImportTotpCsv(csv);
    public IReadOnlyList<SecureItem> ImportNoteCsv(string csv) => _inner.ImportNoteCsv(csv);
    public bool IsEncryptedAegisJson(string json) => _inner.IsEncryptedAegisJson(json);
    public IReadOnlyList<SecureItem> ImportAegisJson(string json, string? password = null) =>
        _inner.ImportAegisJson(json, password);
    public IReadOnlyList<PasswordEntry> ImportPasswordCsv(string csv) => _inner.ImportPasswordCsv(csv);
}
