using Avalonia.Controls;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Monica.App.Features.Vault;
using Monica.App.Features.Wallet;
using Monica.App.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class WalletWorkflowUiTests
{
    public WalletWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    // The card page owned a list, an inspector and a workbench. The library keeps the workbench and
    // takes the rest away, so the workbench has to answer for the identity card on its own.
    [Fact]
    public void Wallet_workbench_carries_the_identity_surface_and_its_own_single_scroll()
    {
        var xaml = File.ReadAllText(XamlSource.PathOf("WalletWorkbenchView.axaml"));

        Assert.Contains("Classes=\"walletIdentitySurface\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SelectedWalletDetails.KindText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SelectedWalletDetails.PrimaryText}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(xaml, "<ScrollViewer"));
        Assert.DoesNotContain("BackToWalletListButton", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wallet_workbench_is_the_only_card_surface_the_library_opens()
    {
        using var library = LibraryUiHarness.Open("Cards");
        library.ViewModel.WalletItems.Add(new Monica.Core.Models.SecureItem
        {
            Id = 81,
            ItemType = Monica.Core.Models.VaultItemType.BankCard,
            Title = "Travel card"
        });
        library.Settle();

        library.SelectFirstEntry();

        Assert.Equal(VaultSurface.Card, library.ViewModel.SelectedVaultSurface);
        Assert.IsType<WalletWorkbenchView>(library.SurfaceHost.Content);
        Assert.Single(library.Window.GetVisualDescendants().OfType<WalletWorkbenchView>());
        Assert.NotNull(library.ViewModel.SelectedWalletItem);
        Assert.NotNull(library.ViewModel.SelectedWalletDetails);
    }

    [Fact]
    public void Wallet_add_and_delete_are_owned_by_the_library_not_by_the_workbench()
    {
        using var library = LibraryUiHarness.Open();
        var create = library.Workspace.FindControl<Button>("VaultCreateMenuButton")!;
        var flyout = Assert.IsType<MenuFlyout>(create.Flyout);
        flyout.ShowAt(create);
        library.Settle();

        var addItem = Assert.Single(
            LibraryUiHarness.MenuItems(flyout),
            item => Equals(item.Command, library.ViewModel.AddWalletItemCommand));
        Assert.NotNull(addItem.Command);
        flyout.Hide();

        var workbenchXaml = File.ReadAllText(XamlSource.PathOf("WalletWorkbenchView.axaml"));
        Assert.DoesNotContain("DeleteWalletItemCommand", workbenchXaml, StringComparison.Ordinal);
        Assert.NotNull(library.Tree.DeleteEntryCommand);
        Assert.Same(library.ViewModel.DeleteSelectedVaultEntryCommand, library.Tree.DeleteEntryCommand);
    }

    [Fact]
    public void Wallet_search_and_result_announcements_are_localized_for_english_and_chinese()
    {
        var localization = new LocalizationService();

        Assert.Contains("Ctrl+F", localization.Get("VaultSearchHelp"), StringComparison.Ordinal);
        Assert.Equal("{0} visible · {1} total", localization.Get("VaultFilteredStatusFormat"));

        localization.SetLanguage("zh-CN");

        Assert.Contains("Ctrl+F", localization.Get("VaultSearchHelp"), StringComparison.Ordinal);
        Assert.Equal("显示 {0} 条 · 共 {1} 条", localization.Get("VaultFilteredStatusFormat"));
    }

    [Fact]
    public void Wallet_editor_masks_sensitive_inputs_and_exposes_visibility_controls()
    {
        var editor = new WalletItemEditorDialog();

        Assert.NotNull(editor.FindControl<ScrollViewer>("WalletEditorFormScrollViewer"));
        Assert.NotNull(editor.FindControl<StackPanel>("WalletEditorPrimaryForm"));
        Assert.NotNull(editor.FindControl<TextBox>("DocumentNumberInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleDocumentNumberVisibilityButton"));
        Assert.NotNull(editor.FindControl<TextBox>("CardNumberInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleCardNumberVisibilityButton"));
        Assert.NotNull(editor.FindControl<TextBox>("CardCvvInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleCardCvvVisibilityButton"));
        Assert.Equal(40, editor.FindControl<Button>("ToggleCardCvvVisibilityButton")!.Width);
    }

    [Fact]
    public void Wallet_editor_exposes_android_extended_types_as_desktop_panels()
    {
        var editor = new WalletItemEditorDialog();
        var xaml = File.ReadAllText(XamlSource.PathOf("WalletItemEditorDialog.axaml"));

        Assert.NotNull(editor.FindControl<StackPanel>("BillingAddressEditorPanel"));
        Assert.NotNull(editor.FindControl<StackPanel>("PaymentAccountEditorPanel"));
        Assert.Contains("IsVisible=\"{Binding IsBillingAddress}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsPaymentAccount}\"", xaml, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value) =>
        XamlSource.CountOccurrences(text, value);
}
