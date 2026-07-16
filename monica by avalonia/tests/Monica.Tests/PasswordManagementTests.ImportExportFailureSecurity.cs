using Monica.Core.Models;
using Monica.Core.ImportExport;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class PasswordManagementTests
{
    [Fact]
    public async Task ImportExportFailureSecurity_save_failure_hides_local_path_and_provider_details()
    {
        const string sensitiveDetail = @"Access denied: C:\Users\joyins\Documents\private-vault\monica-export.json";
        var picker = new ThrowingTextFilePickerService(saveFailure: new IOException(sensitiveDetail));
        var harness = CreateHarness(
            fileSystemPickerService: picker,
            exportAuthorizationService: new CountingExportAuthorizationService());

        await harness.ViewModel.SaveNoteCsvExportCommand.ExecuteAsync(null);

        Assert.Equal(harness.ViewModel.L.Get("SaveExportFileFailed"), harness.ViewModel.StatusMessage);
        Assert.DoesNotContain("joyins", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-vault", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("monica-export.json", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportExportFailureSecurity_open_failure_hides_local_path_and_provider_details()
    {
        const string sensitiveDetail = @"Sharing violation: C:\Users\joyins\Desktop\secret-import.csv";
        var picker = new ThrowingTextFilePickerService(openFailure: new IOException(sensitiveDetail));
        var harness = CreateHarness(fileSystemPickerService: picker);

        await harness.ViewModel.ImportPasswordCsvFileCommand.ExecuteAsync(null);

        Assert.Equal(harness.ViewModel.L.Get("ImportFileSelectionFailed"), harness.ViewModel.StatusMessage);
        Assert.DoesNotContain("joyins", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-import.csv", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sharing violation", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportExportFailureSecurity_unexpected_json_parser_failure_hides_payload_and_parser_details()
    {
        const string secretFragment = "PRIVATE-RECOVERY-CODE-7291";
        var service = new ThrowingImportExportService(
            importJsonFailure: new InvalidOperationException(
                $"Parser state failed near {secretFragment} in C:\\Users\\joyins\\Desktop\\vault.json"));
        var harness = CreateHarness(importExportService: service);
        harness.ViewModel.ImportJsonText = $"{{\"secret\":\"{secretFragment}\"}}";

        await harness.ViewModel.ImportDataCommand.ExecuteAsync(null);

        Assert.Equal(harness.ViewModel.L.Get("ImportUnexpectedFailure"), harness.ViewModel.StatusMessage);
        Assert.DoesNotContain(secretFragment, harness.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("joyins", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("parser state", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportExportFailureSecurity_invalid_monica_json_keeps_actionable_format_feedback()
    {
        var harness = CreateHarness();
        harness.ViewModel.ImportJsonText = "{\"schemaVersion\":71,\"passwords\":[";

        await harness.ViewModel.ImportDataCommand.ExecuteAsync(null);

        Assert.Equal(harness.ViewModel.L.Get("ImportInvalidFormat"), harness.ViewModel.StatusMessage);
        Assert.NotEmpty(harness.ViewModel.ImportJsonText);
        Assert.Empty(await harness.Repository.GetPasswordsAsync());
    }

    [Fact]
    public async Task ImportExportFailureSecurity_unexpected_password_csv_failure_hides_record_contents()
    {
        const string secretFragment = "CSV-SECRET-8862";
        var service = new ThrowingImportExportService(
            importPasswordCsvFailure: new InvalidOperationException(
                $"CSV parser failed on password {secretFragment} at row 42"));
        var harness = CreateHarness(importExportService: service);
        harness.ViewModel.ImportCsvText = $"title,password\r\nprivate,{secretFragment}";

        await harness.ViewModel.ImportPasswordCsvCommand.ExecuteAsync(null);

        Assert.Equal(harness.ViewModel.L.Get("ImportUnexpectedFailure"), harness.ViewModel.StatusMessage);
        Assert.DoesNotContain(secretFragment, harness.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("row 42", harness.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportExportFailureSecurity_unexpected_note_csv_failure_hides_note_contents()
    {
        const string privateNote = "PRIVATE-NOTE-CONTENT-4390";
        var service = new ThrowingImportExportService(
            importNoteCsvFailure: new InvalidOperationException(
                $"CSV parser failed while reading {privateNote}"));
        var harness = CreateHarness(importExportService: service);
        harness.ViewModel.ImportNoteCsvText = $"type,title,data\r\nNOTE,private,{privateNote}";

        await harness.ViewModel.ImportNoteCsvCommand.ExecuteAsync(null);

        Assert.Equal(harness.ViewModel.L.Get("ImportUnexpectedFailure"), harness.ViewModel.StatusMessage);
        Assert.DoesNotContain(privateNote, harness.ViewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(await harness.Repository.GetSecureItemsAsync(VaultItemType.Note));
    }

    [Fact]
    public async Task ImportExportFailureSecurity_password_csv_with_unrecognized_headers_reports_invalid_format()
    {
        var harness = CreateHarness();
        harness.ViewModel.ImportCsvText = "private_column,internal_value\r\nalpha,beta";

        await harness.ViewModel.ImportPasswordCsvCommand.ExecuteAsync(null);

        Assert.Equal(harness.ViewModel.L.Get("ImportCsvInvalidFormat"), harness.ViewModel.StatusMessage);
        Assert.NotEmpty(harness.ViewModel.ImportCsvText);
        Assert.Empty(await harness.Repository.GetPasswordsAsync());
    }

    [Fact]
    public async Task ImportExportFailureSecurity_monica_json_without_required_collections_reports_invalid_format()
    {
        var harness = CreateHarness();
        harness.ViewModel.ImportJsonText = "{\"schemaVersion\":71}";

        await harness.ViewModel.ImportDataCommand.ExecuteAsync(null);

        Assert.Equal(harness.ViewModel.L.Get("ImportInvalidFormat"), harness.ViewModel.StatusMessage);
        Assert.Empty(await harness.Repository.GetPasswordsAsync());
        Assert.Empty(await harness.Repository.GetSecureItemsAsync(VaultItemType.Note));
    }

    private sealed class ThrowingTextFilePickerService(
        Exception? openFailure = null,
        Exception? saveFailure = null) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability { get; } = new(
            PlatformFeatureKeys.FilePicker,
            PlatformFeatureStatus.Available,
            "Test file picker");

        public Task<PickedTextFile?> OpenTextFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) =>
            openFailure is null
                ? Task.FromResult<PickedTextFile?>(null)
                : Task.FromException<PickedTextFile?>(openFailure);

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PickedBinaryFile?>(null);

        public Task<string?> SaveTextFileAsync(
            string title,
            string suggestedFileName,
            string content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) =>
            saveFailure is null
                ? Task.FromResult<string?>(suggestedFileName)
                : Task.FromException<string?>(saveFailure);

        public Task<string?> SaveBinaryFileAsync(
            string title,
            string suggestedFileName,
            ReadOnlyMemory<byte> content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(suggestedFileName);
    }

    private sealed class ThrowingImportExportService(
        Exception? importJsonFailure = null,
        Exception? importPasswordCsvFailure = null,
        Exception? importNoteCsvFailure = null) : IImportExportService
    {
        private readonly ImportExportService _inner = new();

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

        public MonicaExportPackage ImportJson(string json) =>
            importJsonFailure is null
                ? _inner.ImportJson(json)
                : throw importJsonFailure;

        public BitwardenJsonImportSnapshot ImportBitwardenJson(string json) => _inner.ImportBitwardenJson(json);
        public string ExportPasswordCsv(IEnumerable<PasswordEntry> passwords) => _inner.ExportPasswordCsv(passwords);
        public string ExportTotpCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportTotpCsv(secureItems);
        public string ExportNoteCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportNoteCsv(secureItems);
        public string ExportWalletCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportWalletCsv(secureItems);
        public string ExportAegisJson(IEnumerable<SecureItem> secureItems) => _inner.ExportAegisJson(secureItems);
        public IReadOnlyList<SecureItem> ImportTotpCsv(string csv) => _inner.ImportTotpCsv(csv);
        public IReadOnlyList<SecureItem> ImportNoteCsv(string csv) =>
            importNoteCsvFailure is null
                ? _inner.ImportNoteCsv(csv)
                : throw importNoteCsvFailure;
        public bool IsEncryptedAegisJson(string json) => _inner.IsEncryptedAegisJson(json);
        public IReadOnlyList<SecureItem> ImportAegisJson(string json, string? password = null) =>
            _inner.ImportAegisJson(json, password);
        public IReadOnlyList<PasswordEntry> ImportPasswordCsv(string csv) =>
            importPasswordCsvFailure is null
                ? _inner.ImportPasswordCsv(csv)
                : throw importPasswordCsvFailure;
    }
}
