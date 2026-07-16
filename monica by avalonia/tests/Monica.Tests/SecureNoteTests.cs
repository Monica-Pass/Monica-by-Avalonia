using System.Diagnostics;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class SecureNoteTests
{
    [Fact]
    public void Note_editor_caret_lookup_uses_cached_line_index()
    {
        const int lineCount = 5000;
        const int updateCount = 500;
        var viewModel = CreateViewModel();
        var firstLine = "Line 0001 recovery content";
        var lastLine = $"Line {lineCount:D4} recovery content";
        var content = string.Join(
            '\n',
            Enumerable.Range(1, lineCount)
                .Select(index => $"Line {index:D4} recovery content"));
        viewModel.NoteContent = content;
        _ = viewModel.NoteLineCount;

        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < updateCount; index++)
        {
            var caretIndex = content.Length - (index % lastLine.Length);
            viewModel.UpdateNoteEditorStatus(caretIndex, caretIndex - 7, caretIndex);
        }
        stopwatch.Stop();

        Assert.Equal(1, viewModel.NoteContentAnalysisBuildCount);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 50,
            $"Repeated caret lookup took {stopwatch.ElapsedMilliseconds} ms.");

        viewModel.UpdateNoteEditorStatus(content.Length + 100, -100, content.Length + 100);
        Assert.Equal(lineCount, viewModel.NoteCaretLine);
        Assert.Equal(lastLine.Length + 1, viewModel.NoteCaretColumn);
        Assert.Equal(content.Length, viewModel.NoteSelectedCharacterCount);

        viewModel.UpdateNoteEditorStatus(firstLine.Length, firstLine.Length, firstLine.Length);
        Assert.Equal(1, viewModel.NoteCaretLine);
        Assert.Equal(firstLine.Length + 1, viewModel.NoteCaretColumn);

        viewModel.UpdateNoteEditorStatus(firstLine.Length + 1, firstLine.Length + 1, firstLine.Length + 1);
        Assert.Equal(2, viewModel.NoteCaretLine);
        Assert.Equal(1, viewModel.NoteCaretColumn);

        viewModel.NoteContent = "One\nTwo";
        viewModel.UpdateNoteEditorStatus(4, 4, 4);
        Assert.Equal(2, viewModel.NoteContentAnalysisBuildCount);
        Assert.Equal(2, viewModel.NoteCaretLine);
        Assert.Equal(1, viewModel.NoteCaretColumn);
    }

    [Fact]
    public void Note_content_codec_roundtrips_markdown_tags_and_preview()
    {
        var payload = NoteContentCodec.BuildSavePayload(
            "",
            "# Recovery codes\n\n- alpha\n- beta\n\n![](monica-image://img-1)",
            "recovery, private, recovery",
            isMarkdown: true);

        Assert.Equal("Recovery codes", payload.Title);
        Assert.Contains("\"isMarkdown\":true", payload.ItemData);
        Assert.Equal("""["img-1"]""", payload.ImagePaths);

        var decoded = NoteContentCodec.Decode(payload.ItemData, payload.NotesCache);
        Assert.True(decoded.IsMarkdown);
        Assert.Equal(["recovery", "private"], decoded.Tags);
        Assert.Contains("alpha", NoteContentCodec.ToPlainPreview(decoded.Content, decoded.IsMarkdown));
    }

    [Fact]
    public void ViewModel_note_preview_markdown_does_not_emit_monica_image_uri()
    {
        var viewModel = CreateViewModel();
        viewModel.NoteIsMarkdown = true;
        viewModel.NoteContent = "# Inline image\n\n![inline](monica-image://img-1)\n\n![web](https://example.com/a.png)";

        Assert.Contains($"[{viewModel.L.Format("NoteImageAttachmentFormat", "inline")}]", viewModel.NotePreviewMarkdown);
        Assert.DoesNotContain("monica-image://", viewModel.NotePreviewMarkdown);
        Assert.Contains("![web](https://example.com/a.png)", viewModel.NotePreviewMarkdown);
    }

    [Fact]
    public async Task ViewModel_picks_note_image_as_inline_markdown_attachment()
    {
        var picker = new CapturingFileSystemPickerService(new PickedBinaryFile("inline.png", [1, 2, 3, 4]));
        var attachments = new CapturingPasswordAttachmentFileService("secure-notes/inline.png");
        var viewModel = CreateViewModel(fileSystemPickerService: picker, passwordAttachmentFileService: attachments);

        var markdown = await viewModel.PickNoteImageMarkdownAsync();

        Assert.Equal("![](monica-image://secure-notes/inline.png)", markdown);
        Assert.Equal("inline.png", attachments.FileName);
        Assert.Equal("image/png", attachments.ContentType);
        Assert.Equal([1, 2, 3, 4], attachments.Content);
        Assert.Contains("inline.png", viewModel.StatusMessage);
        Assert.Contains("*.png", picker.OpenFileTypes.SelectMany(type => type.Patterns));
        Assert.Contains("*.jpg", picker.OpenFileTypes.SelectMany(type => type.Patterns));
    }

    [Fact]
    public async Task ViewModel_creates_edits_favorites_and_deletes_secure_note()
    {
        var viewModel = CreateViewModel();

        viewModel.NoteTitle = "Recovery";
        viewModel.NoteContent = "# Codes\n\n123456";
        viewModel.NoteTagsText = "account, emergency";
        viewModel.NoteIsMarkdown = true;
        await viewModel.SaveNoteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.NoteItems);
        Assert.Equal("Recovery", viewModel.SelectedNote?.Title);
        Assert.Contains("\"tags\":[\"account\",\"emergency\"]", viewModel.SelectedNote?.ItemData);
        Assert.Equal(viewModel.L.Format("NoteCountFormat", 1), viewModel.NoteCountText);

        await viewModel.ToggleNoteFavoriteCommand.ExecuteAsync(null);
        Assert.True(viewModel.SelectedNote?.IsFavorite);

        viewModel.NoteContent = "plain content";
        viewModel.NoteIsMarkdown = false;
        await viewModel.SaveNoteCommand.ExecuteAsync(null);
        Assert.Equal("plain content", viewModel.SelectedNote?.Notes);

        await viewModel.DeleteNoteCommand.ExecuteAsync(viewModel.SelectedNote);
        Assert.Empty(viewModel.NoteItems);
    }

    [Fact]
    public async Task ViewModel_groups_secure_notes_into_tag_file_tree()
    {
        var viewModel = CreateViewModel();

        viewModel.NoteTitle = "Operations Recovery";
        viewModel.NoteContent = "# Recovery\n\nops codes";
        viewModel.NoteTagsText = "ops, private";
        await viewModel.SaveNoteCommand.ExecuteAsync(null);

        viewModel.SelectedNote = null;
        viewModel.NoteTitle = "Personal Checklist";
        viewModel.NoteContent = "- buy backup key";
        viewModel.NoteTagsText = "private";
        await viewModel.SaveNoteCommand.ExecuteAsync(null);

        viewModel.SelectedNote = null;
        viewModel.NoteTitle = "Loose Note";
        viewModel.NoteContent = "no tags";
        viewModel.NoteTagsText = "";
        await viewModel.SaveNoteCommand.ExecuteAsync(null);

        var groups = viewModel.NoteTreeGroups;
        var untagged = viewModel.L.Get("NoteUntagged");
        Assert.Equal(["ops", "private", untagged], groups.Select(group => group.Name));
        Assert.Single(groups.Single(group => group.Name == "ops").Items);
        Assert.Equal(2, groups.Single(group => group.Name == "private").Items.Count);
        Assert.True(groups.Single(group => group.Name == untagged).IsUntagged);

        viewModel.NoteSearchText = "operations";
        groups = viewModel.NoteTreeGroups;

        Assert.Equal(["ops", "private"], groups.Select(group => group.Name));
        Assert.All(groups, group => Assert.Single(group.Items));
        Assert.All(groups.SelectMany(group => group.Items), item => Assert.Equal("Operations Recovery", item.Title));
    }

    [Fact]
    public async Task Repository_soft_deletes_secure_note()
    {
        var path = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(path);
        var repository = new MonicaRepository(factory, new DatabaseMigrator(factory));
        var payload = NoteContentCodec.BuildSavePayload("Note", "secret", "", true);
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = payload.Title,
            Notes = payload.NotesCache,
            ItemData = payload.ItemData,
            ImagePaths = payload.ImagePaths
        };
        await repository.SaveSecureItemAsync(note);

        await repository.SoftDeleteSecureItemAsync(note.Id);

        Assert.Empty(await repository.GetSecureItemsAsync(VaultItemType.Note));
        Assert.Single(await repository.GetSecureItemsAsync(VaultItemType.Note, includeDeleted: true));
    }

    private static MainWindowViewModel CreateViewModel(
        IFileSystemPickerService? fileSystemPickerService = null,
        IPasswordAttachmentFileService? passwordAttachmentFileService = null)
    {
        var databasePath = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(databasePath);
        var migrator = new DatabaseMigrator(factory);
        return new MainWindowViewModel(
            new MonicaRepository(factory, migrator),
            new VaultCredentialStore(factory, migrator),
            new CryptoService(),
            new TotpService(),
            new PasswordGeneratorService(),
            new ImportExportService(),
            new PlatformCapabilityService(),
            new PlatformIntegrationService(),
            new NoopClipboardService(),
            new NoopWebDavBackupService(),
            new MdbxVaultService(),
            passwordAttachmentFileService ?? new NoopPasswordAttachmentFileService(),
            new NoopPasswordEditorDialogService(),
            new NoopPasswordDetailDialogService(),
            new NoopCategoryPickerDialogService(),
            new LegacyVaultDetector(factory),
            new AppSettingsService(GetTempSettingsPath()),
            new LocalizationService(),
            fileSystemPickerService: fileSystemPickerService);
    }

    private static string GetTempDatabasePath()
    {
        return TestTempPaths.CreateFilePath(".db");
    }

    private static string GetTempSettingsPath()
    {
        return TestTempPaths.CreateFilePath(".settings.json");
    }

    private sealed class NoopClipboardService : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopWebDavBackupService : IWebDavBackupService
    {
        public string NormalizeRemotePath(string rootPath, string relativePath) => relativePath;
        public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteFileEntry>>([]);
        public Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopPasswordEditorDialogService : IPasswordEditorDialogService
    {
        public Task<PasswordEditorViewModel?> ShowAsync(
            PasswordEntry? entry,
            IReadOnlyList<Category> categories,
            string plainPassword,
            IReadOnlyList<string>? siblingPasswords = null,
            IReadOnlyList<SecureItem>? notes = null,
            IReadOnlyList<CustomField>? customFields = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordEditorViewModel?>(null);
    }

    private sealed class NoopPasswordDetailDialogService : IPasswordDetailDialogService
    {
        public Task ShowAsync(
            PasswordEntry entry,
            IReadOnlyList<PasswordEntry> siblings,
            Category? category,
            SecureItem? boundNote,
            IReadOnlyList<Attachment> attachments,
            IReadOnlyList<CustomField> customFields,
            IReadOnlyList<PasswordHistoryDisplayItem> passwordHistory,
            Func<PasswordEntry, Task>? addAttachment,
            Func<Attachment, Task<PasswordAttachmentSaveResult>>? saveAttachment,
            Func<Attachment, Task<bool>>? deleteAttachment,
            Func<PasswordHistoryEntry, Task<bool>>? deletePasswordHistory,
            Func<long, Task<bool>>? clearPasswordHistory,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopPasswordAttachmentFileService : IPasswordAttachmentFileService
    {
        public Task<PasswordAttachmentFileDraft?> PickAttachmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordAttachmentFileDraft?>(null);

        public Task<PasswordAttachmentFileDraft> StoreAttachmentAsync(string fileName, byte[] content, string contentType = "", CancellationToken cancellationToken = default) =>
            Task.FromResult(new PasswordAttachmentFileDraft(fileName, "", content.LongLength, contentType, content));

        public Task DeleteStoredAttachmentAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CapturingPasswordAttachmentFileService(string storagePath) : IPasswordAttachmentFileService
    {
        public string FileName { get; private set; } = "";
        public byte[] Content { get; private set; } = [];
        public string ContentType { get; private set; } = "";

        public Task<PasswordAttachmentFileDraft?> PickAttachmentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordAttachmentFileDraft?>(null);

        public Task<PasswordAttachmentFileDraft> StoreAttachmentAsync(string fileName, byte[] content, string contentType = "", CancellationToken cancellationToken = default)
        {
            FileName = fileName;
            Content = content;
            ContentType = contentType;
            return Task.FromResult(new PasswordAttachmentFileDraft(fileName, storagePath, content.LongLength, contentType, content));
        }

        public Task DeleteStoredAttachmentAsync(string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CapturingFileSystemPickerService(PickedBinaryFile? openFile) : IFileSystemPickerService
    {
        public IReadOnlyList<PlatformFilePickerFileType> OpenFileTypes { get; private set; } = [];
        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.FilePicker,
            "Test file picking works.");

        public Task<PickedTextFile?> OpenTextFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(string title, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default)
        {
            OpenFileTypes = fileTypes;
            return Task.FromResult(openFile);
        }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SaveBinaryFileAsync(string title, string suggestedFileName, ReadOnlyMemory<byte> content, IReadOnlyList<PlatformFilePickerFileType> fileTypes, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoopCategoryPickerDialogService : ICategoryPickerDialogService
    {
        public Task<PasswordCategoryChoice?> ShowAsync(
            IReadOnlyList<Category> categories,
            long? selectedCategoryId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PasswordCategoryChoice?>(null);
    }
}
