namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasswordDetailWorkflowUiTests
{
    [Fact]
    public void Password_detail_actions_bind_accessible_names_to_their_desktop_commands()
    {
        var xaml = File.ReadAllText(FindPasswordDetailXaml());

        Assert.Contains("x:Name=\"PasswordDetailFieldRevealButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PasswordHistoryRevealButton\"", xaml, StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(xaml, "AutomationProperties.Name=\"{Binding VisibilityActionLabel}\""));
        Assert.Equal(
            2,
            CountOccurrences(xaml, "AutomationProperties.Name=\"{Binding #Root.DataContext.CopyLabel}\""));
        Assert.Equal(
            2,
            CountOccurrences(xaml, "AutomationProperties.Name=\"{Binding #Root.DataContext.DeleteLabel}\""));
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding ClearPasswordHistoryLabel}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding L.AddAttachment}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PasswordAttachmentSaveButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding #Root.DataContext.SaveAttachmentLabel}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"PasswordAttachmentCopyButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyAttachmentPathCommand", xaml, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string FindPasswordDetailXaml()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "PasswordDetailDialog.axaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate PasswordDetailDialog.axaml from the test output directory.");
    }
}
