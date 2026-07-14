using Avalonia.Controls;
using Monica.App.Features.Notes;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NoteWorkflowUiTests
{
    public NoteWorkflowUiTests()
    {
        TestAppBuilder.EnsureInitialized();
    }

    [Fact]
    public void Note_workspace_exposes_responsive_master_editor_regions()
    {
        var view = new NoteWorkspaceView();

        Assert.NotNull(view.FindControl<Grid>("NoteWorkspaceGrid"));
        Assert.NotNull(view.FindControl<NoteTreeView>("NoteTreeRegion"));
        Assert.NotNull(view.FindControl<NoteEditorView>("NoteEditorRegion"));
    }

    [Fact]
    public void Note_views_expose_empty_search_and_return_actions()
    {
        var tree = new NoteTreeView();
        var editor = new NoteEditorView();

        Assert.NotNull(tree.FindControl<TextBox>("NoteSearchBox"));
        Assert.NotNull(tree.FindControl<Button>("ClearNoteSearchButton"));
        Assert.NotNull(tree.FindControl<StackPanel>("NoteTreeEmptyState"));
        Assert.NotNull(tree.FindControl<Button>("EmptyNoteTreeAddButton"));
        Assert.NotNull(editor.FindControl<Grid>("NoteEditorContent"));
        Assert.NotNull(editor.FindControl<StackPanel>("NoteEditorEmptyState"));
        Assert.NotNull(editor.FindControl<Button>("BackToNoteListButton"));
    }
}
