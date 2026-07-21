using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.DatabaseManagement;
using Monica.App.Features.Mdbx;
using Monica.App.Features.Sync;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class StorageWorkflowUiTests
{
    public StorageWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Storage_workspaces_expose_named_operational_regions()
    {
        var sync = new SyncWorkspaceView();
        var mdbx = new MdbxWorkspaceView();
        var mdbxWorkbench = new MdbxWorkbenchView();
        var syncSources = new SyncSourcesView();
        var databases = new DatabaseManagementWorkspaceView();

        Assert.NotNull(sync.FindControl<Grid>("SyncWorkspaceLayoutGrid"));
        Assert.NotNull(sync.FindControl<Border>("SyncSidebarRegion"));
        Assert.NotNull(sync.FindControl<ScrollViewer>("SyncContentRegion"));
        Assert.NotNull(sync.FindControl<Border>("SyncOperationsCommandSurface"));
        Assert.NotNull(sync.FindControl<ScrollViewer>("SyncHealthStatusRegion"));
        Assert.NotNull(mdbx.FindControl<Grid>("MdbxWorkspaceLayoutGrid"));
        Assert.NotNull(mdbx.FindControl<Border>("MdbxListRegion"));
        Assert.NotNull(mdbx.FindControl<ScrollViewer>("MdbxContentRegion"));
        Assert.NotNull(mdbx.FindControl<Border>("MdbxEngineCommandSurface"));
        Assert.NotNull(mdbx.FindControl<Grid>("MdbxWorkbenchLayoutGrid"));
        Assert.NotNull(mdbx.FindControl<StackPanel>("MdbxSectionNavigator"));
        Assert.NotNull(mdbxWorkbench.FindControl<Button>("SyncMdbxDatabaseButton"));
        Assert.NotNull(mdbxWorkbench.FindControl<Button>("KeepLocalRemoteMdbxButton"));
        Assert.NotNull(mdbxWorkbench.FindControl<Button>("UseRemoteMdbxButton"));
        Assert.NotNull(syncSources.FindControl<Button>("OneDriveConnectButton"));
        Assert.NotNull(syncSources.FindControl<Button>("OneDriveDisconnectButton"));
        Assert.NotNull(syncSources.FindControl<Border>("OneDriveDeviceCodeCard"));
        Assert.NotNull(syncSources.FindControl<TextBlock>("OneDriveDeviceCodeText"));
        Assert.NotNull(databases.FindControl<Grid>("DatabaseWorkspaceLayoutGrid"));
        Assert.NotNull(databases.FindControl<Border>("DatabaseListRegion"));
        Assert.NotNull(databases.FindControl<ScrollViewer>("DatabaseContentRegion"));
        Assert.NotNull(databases.FindControl<Border>("DatabaseOperationsCommandSurface"));
        Assert.NotNull(databases.FindControl<Grid>("DatabaseOperationNavigator"));
    }

    [Fact]
    public void Storage_workspaces_reflow_to_single_column_at_narrow_width()
    {
        var sync = new SyncWorkspaceView();
        var mdbx = new MdbxWorkspaceView();
        var databases = new DatabaseManagementWorkspaceView();

        sync.UpdateResponsiveLayoutForWidth(680);
        mdbx.UpdateResponsiveLayoutForWidth(680);
        databases.UpdateResponsiveLayoutForWidth(680);

        Assert.True(sync.IsNarrowLayout);
        Assert.True(mdbx.IsNarrowLayout);
        Assert.True(databases.IsNarrowLayout);
        Assert.Single(sync.FindControl<Grid>("SyncWorkspaceLayoutGrid")!.ColumnDefinitions);
        Assert.Single(mdbx.FindControl<Grid>("MdbxWorkspaceLayoutGrid")!.ColumnDefinitions);
        Assert.Single(databases.FindControl<Grid>("DatabaseWorkspaceLayoutGrid")!.ColumnDefinitions);
        Assert.Equal(4, Grid.GetRow(sync.FindControl<ScrollViewer>("SyncHealthStatusRegion")!));
        Assert.Single(mdbx.FindControl<Grid>("MdbxWorkbenchLayoutGrid")!.ColumnDefinitions);

        sync.UpdateResponsiveLayoutForWidth(1100);
        mdbx.UpdateResponsiveLayoutForWidth(1100);
        databases.UpdateResponsiveLayoutForWidth(1100);

        Assert.False(sync.IsNarrowLayout);
        Assert.False(mdbx.IsNarrowLayout);
        Assert.False(databases.IsNarrowLayout);
        Assert.Equal(2, sync.FindControl<Grid>("SyncWorkspaceLayoutGrid")!.ColumnDefinitions.Count);
        Assert.Equal(2, mdbx.FindControl<Grid>("MdbxWorkspaceLayoutGrid")!.ColumnDefinitions.Count);
        Assert.Equal(2, databases.FindControl<Grid>("DatabaseWorkspaceLayoutGrid")!.ColumnDefinitions.Count);
        Assert.Equal(2, mdbx.FindControl<Grid>("MdbxWorkbenchLayoutGrid")!.ColumnDefinitions.Count);
    }

    [Fact]
    public void Sync_sensitive_fields_and_icon_commands_are_accessible()
    {
        var configuration = new SyncConfigurationView();
        var backup = new SyncBackupView();
        var export = new SyncExportView();

        Assert.Equal('*', configuration.FindControl<TextBox>("WebDavPasswordBox")!.PasswordChar);
        Assert.Equal('*', backup.FindControl<TextBox>("WebDavBackupPasswordBox")!.PasswordChar);
        Assert.True(export.FindControl<TextBox>("JsonExportPreviewBox")!.IsReadOnly);
        Assert.True(export.FindControl<ProgressBar>("ExportBusyIndicator")!.IsIndeterminate);
        Assert.NotNull(export.FindControl<StackPanel>("ExportOperationContent"));

        var import = new SyncImportView();
        Assert.True(import.FindControl<ProgressBar>("ImportBusyIndicator")!.IsIndeterminate);
        Assert.NotNull(import.FindControl<StackPanel>("ImportOperationContent"));
        Assert.Equal('*', import.FindControl<TextBox>("AegisImportPasswordBox")!.PasswordChar);
        Assert.NotNull(import.FindControl<StackPanel>("KeePassImportCard"));
        Assert.Equal('*', import.FindControl<TextBox>("KeePassImportPasswordBox")!.PasswordChar);
        Assert.NotNull(import.FindControl<Button>("SelectKeePassFileButton"));
        Assert.NotNull(import.FindControl<Button>("PreviewKeePassImportButton"));
        Assert.NotNull(import.FindControl<Button>("ImportKeePassVaultButton"));
        Assert.NotNull(import.FindControl<Button>("CancelKeePassImportButton"));
        Assert.NotNull(import.FindControl<StackPanel>("BitwardenImportCard"));
        Assert.NotNull(import.FindControl<Button>("SelectBitwardenJsonFileButton"));
        Assert.NotNull(import.FindControl<Button>("PreviewBitwardenJsonImportButton"));
        Assert.NotNull(import.FindControl<Button>("ImportBitwardenJsonVaultButton"));
        Assert.NotNull(import.FindControl<Button>("CancelBitwardenImportButton"));

        var importTabs = import.FindControl<TabControl>("ImportSourceTabs");
        var exportTabs = export.FindControl<TabControl>("ExportFormatTabs");
        Assert.NotNull(importTabs);
        Assert.NotNull(exportTabs);
        Assert.Equal(7, importTabs.ItemCount);
        Assert.Equal(5, exportTabs.ItemCount);

        import.UpdateResponsiveLayoutForWidth(680);
        export.UpdateResponsiveLayoutForWidth(680);
        Assert.True(import.IsNarrowLayout);
        Assert.True(export.IsNarrowLayout);
        Assert.Equal(Dock.Top, importTabs.TabStripPlacement);
        Assert.Equal(Dock.Top, exportTabs.TabStripPlacement);

        import.UpdateResponsiveLayoutForWidth(900);
        export.UpdateResponsiveLayoutForWidth(900);
        Assert.Equal(Dock.Left, importTabs.TabStripPlacement);
        Assert.Equal(Dock.Left, exportTabs.TabStripPlacement);

        var importXaml = ReadSyncFeatureFile("SyncImportView.axaml");
        var exportXaml = ReadSyncFeatureFile("SyncExportView.axaml");
        Assert.DoesNotContain("FASettingsExpander", importXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FASettingsExpander", exportXaml, StringComparison.Ordinal);

        var databaseWorkbench = new DatabaseWorkbenchView();
        Assert.NotNull(databaseWorkbench.FindControl<FAInfoBar>("LegacyBusinessDataNotice"));
        Assert.NotNull(databaseWorkbench.FindControl<Button>("OpenKeePassImportButton"));
        Assert.NotNull(databaseWorkbench.FindControl<Button>("OpenBitwardenImportButton"));

        var restore = backup.FindControl<Button>("RestoreSelectedBackupButton");
        var delete = backup.FindControl<Button>("DeleteSelectedBackupButton");
        Assert.NotNull(restore);
        Assert.NotNull(delete);
    }

    [Fact]
    public void Legacy_business_data_notice_exposes_a_visible_persistent_dismiss_action()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        viewModel.HasPendingLegacyBusinessData = true;
        viewModel.SelectedDatabaseManagementPage = "Overview";
        var workbench = new DatabaseWorkbenchView { DataContext = viewModel };
        var host = new Window
        {
            Width = 1200,
            Height = 800,
            Content = workbench
        };
        host.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();

            var notice = workbench.FindControl<FAInfoBar>("LegacyBusinessDataNotice");
            var dismiss = workbench.FindControl<Button>("DismissLegacyBusinessDataNoticeButton");
            Assert.NotNull(notice);
            Assert.NotNull(dismiss);
            Assert.False(notice.IsClosable);
            Assert.Same(dismiss, notice.ActionButton);
            Assert.Same(viewModel.DismissLegacyBusinessDataNoticeCommand, dismiss.Command);
            Assert.True(dismiss.Bounds.Width > 0);
            Assert.True(dismiss.Bounds.Height > 0);

            dismiss.Command!.Execute(dismiss.CommandParameter);

            Assert.False(viewModel.HasPendingLegacyBusinessData);
        }
        finally
        {
            host.Close();
        }
    }

    [Fact]
    public void Webdav_backup_encryption_is_presented_as_mandatory()
    {
        var backup = new SyncBackupView();

        var encryptionItem = backup.FindControl<FASettingsExpanderItem>("WebDavBackupEncryptionItem");
        Assert.NotNull(encryptionItem);
        Assert.IsNotType<ToggleSwitch>(encryptionItem.Footer);
        Assert.NotNull(backup.FindControl<Border>("WebDavBackupEncryptionStatus"));
    }

    [Fact]
    public void Sync_webdav_operations_expose_accessible_progress_feedback()
    {
        var sync = new SyncWorkspaceView();

        Assert.NotNull(sync.FindControl<Border>("WebDavOperationProgressRegion"));
        Assert.True(sync.FindControl<ProgressBar>("WebDavOperationProgressBar")!.IsIndeterminate);
        Assert.NotNull(sync.FindControl<TextBlock>("WebDavOperationStageText"));
    }

    private static string ReadSyncFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "Features", "Sync", fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException($"Could not locate Sync/{fileName} from the test output directory.");
    }
}
