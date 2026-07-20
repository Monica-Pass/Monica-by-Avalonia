using Avalonia.Controls;
using Monica.App.Features;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class RemainingDialogsWorkflowUiTests
{
    public RemainingDialogsWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Totp_and_category_editors_expose_progressive_labeled_forms()
    {
        var totp = new Monica.App.TotpEditorDialog();
        var category = new Monica.App.CategoryPickerDialog();

        Assert.NotNull(totp.FindControl<ScrollViewer>("TotpEditorFormScrollViewer"));
        Assert.NotNull(totp.FindControl<StackPanel>("TotpEditorPrimaryForm"));
        Assert.NotNull(totp.FindControl<TextBox>("TotpSecretInput"));
        Assert.False(totp.FindControl<Expander>("TotpAdvancedOptionsExpander")!.IsExpanded);
        Assert.NotNull(category.FindControl<Grid>("CategoryPickerForm"));
        Assert.Equal(40, category.FindControl<TextBox>("CategoryPickerSearchBox")!.MinHeight);
        Assert.NotNull(category.FindControl<ListBox>("CategoryPickerList"));
    }

    [Fact]
    public void Category_picker_projects_and_searches_nested_folder_paths()
    {
        var viewModel = new CategoryPickerViewModel(
            new LocalizationService(),
            [
                new Category { Id = 1, Name = "Work/Production", SortOrder = 2 },
                new Category { Id = 2, Name = "Work/Development/Cloud", SortOrder = 1 }
            ],
            selectedCategoryId: 2);

        Assert.Equal(
            ["Work/Development/Cloud", "Work/Production"],
            viewModel.CategoryOptions.Skip(1).Select(option => option.FullPath));
        Assert.Equal("Cloud", viewModel.SelectedCategory?.FolderDisplayName);
        Assert.Equal(2, viewModel.SelectedCategory?.Level);

        viewModel.SearchText = "production";

        Assert.Single(viewModel.FilteredCategoryOptions);
        Assert.Equal("Work/Production", viewModel.FilteredCategoryOptions[0].FullPath);
    }

    [Fact]
    public void Shell_loading_surface_uses_localization_and_theme_resources()
    {
        var xaml = ReadSource("Features", "UnlockedShellView.axaml");
        Assert.Contains("x:Name=\"VaultNavigationView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("L[VaultLoadTitle]", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("正在加载保险库", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("#DDFFFFFF", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#C8FFFFFF", xaml, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, "src", "Monica.App", .. parts]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }
}
