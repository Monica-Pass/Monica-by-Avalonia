using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
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
        Assert.NotNull(mdbx.FindControl<Grid>("MdbxWorkspaceLayoutGrid"));
        Assert.NotNull(mdbx.FindControl<Border>("MdbxListRegion"));
        Assert.NotNull(mdbx.FindControl<ScrollViewer>("MdbxContentRegion"));
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
        Assert.False(sync.FindControl<StackPanel>("SyncSidebarOverview")!.IsVisible);

        sync.UpdateResponsiveLayoutForWidth(1100);
        mdbx.UpdateResponsiveLayoutForWidth(1100);
        databases.UpdateResponsiveLayoutForWidth(1100);

        Assert.False(sync.IsNarrowLayout);
        Assert.False(mdbx.IsNarrowLayout);
        Assert.False(databases.IsNarrowLayout);
        Assert.Equal(2, sync.FindControl<Grid>("SyncWorkspaceLayoutGrid")!.ColumnDefinitions.Count);
        Assert.Equal(2, mdbx.FindControl<Grid>("MdbxWorkspaceLayoutGrid")!.ColumnDefinitions.Count);
        Assert.Equal(2, databases.FindControl<Grid>("DatabaseWorkspaceLayoutGrid")!.ColumnDefinitions.Count);
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
        Assert.NotNull(import.FindControl<Border>("KeePassImportCard"));
        Assert.Equal('*', import.FindControl<TextBox>("KeePassImportPasswordBox")!.PasswordChar);
        Assert.NotNull(import.FindControl<Button>("SelectKeePassFileButton"));
        Assert.NotNull(import.FindControl<Button>("PreviewKeePassImportButton"));
        Assert.NotNull(import.FindControl<Button>("ImportKeePassVaultButton"));
        Assert.NotNull(import.FindControl<Button>("CancelKeePassImportButton"));
        Assert.NotNull(import.FindControl<Border>("BitwardenImportCard"));
        Assert.NotNull(import.FindControl<Button>("SelectBitwardenJsonFileButton"));
        Assert.NotNull(import.FindControl<Button>("PreviewBitwardenJsonImportButton"));
        Assert.NotNull(import.FindControl<Button>("ImportBitwardenJsonVaultButton"));
        Assert.NotNull(import.FindControl<Button>("CancelBitwardenImportButton"));

        var databaseWorkbench = new DatabaseWorkbenchView();
        Assert.NotNull(databaseWorkbench.FindControl<Button>("OpenKeePassImportButton"));
        Assert.NotNull(databaseWorkbench.FindControl<Button>("OpenBitwardenImportButton"));

        var restore = backup.FindControl<Button>("RestoreSelectedBackupButton");
        var delete = backup.FindControl<Button>("DeleteSelectedBackupButton");
        Assert.NotNull(restore);
        Assert.NotNull(delete);
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
}
