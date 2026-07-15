using Avalonia.Controls;
using Monica.App.Features.Wallet;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class WalletWorkflowUiTests
{
    public WalletWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Wallet_workspace_exposes_search_selection_and_empty_state_actions()
    {
        var view = new WalletWorkspaceView();

        Assert.NotNull(view.FindControl<TextBox>("WalletSearchBox"));
        Assert.NotNull(view.FindControl<Button>("WalletSearchClearButton"));
        Assert.NotNull(view.FindControl<ListBox>("WalletItemList"));
        Assert.NotNull(view.FindControl<StackPanel>("WalletEmptyState"));
        Assert.NotNull(view.FindControl<Button>("EmptyWalletAddButton"));
        Assert.NotNull(view.FindControl<Button>("EmptyWalletClearSearchButton"));
        Assert.NotNull(view.FindControl<Button>("WalletMoreActionsButton"));
    }

    [Fact]
    public void Wallet_workspace_exposes_wide_medium_and_narrow_regions()
    {
        var view = new WalletWorkspaceView();

        Assert.NotNull(view.FindControl<Grid>("WalletMasterDetailGrid"));
        Assert.NotNull(view.FindControl<Border>("WalletListRegion"));
        Assert.NotNull(view.FindControl<Border>("WalletWorkbenchRegion"));
        Assert.NotNull(view.FindControl<Border>("WalletInspectorRegion"));
        var workbench = Assert.IsType<WalletWorkbenchView>(
            view.FindControl<WalletWorkbenchView>("WalletWorkbench"));
        Assert.NotNull(workbench.FindControl<Button>("BackToWalletListButton"));

        view.UpdateResponsiveLayoutForWidth(680);
        Assert.True(view.IsNarrowLayout);
        Assert.True(view.FindControl<Border>("WalletListRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("WalletWorkbenchRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("WalletInspectorRegion")!.IsVisible);

        view.UpdateResponsiveLayoutForWidth(900);
        Assert.True(view.IsMediumLayout);
        Assert.True(view.FindControl<Border>("WalletListRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("WalletWorkbenchRegion")!.IsVisible);
        Assert.False(view.FindControl<Border>("WalletInspectorRegion")!.IsVisible);

        view.UpdateResponsiveLayoutForWidth(1200);
        Assert.False(view.IsNarrowLayout);
        Assert.False(view.IsMediumLayout);
        Assert.True(view.FindControl<Border>("WalletListRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("WalletWorkbenchRegion")!.IsVisible);
        Assert.True(view.FindControl<Border>("WalletInspectorRegion")!.IsVisible);
    }

    [Fact]
    public void Wallet_editor_masks_sensitive_inputs_and_exposes_visibility_controls()
    {
        var editor = new WalletItemEditorDialog();

        Assert.NotNull(editor.FindControl<TextBox>("DocumentNumberInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleDocumentNumberVisibilityButton"));
        Assert.NotNull(editor.FindControl<TextBox>("CardNumberInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleCardNumberVisibilityButton"));
        Assert.NotNull(editor.FindControl<TextBox>("CardCvvInput"));
        Assert.NotNull(editor.FindControl<Button>("ToggleCardCvvVisibilityButton"));
    }
}
