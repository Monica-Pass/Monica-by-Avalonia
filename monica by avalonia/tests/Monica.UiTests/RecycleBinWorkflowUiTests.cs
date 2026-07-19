using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.RecycleBin;
using Monica.Core.Models;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class RecycleBinWorkflowUiTests
{
    public RecycleBinWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Recycle_bin_projects_password_totp_note_card_and_document_without_secret_fields()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<Monica.App.ViewModels.MainWindowViewModel>();
        var deletedAt = DateTimeOffset.UtcNow;

        viewModel.DeletedPasswords.Add(new PasswordEntry
        {
            Id = 1,
            Title = "Account",
            Password = "must-not-appear",
            IsDeleted = true,
            DeletedAt = deletedAt
        });
        viewModel.DeletedSecureItems.Add(CreateSecureItem(2, VaultItemType.Totp, "Authenticator", deletedAt));
        viewModel.DeletedSecureItems.Add(CreateSecureItem(3, VaultItemType.Note, "Note", deletedAt));
        viewModel.DeletedSecureItems.Add(CreateSecureItem(4, VaultItemType.BankCard, "Card", deletedAt));
        viewModel.DeletedSecureItems.Add(CreateSecureItem(5, VaultItemType.Document, "Document", deletedAt));

        viewModel.RecycleBinSearchText = "missing";
        viewModel.ClearRecycleBinSearchCommand.Execute(null);

        Assert.Equal(5, viewModel.RecycleBinItems.Count);
        Assert.Contains(viewModel.RecycleBinItems, item => item.Password is not null);
        Assert.Contains(viewModel.RecycleBinItems, item => item.SecureItem?.ItemType == VaultItemType.Totp);
        Assert.Contains(viewModel.RecycleBinItems, item => item.SecureItem?.ItemType == VaultItemType.Note);
        Assert.Contains(viewModel.RecycleBinItems, item => item.SecureItem?.ItemType == VaultItemType.BankCard);
        Assert.Contains(viewModel.RecycleBinItems, item => item.SecureItem?.ItemType == VaultItemType.Document);
        Assert.DoesNotContain("must-not-appear", string.Join('|', viewModel.RecycleBinItems.Select(item => $"{item.Title}|{item.ItemType}|{item.Source}")), StringComparison.Ordinal);
    }

    [Fact]
    public void Recycle_bin_xaml_uses_unified_virtualized_list_and_safe_metadata()
    {
        var xaml = File.ReadAllText(FindFeatureFile("RecycleBinWorkspaceView.axaml"));

        Assert.Contains("ItemsSource=\"{Binding FilteredRecycleBinItems}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"recycle:RecycleBinDisplayItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedRecycleBinItem.ItemType", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedRecycleBinItem.Source", xaml, StringComparison.Ordinal);
        Assert.Contains("RecycleBinRetentionMenuItem", xaml, StringComparison.Ordinal);
        Assert.Contains("RecycleBinRetentionDays", xaml + File.ReadAllText(FindFeatureFile("MainWindowViewModel.RecycleBinProperties.cs")), StringComparison.Ordinal);
        Assert.Contains("L[RestoreItem]", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectRecycleBinItemsText", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectAllVisibleRecycleBinItemsText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedDeletedPassword.Username", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedDeletedPassword.Website", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("L.RestorePassword", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectPasswordItemsText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectAllVisiblePasswordsText", xaml, StringComparison.Ordinal);
    }

    private static SecureItem CreateSecureItem(long id, VaultItemType type, string title, DateTimeOffset deletedAt) => new()
    {
        Id = id,
        ItemType = type,
        Title = title,
        ItemData = "{\"secret\":\"must-not-appear\"}",
        IsDeleted = true,
        DeletedAt = deletedAt
    };

    private static string FindFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "Features", "RecycleBin", fileName);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
