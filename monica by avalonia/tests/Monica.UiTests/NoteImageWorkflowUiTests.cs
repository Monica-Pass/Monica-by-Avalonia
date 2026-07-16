namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NoteImageWorkflowUiTests
{
    [Fact]
    public void Note_image_toolbar_exposes_accessible_busy_state()
    {
        var toolbarXaml = File.ReadAllText(FindSourceFile("NoteEditorToolbarView.axaml"));
        var tabStripXaml = File.ReadAllText(FindSourceFile("NoteTabStripView.axaml"));

        Assert.Contains("x:Name=\"InsertNoteImageButton\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InsertNoteImageProgress\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanInsertNoteImage}\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsInsertingNoteImage}\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding InsertNoteImageActionLabel}\"",
            toolbarXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Header=\"{Binding InsertNoteImageActionLabel}\"",
            tabStripXaml,
            StringComparison.Ordinal);
    }

    private static string FindSourceFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Monica.App", "Features", "Notes", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
