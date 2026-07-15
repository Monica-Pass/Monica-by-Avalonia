namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private NoteContentAnalysis _noteContentAnalysis = NoteContentAnalysis.Empty;
    private NotePreviewProjection _notePreviewProjection = NotePreviewProjection.Empty;
    private bool _noteContentAnalysisDirty = true;
    private bool _notePreviewProjectionDirty = true;

    private NoteContentAnalysis GetNoteContentAnalysis()
    {
        if (!_noteContentAnalysisDirty)
        {
            return _noteContentAnalysis;
        }

        NoteContentAnalysisBuildCount++;
        var content = NoteContent;
        var lineCount = CountNoteLines(content);
        _noteContentAnalysis = new NoteContentAnalysis(
            BuildLineNumbersText(lineCount),
            lineCount,
            CountNoteWords(content),
            content.Length,
            BuildNoteOutlineItems(content),
            BuildNoteReferenceItems(content));
        _noteContentAnalysisDirty = false;
        return _noteContentAnalysis;
    }

    private NotePreviewProjection GetNotePreviewProjection()
    {
        if (!_notePreviewProjectionDirty)
        {
            return _notePreviewProjection;
        }

        NotePreviewProjectionBuildCount++;
        _notePreviewProjection = new NotePreviewProjection(
            NoteIsMarkdown ? BuildNotePreviewMarkdown(NoteContent) : "",
            BuildNotePlainPreview(NoteContent, NoteIsMarkdown));
        _notePreviewProjectionDirty = false;
        return _notePreviewProjection;
    }

    private void InvalidateNoteEditorProjections()
    {
        _noteContentAnalysis = NoteContentAnalysis.Empty;
        _noteContentAnalysisDirty = true;
        InvalidateNotePreviewProjection();
    }

    private void InvalidateNotePreviewProjection()
    {
        _notePreviewProjection = NotePreviewProjection.Empty;
        _notePreviewProjectionDirty = true;
    }

    private void ClearSensitiveNoteEditorProjectionCaches() =>
        InvalidateNoteEditorProjections();

    private sealed record NoteContentAnalysis(
        string LineNumbersText,
        int LineCount,
        int WordCount,
        int CharacterCount,
        IReadOnlyList<NoteOutlineItem> OutlineItems,
        IReadOnlyList<NoteReferenceItem> ReferenceItems)
    {
        public static NoteContentAnalysis Empty { get; } = new("1", 1, 0, 0, [], []);
    }

    private sealed record NotePreviewProjection(string Markdown, string PlainText)
    {
        public static NotePreviewProjection Empty { get; } = new("", "");
    }
}
