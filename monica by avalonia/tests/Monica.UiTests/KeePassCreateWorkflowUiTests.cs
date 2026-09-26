using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.ImportExport;
using Monica.App.Features.Sync;
using Monica.App.ViewModels;
using Monica.Platform.Services;

namespace Monica.UiTests;

/// <summary>
/// The "new database" entry point asked where a person meets it: the KeePass tab of a shown window. The
/// commands are already covered against the disk elsewhere, so what this proves is the two things a
/// command test cannot reach - that the form and its masked fields are actually painted, that the create
/// button refuses to light up while the two master passwords disagree, and that the file it writes is a
/// database the same pane can put an entry into and save back over without asking for a location twice.
/// The master password is only ever compared, never printed.
/// </summary>
[Collection(AvaloniaUiTestCollection.Name)]
public sealed class KeePassCreateWorkflowUiTests
{
    private const string CreatedVaultPassword = "created-vault-fixture-not-a-secret";
    private const string WrongVaultPassword = "not-the-password";
    private const string DraftSecret = "create-draft-secret";
    private const string DraftTitle = "Written by Monica";

    public KeePassCreateWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    /// <summary>
    /// Opens the form through the button that declares it and reads the answer off the screen. The
    /// mismatch is the case that matters: an enabled create next to two passwords that differ would write
    /// a vault the person cannot open, so both the notice and the greyed-out button are checked, and the
    /// command is then run anyway to show the view model does not rely on the button to stop it.
    /// </summary>
    [Fact]
    public async Task KeePass_create_form_is_painted_masked_and_waits_for_two_passwords_that_agree()
    {
        var targetPath = NewTargetPath();
        var picker = new CreateTargetFilePicker(targetPath);
        var opened = await HostAsync(picker);
        var view = opened.View;
        var viewModel = opened.ViewModel;
        try
        {
            var openFormButton = view.InPane<Button>("NewKeePassVaultButton")!;
            var form = view.InPane<StackPanel>("KeePassCreateForm")!;
            Assert.False(form.IsVisible);
            Assert.True(openFormButton.IsEnabled, "the pane is not idle before anything has been asked of it");

            openFormButton.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var passwordBox = view.InPane<TextBox>("KeePassCreatePasswordBox")!;
            var confirmBox = view.InPane<TextBox>("KeePassCreateConfirmPasswordBox")!;
            var mismatchText = view.InPane<TextBlock>("KeePassCreatePasswordMismatchText")!;
            var createButton = view.InPane<Button>("CreateKeePassVaultButton")!;
            var cancelButton = view.InPane<Button>("CancelKeePassVaultCreateButton")!;

            // A button that points at a neighbour command would pass every visual assertion below.
            Assert.Same(viewModel.CreateKeePassVaultCommand, createButton.Command);
            Assert.Same(viewModel.CancelKeePassVaultCreateCommand, cancelButton.Command);

            Assert.True(form.IsVisible, "the create form never came up on screen");
            Assert.True(form.Bounds.Width > 0 && form.Bounds.Height > 0);
            Assert.True(passwordBox.Bounds.Width > 0);
            Assert.True(confirmBox.Bounds.Width > 0);
            Assert.Equal('*', passwordBox.PasswordChar);
            Assert.Equal('*', confirmBox.PasswordChar);
            Assert.True(createButton.IsVisible);
            Assert.False(createButton.IsEnabled);
            Assert.False(mismatchText.IsVisible);

            passwordBox.Text = CreatedVaultPassword;
            Dispatcher.UIThread.RunJobs();

            // The second line is still empty, so nothing has gone wrong yet - and the button stays dark
            // because a single password is not a confirmation.
            Assert.False(mismatchText.IsVisible);
            Assert.False(createButton.IsEnabled);

            confirmBox.Text = WrongVaultPassword;
            Dispatcher.UIThread.RunJobs();

            Assert.True(mismatchText.IsVisible, "the pane did not say the two passwords differ");
            Assert.False(string.IsNullOrEmpty(mismatchText.Text));
            Assert.False(createButton.IsEnabled);
            Assert.False(viewModel.CanCreateKeePassVault);

            // Run it anyway. The button is a convenience; the command refusing to reach the disk is the
            // guarantee, and the only way to tell those apart is to take the button out of the loop.
            await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);

            Assert.Equal(0, picker.TargetCalls);
            Assert.False(File.Exists(targetPath));
            Assert.False(viewModel.HasKeePassImportPreview);

            confirmBox.Text = CreatedVaultPassword;
            Dispatcher.UIThread.RunJobs();

            Assert.False(mismatchText.IsVisible);
            Assert.True(createButton.IsEnabled);
            // The typed text has to stay inside the boxes. Everything the form renders is collected here
            // because a supporting label that echoed the field would otherwise sail past the mask.
            AssertPaintedWithoutSecrets(form, CreatedVaultPassword, WrongVaultPassword);

            cancelButton.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(form.IsVisible);
            // Emptiness asserted without a value pair: an expected/actual against "" would have xUnit
            // print whatever the box still held, and that is exactly the text this test typed in.
            Assert.True(string.IsNullOrEmpty(passwordBox.Text));
            Assert.True(string.IsNullOrEmpty(confirmBox.Text));
            Assert.Equal(0, viewModel.KeePassCreatePassword.Length);
            Assert.Equal(0, viewModel.KeePassCreateConfirmation.Length);
        }
        finally
        {
            opened.Host.Close();
            Dispatcher.UIThread.RunJobs();
            opened.Services.Dispose();
            TryDelete(targetPath);
        }
    }

    /// <summary>
    /// Creates a database the way the tab does and then keeps using it: the named file appears, the browse
    /// tree comes up on the root folder, an entry typed through the pane's own editor survives a save that
    /// does not ask for a location a second time, and the bytes on disk unlock with the master password
    /// that was typed into the masked box.
    /// </summary>
    [Fact]
    public async Task KeePass_create_from_the_tab_writes_a_vault_that_the_same_pane_saves_in_place()
    {
        var targetPath = NewTargetPath();
        var picker = new CreateTargetFilePicker(targetPath);
        var opened = await HostAsync(picker);
        var view = opened.View;
        var viewModel = opened.ViewModel;
        try
        {
            view.InPane<Button>("NewKeePassVaultButton")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            view.InPane<TextBox>("KeePassCreatePasswordBox")!.Text = CreatedVaultPassword;
            view.InPane<TextBox>("KeePassCreateConfirmPasswordBox")!.Text = CreatedVaultPassword;
            Dispatcher.UIThread.RunJobs();
            await viewModel.CreateKeePassVaultCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, picker.TargetCalls);
            Assert.Equal("database.kdbx", picker.ReceivedSuggestedFileName);
            Assert.Equal("*.kdbx", Assert.Single(picker.ReceivedFileTypes).Patterns.Single());
            Assert.True(File.Exists(targetPath));
            Assert.True(new FileInfo(targetPath).Length > 0);

            var summary = view.InPane<Border>("KeePassPreviewCard")!;
            Assert.True(summary.IsVisible, "the created database did not report itself on screen");
            Assert.True(summary.Bounds.Width > 0 && summary.Bounds.Height > 0);
            Assert.False(view.InPane<StackPanel>("KeePassCreateForm").IsVisible);
            Assert.False(viewModel.KeePassVaultIsDirty);
            Assert.False(viewModel.IsStatusMessageFailure);
            Assert.DoesNotContain(CreatedVaultPassword, viewModel.StatusMessage, StringComparison.Ordinal);

            var tree = view.InPane<VaultFolderTree>("KeePassBrowseTree")!;
            Assert.True(tree.Bounds.Width > 0 && tree.Bounds.Height > 0);
            var rootRow = Assert.Single(
                viewModel.KeePassTreeRowsPublic,
                row => row.Kind == KeePassTreeRowKind.Folder);
            Assert.Equal("Root", rootRow.Label);

            // The created database is the opened one, so the entry command next to it has to work without
            // another trip through a file dialog.
            view.InPane<Button>("NewKeePassEntryButton")!.Command!.Execute(null);
            var editor = viewModel.KeePassEditorPublic;
            Assert.NotNull(editor);
            editor!.Title = DraftTitle;
            editor.UserName = "monica@example.com";
            editor.Password = DraftSecret;
            await viewModel.ApplyKeePassEntryEditCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.KeePassVaultIsDirty);
            var unsaved = view.InPane<TextBlock>("KeePassUnsavedChangesText")!;
            Assert.True(unsaved.IsVisible);
            AssertPaintedWithoutSecrets(view.InPane<StackPanel>("KeePassImportCard")!, CreatedVaultPassword, DraftSecret);

            var saveButton = view.InPane<Button>("SaveKeePassVaultButton")!;
            Assert.Same(viewModel.SaveKeePassVaultCommand, saveButton.Command);
            await viewModel.SaveKeePassVaultCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            // One location for the life of the database: the save went where the create asked, so the
            // picker was not consulted again.
            Assert.Equal(1, picker.TargetCalls);
            Assert.False(viewModel.KeePassVaultIsDirty);
            Assert.False(unsaved.IsVisible);

            var saved = await File.ReadAllBytesAsync(
                targetPath,
                TestContext.Current.CancellationToken);
            using var reopened = await new KeePassVaultService().OpenAsync(
                saved,
                Path.GetFileName(targetPath),
                CreatedVaultPassword,
                cancellationToken: TestContext.Current.CancellationToken);
            var written = 0;
            await foreach (var detail in reopened.ReadDetailsAsync(TestContext.Current.CancellationToken))
            {
                written++;
                Assert.Equal(DraftTitle, detail.Row.Title);
                Assert.Equal("monica@example.com", detail.Row.UserName);
                // Compared, not asserted: a failing Assert.Equal would print both sides into the report.
                Assert.True(detail.Password == DraftSecret);
            }

            Assert.Equal(1, written);
        }
        finally
        {
            opened.Host.Close();
            Dispatcher.UIThread.RunJobs();
            opened.Services.Dispose();
            TryDelete(targetPath);
        }
    }

    /// <summary>
    /// Collects the strings a subtree actually renders and checks none of them carries a secret. The
    /// password fields are masked by the control itself, so their own text is proved separately; this is
    /// the label that repeats what it was handed.
    /// </summary>
    private static void AssertPaintedWithoutSecrets(Control subtree, params string[] secrets)
    {
        var painted = subtree.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsVisible)
            .Select(text => text.Text ?? "")
            .ToList();
        Assert.NotEmpty(painted);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(painted, text => text.Contains(secret, StringComparison.Ordinal));
        }
    }

    private static string NewTargetPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "monica-uitests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"keepass-create-{Guid.NewGuid():N}.kdbx");
    }

    /// <summary>
    /// Puts the KeePass tab on screen with the create picker wired in. Nothing is opened first - the tab
    /// has to be able to hand out a database to a person whose file list is empty.
    /// </summary>
    private static async Task<CreatedHost> HostAsync(IFileSystemPickerService picker)
    {
        var window = new Monica.App.MainWindow();
        var services = Monica.App.App.ConfigureServices(window, collection =>
        {
            collection.AddSingleton<IFileSystemPickerService>(picker);
        });
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        var view = new SyncImportView { DataContext = viewModel };
        var host = new Window { Width = 1280, Height = 800, Content = view };
        viewModel.SelectedSyncPage = "Import";
        host.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = view.FindControl<TabControl>("ImportSourceTabs")!;
        tabs.SelectedItem = view.FindControl<TabItem>("KeePassImportTab")!;
        Dispatcher.UIThread.RunJobs();

        return new CreatedHost(view, viewModel, host, services);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed record CreatedHost(
        SyncImportView View,
        MainWindowViewModel ViewModel,
        Window Host,
        ServiceProvider Services) : IDisposable
    {
        public void Dispose() => Services.Dispose();
    }

    /// <summary>
    /// Hands the view model one save location and records how often it was asked for it, because "the
    /// save did not come back with a dialog" is only visible as a call count.
    /// </summary>
    private sealed class CreateTargetFilePicker(string? targetPath) : IFileSystemPickerService
    {
        public int TargetCalls { get; private set; }

        public string? ReceivedSuggestedFileName { get; private set; }

        public IReadOnlyList<PlatformFilePickerFileType> ReceivedFileTypes { get; private set; } = [];

        public PlatformIntegrationCapability Capability { get; } = PlatformIntegrationService.Available(
            PlatformFeatureKeys.FilePicker,
            "Test file picker");

        public Task<PickedBinaryFile?> OpenBinaryFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<PickedBinaryFile?>(null);

        public Task<PickedTextFile?> OpenTextFileAsync(
            string title,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<PickedTextFile?>(null);

        public Task<string?> SaveTextFileAsync(
            string title,
            string suggestedFileName,
            string content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> SaveBinaryFileAsync(
            string title,
            string suggestedFileName,
            ReadOnlyMemory<byte> content,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<PickedSaveTarget?> PickSaveFileTargetAsync(
            string title,
            string suggestedFileName,
            IReadOnlyList<PlatformFilePickerFileType> fileTypes,
            CancellationToken cancellationToken = default)
        {
            TargetCalls++;
            ReceivedSuggestedFileName = suggestedFileName;
            ReceivedFileTypes = fileTypes;
            return targetPath is null
                ? Task.FromResult<PickedSaveTarget?>(null)
                : Task.FromResult<PickedSaveTarget?>(
                    new PickedSaveTarget(Path.GetFileName(targetPath), targetPath));
        }
    }
}
