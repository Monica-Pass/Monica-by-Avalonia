using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
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

            var kinds = new (VaultEntryGroup Group, ICommand Command)[]
            {
                (VaultEntryGroup.Passwords, viewModel.AddPasswordCommand),
                (VaultEntryGroup.Notes, viewModel.AddNoteCommand),
                (VaultEntryGroup.Totp, viewModel.AddTotpCommand),
                (VaultEntryGroup.Cards, viewModel.AddWalletItemCommand)
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

            foreach (var (group, command) in kinds)
            {
                viewModel.SelectVaultGroupCommand.Execute(group);
                Dispatcher.UIThread.RunJobs();

                Assert.True(presetButton.IsVisible);
                Assert.Same(command, presetButton.Command);
                Assert.False(menuButton.IsVisible);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
