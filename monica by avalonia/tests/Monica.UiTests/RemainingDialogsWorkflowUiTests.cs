using Avalonia.Controls;
using Monica.App.Features;

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
        Assert.NotNull(category.FindControl<StackPanel>("CategoryPickerForm"));
        Assert.Equal(40, category.FindControl<ComboBox>("CategoryPickerComboBox")!.MinHeight);
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
