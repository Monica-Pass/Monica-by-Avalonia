namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NoteImageWorkflowUiTests
{
    [Fact]
    public void Note_image_toolbar_exposes_accessible_busy_state()
    {
        var toolbarXaml = File.ReadAllText(FindSourceFile("NoteEditorToolbarView.axaml"));

        Assert.Contains("x:Name=\"InsertNoteImageButton\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InsertNoteImageProgress\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanInsertNoteImage}\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsInsertingNoteImage}\"", toolbarXaml, StringComparison.Ordinal);
        Assert.Contains(
            "AutomationProperties.Name=\"{Binding InsertNoteImageActionLabel}\"",
            toolbarXaml,
            StringComparison.Ordinal);

        // The tab strip kept a second copy of that label and went away with the notes page, so the
        // toolbar is the one place left that reports the busy state. The library tree is the navigator.
        Assert.Equal(
            ["NoteEditorToolbarView.axaml"],
            [.. XamlSource.ContainsAnywhere("InsertNoteImageActionLabel")
                .Where(name => name.EndsWith(".axaml", StringComparison.Ordinal))]);
    }

    private static string FindSourceFile(string fileName) =>
        XamlSource.PathOf(fileName);
}
