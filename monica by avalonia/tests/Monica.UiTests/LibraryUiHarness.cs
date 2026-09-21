using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.App.ViewModels;

namespace Monica.UiTests;

/// Opens the library the way the shell does: unlocked, on screen and with a preset chosen, so a test
/// probes the surface a user actually meets instead of a view model that no page hosts.
internal sealed class LibraryUiHarness : IDisposable
{
    private LibraryUiHarness(Window window, ServiceProvider services, MainWindowViewModel viewModel)
    {
        Window = window;
        Services = services;
        ViewModel = viewModel;
        Workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
        SurfaceHost = Workspace.FindControl<ContentControl>("VaultSurfaceHost")!;
        Tree = Workspace.FindControl<VaultFolderTree>("VaultTree")!;
    }

    public Window Window { get; }

    public ServiceProvider Services { get; }

    public MainWindowViewModel ViewModel { get; }

    public VaultWorkspaceView Workspace { get; }

    public ContentControl SurfaceHost { get; }

    public VaultFolderTree Tree { get; }

    public static LibraryUiHarness Open(string section = VaultPresets.LibrarySection, double width = 1280)
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
        var window = new Monica.App.MainWindow { Width = width, Height = 820 };
        var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        window.DataContext = viewModel;
        viewModel.IsUnlocked = true;
        viewModel.SelectSectionCommand.Execute(section);
        Dispatcher.UIThread.RunJobs();
        return new LibraryUiHarness(window, services, viewModel);
    }

    public void Settle() => Dispatcher.UIThread.RunJobs();

    /// The search box lives inside the shared SearchField control, so it is no longer a direct named
    /// element of the workspace; reach it through the control's own inner text box.
    public static TextBox SearchBoxIn(VaultWorkspaceView workspace) =>
        workspace.FindControl<SearchField>("VaultSearchField")!.InnerSearchBox!;

    public static Button ClearButtonIn(VaultWorkspaceView workspace) =>
        workspace.FindControl<SearchField>("VaultSearchField")!.InnerClearButton!;

    /// A search rebuild is coalesced behind a timer, so a test that wants the tree rather than the
    /// filter has to let real time pass before it drains the dispatcher.
    public async Task AwaitTreeRefresh()
    {
        await Task.Delay(450);
        Settle();
    }

    public VaultTreeEntryRow SelectFirstEntry()
    {
        var row = ViewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().First();
        ViewModel.SelectedVaultRow = row;
        Settle();
        return row;
    }

    public static IEnumerable<MenuItem> MenuItems(MenuFlyout flyout) =>
        flyout.Items.OfType<MenuItem>().SelectMany(item => new[] { item }.Concat(Children(item)));

    private static IEnumerable<MenuItem> Children(MenuItem item) =>
        item.Items.OfType<MenuItem>().SelectMany(child => new[] { child }.Concat(Children(child)));

    public void Dispose()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
        Window.Close();
        Services.Dispose();
    }
}
