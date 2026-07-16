using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public async Task ImportExportFailureSecurity_authenticator_file_open_failure_hides_local_path()
    {
        const string sensitiveDetail = @"Access denied: C:\Users\joyins\Desktop\private-totp.csv";
        var viewModel = CreateViewModel(
            GetTempPath(),
            fileSystemPickerService: new ThrowingAuthenticatorFilePicker(new IOException(sensitiveDetail)));

        await viewModel.ImportTotpCsvFileCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ImportFileSelectionFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain("joyins", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-totp.csv", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportExportFailureSecurity_totp_csv_with_unrecognized_headers_reports_invalid_format()
    {
        var viewModel = CreateViewModel(GetTempPath());
        viewModel.ImportTotpCsvText = "private_column,internal_value\r\nalpha,beta";

        await viewModel.ImportTotpCsvCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ImportCsvInvalidFormat"), viewModel.StatusMessage);
        Assert.NotEmpty(viewModel.ImportTotpCsvText);
        Assert.Empty(viewModel.TotpItems);
    }

    [Fact]
    public async Task ImportExportFailureSecurity_unexpected_aegis_failure_hides_payload_and_crypto_details()
    {
        const string secretFragment = "AEGIS-PRIVATE-SECRET-9124";
        var service = new ThrowingAuthenticatorImportExportService(
            importAegisFailure: new InvalidOperationException(
                $"Crypto provider failed near {secretFragment} in C:\\Users\\joyins\\Desktop\\aegis.json"));
        var viewModel = CreateViewModel(GetTempPath(), importExportService: service);
        viewModel.ImportAegisJsonText = $"{{\"db\":{{\"secret\":\"{secretFragment}\"}}}}";

        await viewModel.ImportAegisJsonCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ImportUnexpectedFailure"), viewModel.StatusMessage);
        Assert.DoesNotContain(secretFragment, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("crypto provider", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("joyins", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportExportFailureSecurity_unexpected_totp_csv_failure_hides_secret_contents()
    {
        const string secretFragment = "TOTP-PRIVATE-SECRET-3381";
        var service = new ThrowingAuthenticatorImportExportService(
            importTotpCsvFailure: new InvalidOperationException($"Parser failed on {secretFragment}"));
        var viewModel = CreateViewModel(GetTempPath(), importExportService: service);
        viewModel.ImportTotpCsvText = $"type,title,data\r\nTOTP,private,{secretFragment}";

        await viewModel.ImportTotpCsvCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ImportUnexpectedFailure"), viewModel.StatusMessage);
        Assert.DoesNotContain(secretFragment, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.NotEmpty(viewModel.ImportTotpCsvText);
    }

    private sealed class ThrowingAuthenticatorFilePicker(Exception openFailure) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability { get; } = new(
            PlatformFeatureKeys.FilePicker,
            PlatformFeatureStatus.Available,
            "Test file picker");

        public Task<PickedTextFile?> OpenTextFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromException<PickedTextFile?>(openFailure);

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
            Task.FromResult<string?>(suggestedFileName);

        public Task<string?> SaveBinaryFileAsync(
            string title,
            string suggestedFileName,
            ReadOnlyMemory<byte> content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(suggestedFileName);
    }

    private sealed class ThrowingAuthenticatorImportExportService(
        Exception? importAegisFailure = null,
        Exception? importTotpCsvFailure = null) : IImportExportService
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

        public MonicaExportPackage ImportJson(string json) => _inner.ImportJson(json);
        public BitwardenJsonImportSnapshot ImportBitwardenJson(string json) => _inner.ImportBitwardenJson(json);
        public string ExportPasswordCsv(IEnumerable<PasswordEntry> passwords) => _inner.ExportPasswordCsv(passwords);
        public string ExportTotpCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportTotpCsv(secureItems);
        public string ExportNoteCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportNoteCsv(secureItems);
        public string ExportWalletCsv(IEnumerable<SecureItem> secureItems) => _inner.ExportWalletCsv(secureItems);
        public string ExportAegisJson(IEnumerable<SecureItem> secureItems) => _inner.ExportAegisJson(secureItems);
        public IReadOnlyList<SecureItem> ImportTotpCsv(string csv) =>
            importTotpCsvFailure is null
                ? _inner.ImportTotpCsv(csv)
                : throw importTotpCsvFailure;
        public IReadOnlyList<SecureItem> ImportNoteCsv(string csv) => _inner.ImportNoteCsv(csv);
        public bool IsEncryptedAegisJson(string json) => _inner.IsEncryptedAegisJson(json);
        public IReadOnlyList<SecureItem> ImportAegisJson(string json, string? password = null) =>
            importAegisFailure is null
                ? _inner.ImportAegisJson(json, password)
                : throw importAegisFailure;
        public IReadOnlyList<PasswordEntry> ImportPasswordCsv(string csv) => _inner.ImportPasswordCsv(csv);
    }
}
