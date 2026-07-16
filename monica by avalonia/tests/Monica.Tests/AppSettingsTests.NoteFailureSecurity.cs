using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed partial class AppSettingsTests
{
    [Fact]
    public void Note_failure_messages_are_actionable_in_english_and_chinese()
    {
        var localization = new LocalizationService();

        Assert.Contains("try again", localization.Get("OpenReferenceFailed"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("try again", localization.Get("ImportMarkdownFailed"), StringComparison.OrdinalIgnoreCase);

        localization.SetLanguage("zh-CN");

        Assert.Contains("重试", localization.Get("OpenReferenceFailed"), StringComparison.Ordinal);
        Assert.Contains("重试", localization.Get("ImportMarkdownFailed"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Note_failure_opening_reference_keeps_raw_exception_details_out_of_status()
    {
        const string rawFailure = @"C:\Users\joyins\Private\browser.exe access denied";
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.ExternalLinks, "External links work.")
        ]);
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            externalLinkService: new ThrowingNoteExternalLinkService(rawFailure));
        var reference = new NoteReferenceItem("Portal", "https://example.com/private", 1, false);
        await viewModel.InitializeAsync();

        await viewModel.OpenNoteReferenceCommand.ExecuteAsync(reference);

        Assert.Equal(viewModel.L.Get("OpenReferenceFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("joyins", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Note_failure_importing_markdown_keeps_raw_exception_details_out_of_status()
    {
        const string rawFailure = @"Could not read C:\Users\joyins\Private\recovery-codes.md";
        var integration = new PlatformIntegrationService("TestOS",
        [
            PlatformIntegrationService.Available(PlatformFeatureKeys.FilePicker, "File picking works.")
        ]);
        var viewModel = CreateViewModel(
            GetTempPath(),
            platformIntegrationService: integration,
            fileSystemPickerService: new ThrowingNoteFileSystemPickerService(rawFailure));
        await viewModel.InitializeAsync();

        await viewModel.ImportMarkdownNoteCommand.ExecuteAsync(null);

        Assert.Equal(viewModel.L.Get("ImportMarkdownFailed"), viewModel.StatusMessage);
        Assert.DoesNotContain(rawFailure, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("recovery-codes.md", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingNoteExternalLinkService(string message) : IExternalLinkService
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.ExternalLinks,
            "Test external links are available.");

        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException(message));
    }

    private sealed class ThrowingNoteFileSystemPickerService(string message) : IFileSystemPickerService
    {
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.FilePicker,
            "Test file picking is available.");

        public Task<PickedTextFile?> OpenTextFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromException<PickedTextFile?>(new IOException(message));

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
            Task.FromResult<string?>(null);

        public Task<string?> SaveBinaryFileAsync(
            string title,
            string suggestedFileName,
            ReadOnlyMemory<byte> content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
