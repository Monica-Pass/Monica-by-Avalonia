using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.DatabaseManagement;
using Monica.App.Features.Mdbx;
using Monica.App.Features.Sync;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class StoragePlatformWorkflowUiTests
{
    public StoragePlatformWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Storage_pages_use_dedicated_command_and_collection_components()
    {
        var sync = new SyncWorkspaceView();
        var mdbx = new MdbxWorkspaceView();
        var databases = new DatabaseManagementWorkspaceView();

        Assert.IsType<SyncCommandBarView>(sync.FindControl<SyncCommandBarView>("SyncCommandBar"));
        Assert.IsType<SyncPageHostView>(sync.FindControl<SyncPageHostView>("SyncPageHost"));
        Assert.IsType<MdbxCommandBarView>(mdbx.FindControl<MdbxCommandBarView>("MdbxCommandBar"));
        Assert.IsType<MdbxDatabaseListView>(mdbx.FindControl<MdbxDatabaseListView>("MdbxDatabaseListView"));
        Assert.IsType<DatabaseCommandBarView>(databases.FindControl<DatabaseCommandBarView>("DatabaseCommandBar"));
        Assert.IsType<DatabaseSourceListView>(databases.FindControl<DatabaseSourceListView>("DatabaseSourceListView"));
    }

    [Fact]
    public void Storage_command_bars_own_page_actions_without_list_header_duplicates()
    {
        var syncCommandXaml = ReadFeatureFile("Sync", "SyncCommandBarView.axaml");
        var mdbxCommandXaml = ReadFeatureFile("Mdbx", "MdbxCommandBarView.axaml");
        var mdbxListXaml = ReadFeatureFile("Mdbx", "MdbxDatabaseListView.axaml");
        var databaseCommandXaml = ReadFeatureFile("DatabaseManagement", "DatabaseCommandBarView.axaml");
        var databaseListXaml = ReadFeatureFile("DatabaseManagement", "DatabaseSourceListView.axaml");

        Assert.Contains("<fa:FACommandBar", syncCommandXaml, StringComparison.Ordinal);
        Assert.Contains("TestWebDavConnectionCommand", syncCommandXaml, StringComparison.Ordinal);
        Assert.Contains("CreateWebDavBackupCommand", syncCommandXaml, StringComparison.Ordinal);

        Assert.Contains("<fa:FACommandBar", mdbxCommandXaml, StringComparison.Ordinal);
        Assert.Contains("CreateMdbxVaultCommand", mdbxCommandXaml, StringComparison.Ordinal);
        Assert.Contains("RefreshMdbxVaultsCommand", mdbxCommandXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshMdbxVaultsCommand", mdbxListXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", mdbxListXaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", mdbxListXaml, StringComparison.Ordinal);

        Assert.Contains("<fa:FACommandBar", databaseCommandXaml, StringComparison.Ordinal);
        Assert.Contains("LoadCommand", databaseCommandXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadCommand", databaseListXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ScrollViewer", databaseListXaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", databaseListXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Storage_pages_keep_explicit_wide_medium_and_narrow_layout_states()
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
        Assert.Equal(2, Grid.GetRow(sync.FindControl<SyncCommandBarView>("SyncCommandBar")!));
        Assert.Equal(2, Grid.GetRow(mdbx.FindControl<MdbxCommandBarView>("MdbxCommandBar")!));
        Assert.Equal(2, Grid.GetRow(databases.FindControl<DatabaseCommandBarView>("DatabaseCommandBar")!));

        sync.UpdateResponsiveLayoutForWidth(900);
        mdbx.UpdateResponsiveLayoutForWidth(900);
        databases.UpdateResponsiveLayoutForWidth(900);
        Assert.True(sync.IsMediumLayout);
        Assert.True(mdbx.IsMediumLayout);
        Assert.True(databases.IsMediumLayout);

        sync.UpdateResponsiveLayoutForWidth(1200);
        mdbx.UpdateResponsiveLayoutForWidth(1200);
        databases.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(sync.IsNarrowLayout);
        Assert.False(sync.IsMediumLayout);
        Assert.False(mdbx.IsNarrowLayout);
        Assert.False(mdbx.IsMediumLayout);
        Assert.False(databases.IsNarrowLayout);
        Assert.False(databases.IsMediumLayout);
        Assert.Equal(0, Grid.GetRow(sync.FindControl<SyncCommandBarView>("SyncCommandBar")!));
        Assert.Equal(0, Grid.GetRow(mdbx.FindControl<MdbxCommandBarView>("MdbxCommandBar")!));
        Assert.Equal(0, Grid.GetRow(databases.FindControl<DatabaseCommandBarView>("DatabaseCommandBar")!));
    }

    [Fact]
    public void Sync_page_host_constructs_only_the_selected_page()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        var host = new SyncPageHostView { DataContext = viewModel };
        var content = host.FindControl<ContentControl>("SyncPageContent")!;

        Assert.IsType<SyncConfigurationView>(content.Content);

        viewModel.SelectedSyncPage = "Backup";
        Assert.IsType<SyncBackupView>(content.Content);
    }

    private static string ReadFeatureFile(string feature, string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "Features", feature, fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException($"Could not locate {feature}/{fileName} from the test output directory.");
    }
}
