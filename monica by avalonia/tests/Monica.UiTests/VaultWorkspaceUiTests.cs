using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Vault;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class VaultWorkspaceUiTests
{
    public VaultWorkspaceUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Library_header_creates_the_kind_the_active_filter_names()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.SelectSectionCommand.Execute("Vault");
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var presetButton = workspace.FindControl<Button>("VaultCreatePresetButton")!;
            var menuButton = workspace.FindControl<Button>("VaultCreateMenuButton")!;

            var kinds = new (string Section, VaultEntryGroup Group, ICommand Command)[]
            {
                ("Passwords", VaultEntryGroup.Passwords, viewModel.AddPasswordCommand),
                ("Notes", VaultEntryGroup.Notes, viewModel.AddNoteCommand),
                ("Totp", VaultEntryGroup.Totp, viewModel.AddTotpCommand),
                ("Cards", VaultEntryGroup.Cards, viewModel.AddWalletItemCommand)
            };

            // With every kind in view none of them is the one meant, so the header has to ask — and a
            // closed flyout has realized nothing, so it has to be opened before its bindings read back.
            Assert.False(presetButton.IsVisible);
            Assert.Null(presetButton.Command);
            Assert.True(menuButton.IsVisible);
            var flyout = Assert.IsType<MenuFlyout>(menuButton.Flyout);
            flyout.ShowAt(menuButton);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(
                kinds.Select(kind => kind.Command),
                flyout.Items.OfType<MenuItem>().Select(item => item.Command).ToArray());
            flyout.Hide();

            foreach (var (section, group, command) in kinds)
            {
                viewModel.SelectSectionCommand.Execute(section);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(group, viewModel.VaultGroup);
                Assert.True(presetButton.IsVisible);
                Assert.Same(command, presetButton.Command);
                Assert.False(menuButton.IsVisible);
                var filter = new VaultTreeFilter(group);
                Assert.All(
                    viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>(),
                    row => Assert.True(filter.Matches(row.Kind)));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Every_type_section_is_one_page()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;

            foreach (var section in new[] { "Vault", "Passwords", "Notes", "Totp", "Cards", "Vault" })
            {
                viewModel.SelectSectionCommand.Execute(section);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(section, viewModel.SelectedSection);
                Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
                var host = Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>());
                Assert.Equal(new[] { "Vault" }, host.CreatedSections);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Library_escape_unwinds_search_then_selection_then_favorites()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry { Title = "Alpha mail", IsFavorite = true });
            viewModel.Passwords.Add(new Monica.Core.Models.PasswordEntry { Title = "Beta bank" });
            viewModel.SelectSectionCommand.Execute("Passwords");
            viewModel.VaultFavoritesOnly = true;
            viewModel.VaultSearchText = "Alpha";
            Dispatcher.UIThread.RunJobs();

            var workspace = Assert.Single(window.GetVisualDescendants().OfType<VaultWorkspaceView>());
            var searchBox = workspace.FindControl<TextBox>("VaultSearchBox")!;
            searchBox.Focus();

            Assert.True(TryLibraryKey(workspace, viewModel, searchBox, Key.Escape));
            Assert.Empty(viewModel.VaultSearchText);
            Assert.True(viewModel.VaultFavoritesOnly);

            viewModel.SelectedVaultRow = viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().First();
            Dispatcher.UIThread.RunJobs();
            Assert.True(TryLibraryKey(workspace, viewModel, searchBox, Key.Escape));
            Assert.Null(viewModel.SelectedVaultRow);

            Assert.True(TryLibraryKey(workspace, viewModel, searchBox, Key.Escape));
            Assert.False(viewModel.VaultFavoritesOnly);
            Assert.False(TryLibraryKey(workspace, viewModel, searchBox, Key.Escape));
        }
        finally
        {
            window.Close();
        }
    }

    private static bool TryLibraryKey(
        VaultWorkspaceView workspace,
        MainWindowViewModel viewModel,
        Control source,
        Key key)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = source,
            Key = key
        };
        workspace.TryHandleShortcut(viewModel, args);
        return args.Handled;
    }
}
