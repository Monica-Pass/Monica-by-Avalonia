using Avalonia.Controls;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Monica.App.Features.Authenticator;
using Monica.App.Features.Vault;
using Monica.Core.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class AuthenticatorWorkflowUiTests
{
    public AuthenticatorWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Authenticator_console_exposes_the_code_and_the_actions_a_user_needs()
    {
        var console = new AuthenticatorCodeConsoleView();

        Assert.NotNull(console.FindControl<TextBlock>("AuthenticatorCurrentCode"));
        Assert.NotNull(console.FindControl<Button>("CopyAuthenticatorCodeButton"));
        Assert.NotNull(console.FindControl<Button>("AdvanceTotpButton"));
    }

    [Fact]
    public void Authenticator_console_shows_the_live_code_for_a_selected_entry()
    {
        using var library = LibraryUiHarness.Open("Totp");
        library.ViewModel.TotpItems.Add(new Monica.Core.Models.SecureItem
        {
            Id = 71,
            ItemType = Monica.Core.Models.VaultItemType.Totp,
            Title = "Two factor",
            ItemData = TotpDataResolver.ToItemData(
                TotpDataResolver.FromAuthenticatorKey("JBSWY3DPEHPK3PXP", "Two factor", "desktop")!)
        });
        library.Settle();

        library.SelectFirstEntry();

        var console = Assert.IsType<AuthenticatorCodeConsoleView>(library.SurfaceHost.Content);
        Assert.Equal(VaultSurface.Totp, library.ViewModel.SelectedVaultSurface);
        Assert.Same(library.ViewModel, console.DataContext);
        Assert.NotNull(library.ViewModel.SelectedTotpItem);
        Assert.Same(console, Assert.Single(
            library.Window.GetVisualDescendants().OfType<AuthenticatorCodeConsoleView>()));
        var copy = console.FindControl<Button>("CopyAuthenticatorCodeButton")!;
        Assert.Same(library.ViewModel.CopyTotpCommand, copy.Command);
        Assert.Same(library.ViewModel.SelectedTotpItem, copy.CommandParameter);
    }

    [Fact]
    public void Authenticator_scan_action_is_offered_once_and_only_in_its_own_preset()
    {
        using var library = LibraryUiHarness.Open("Totp");
        var more = library.Workspace.FindControl<Button>("VaultMoreButton")!;
        var flyout = Assert.IsType<MenuFlyout>(more.Flyout);

        // A preset switch closes an open menu, so each state has to be read from a menu the user
        // could actually see: an item left over from a hidden flyout reports itself invisible.
        MenuItem ShowAndFindScanItem()
        {
            flyout.ShowAt(more);
            library.Settle();
            return Assert.Single(
                LibraryUiHarness.MenuItems(flyout),
                item => Equals(item.Command, library.ViewModel.ScanTotpQrCommand));
        }

        Assert.True(ShowAndFindScanItem().IsVisible);
        flyout.Hide();

        library.ViewModel.SelectSectionCommand.Execute("Vault");
        library.Settle();
        Assert.False(ShowAndFindScanItem().IsVisible);
        flyout.Hide();
    }

    [Fact]
    public void Authenticator_hotp_counter_stays_visible_only_for_a_counter_based_entry()
    {
        var xaml = File.ReadAllText(XamlSource.PathOf("AuthenticatorCodeConsoleView.axaml"));

        Assert.Contains("Command=\"{Binding AdvanceTotpCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding SelectedTotpDetails.IsCounterBased}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedTotpDetails.CounterText", xaml, StringComparison.Ordinal);
    }

    // The authenticator header used to own favourite, delete and clear-selection buttons; the library
    // batch menu is the one place left that carries them, so it has to answer to the same commands.
    [Fact]
    public void Authenticator_batch_actions_route_through_the_library_batch_menu()
    {
        using var library = LibraryUiHarness.Open("Totp");
        library.ViewModel.TotpItems.Add(new Monica.Core.Models.SecureItem
        {
            Id = 72,
            ItemType = Monica.Core.Models.VaultItemType.Totp,
            Title = "Batchable code"
        });
        library.Settle();
        library.ViewModel.SelectAllVaultRowsCommand.Execute(null);
        library.Settle();

        var batch = library.Workspace.FindControl<Button>("VaultBatchButton")!;
        Assert.True(batch.IsVisible);
        var flyout = Assert.IsType<MenuFlyout>(batch.Flyout);
        flyout.ShowAt(batch);
        library.Settle();
        var items = LibraryUiHarness.MenuItems(flyout).ToArray();

        var favorite = items.Single(item => Equals(item.Command, library.ViewModel.FavoriteVaultBatchCommand));
        var delete = items.Single(item => Equals(item.Command, library.ViewModel.DeleteVaultBatchCommand));
        var clear = items.Single(item => Equals(item.Command, library.ViewModel.ClearVaultBatchSelectionCommand));
        Assert.True(favorite.IsVisible);
        Assert.True(delete.IsVisible);
        Assert.True(library.ViewModel.HasVaultBatchSelection);

        clear.Command!.Execute(clear.CommandParameter);
        library.Settle();
        Assert.False(library.ViewModel.HasVaultBatchSelection);
        flyout.Hide();
    }

    [Fact]
    public void Authenticator_console_owns_no_scroll_surface_of_its_own()
    {
        var xaml = File.ReadAllText(XamlSource.PathOf("AuthenticatorCodeConsoleView.axaml"));

        // The library detail pane is the only scroller, so the console cannot start a nested one.
        Assert.DoesNotContain("<ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"totpCodeSurface\"", xaml, StringComparison.Ordinal);
    }
}
