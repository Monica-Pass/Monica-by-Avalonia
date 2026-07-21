using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.Tests;

public sealed class SecureItemCategoryEditorTests
{
    private static readonly Category Work = new() { Id = 1, Name = "Work", SortOrder = 1 };
    private static readonly Category Production = new() { Id = 2, Name = "Work/Production", SortOrder = 2 };

    [Fact]
    public void Totp_editor_projects_nested_categories_and_writes_selection()
    {
        var source = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Deploy",
            ItemData = "{\"secret\":\"JBSWY3DPEHPK3PXP\"}",
            CategoryId = Production.Id
        };
        var editor = new TotpEditorViewModel(new LocalizationService(), source, [Work, Production]);

        Assert.Equal("Production", editor.SelectedCategory?.FolderDisplayName);
        Assert.Equal(1, editor.SelectedCategory?.Level);

        editor.SelectedCategory = editor.CategoryOptions.Single(option => option.Id == Work.Id);
        var updated = editor.ApplyTo();

        Assert.Equal(Work.Id, updated.CategoryId);
    }

    [Fact]
    public void Wallet_editor_projects_nested_categories_and_writes_selection()
    {
        var source = new SecureItem
        {
            ItemType = VaultItemType.BankCard,
            Title = "Company card",
            CategoryId = Work.Id
        };
        var editor = new WalletItemEditorViewModel(
            new LocalizationService(),
            source,
            categories: [Work, Production]);

        editor.SelectedCategory = editor.CategoryOptions.Single(option => option.Id == Production.Id);
        var updated = editor.ApplyTo();

        Assert.Equal(Production.Id, updated.CategoryId);
        Assert.Equal("Work", editor.SelectedCategory.ParentPath);
    }

    [Fact]
    public void Editors_without_category_context_preserve_existing_assignment()
    {
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Deploy",
            ItemData = "{\"secret\":\"JBSWY3DPEHPK3PXP\"}",
            CategoryId = Production.Id
        };
        var wallet = new SecureItem
        {
            ItemType = VaultItemType.Document,
            CategoryId = Work.Id
        };

        new TotpEditorViewModel(new LocalizationService(), totp).ApplyTo();
        new WalletItemEditorViewModel(new LocalizationService(), wallet).ApplyTo();

        Assert.Equal(Production.Id, totp.CategoryId);
        Assert.Equal(Work.Id, wallet.CategoryId);
    }
}
