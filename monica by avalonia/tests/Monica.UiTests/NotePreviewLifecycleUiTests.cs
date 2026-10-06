using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Markdown.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Notes;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class NotePreviewLifecycleUiTests
{
    public NotePreviewLifecycleUiTests() => AvaloniaUiThreadTestContext.VerifyAccess();

    [Fact]
    public void Editing_defers_renderer_until_preview_or_split_is_requested()
    {
        using var preview = PreviewHarness.Open("# Deferred preview", showPreview: false);
        var viewModel = preview.ViewModel;
        var view = preview.View;

        Assert.Null(view.Content);

        viewModel.NoteSplitPreviewMode = true;

        var renderer = Renderer(view);
        Assert.Equal(viewModel.NotePreviewMarkdown, renderer.Markdown);
        viewModel.NoteContent = "# Updated preview";
        Assert.Equal(viewModel.NotePreviewMarkdown, renderer.Markdown);

        viewModel.NoteSplitPreviewMode = false;
        viewModel.NotePreviewMode = true;
        Assert.Same(renderer, Renderer(view));
        viewModel.NoteIsMarkdown = false;
        preview.Settle();
        Assert.False(renderer.IsVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == viewModel.NotePlainPreview);
        viewModel.NoteIsMarkdown = true;
        Assert.True(renderer.IsVisible);
        Assert.Equal(viewModel.NotePreviewMarkdown, renderer.Markdown);
        view.DataContext = null;
    }

    [Fact]
    public void Changing_data_context_releases_previous_note_and_observer()
    {
        using var preview = PreviewHarness.Open("# First vault note");
        var anchor = new Monica.App.MainWindow();
        using var secondServices = Monica.App.App.ConfigureServices(anchor);
        var first = preview.ViewModel;
        var second = secondServices.GetRequiredService<MainWindowViewModel>();
        second.IsUnlocked = true;
        second.AddNoteCommand.Execute(null);
        second.NoteContent = "# Second vault note";
        second.SetNoteViewModeCommand.Execute("preview");
        var view = preview.View;
        var previousRenderer = Renderer(view);

        var previousPlainText = PlainText(view);

        view.DataContext = second;

        var currentRenderer = Renderer(view);
        Assert.NotSame(previousRenderer, currentRenderer);
        Assert.True(string.IsNullOrEmpty(previousRenderer.Markdown));
        Assert.True(string.IsNullOrEmpty(previousPlainText.Text));
        Assert.Equal(second.NotePreviewMarkdown, currentRenderer.Markdown);
        first.IsUnlocked = false;
        Assert.Same(currentRenderer, Renderer(view));
        view.DataContext = null;
        Assert.Null(view.Content);
        Assert.True(string.IsNullOrEmpty(currentRenderer.Markdown));
        second.NotePreviewMode = false;
        second.NoteSplitPreviewMode = true;
        Assert.Null(view.Content);
    }

    [Fact]
    public void Detaching_releases_rendered_text_and_reattaching_renders_current_note()
    {
        using var preview = PreviewHarness.Open("# Before detach");
        var viewModel = preview.ViewModel;
        var view = preview.View;
        var host = preview.Window;
        var previousRenderer = Renderer(view);

        host.Content = null;

        Assert.Null(view.Content);
        Assert.True(string.IsNullOrEmpty(previousRenderer.Markdown));
        viewModel.NoteContent = "# Changed while detached";
        viewModel.NotePreviewMode = false;
        viewModel.NoteSplitPreviewMode = true;
        Assert.Null(view.Content);

        host.Content = view;
        preview.Settle();

        var currentRenderer = Renderer(view);
        Assert.NotSame(previousRenderer, currentRenderer);
        Assert.Equal(viewModel.NotePreviewMarkdown, currentRenderer.Markdown);
    }

    [Fact]
    public void Locking_releases_preview_and_cannot_rebuild_it_from_locked_mode_changes()
    {
        using var preview = PreviewHarness.Open("# Private note");
        var viewModel = preview.ViewModel;
        var view = preview.View;
        var previousRenderer = Renderer(view);

        viewModel.IsUnlocked = false;

        Assert.Null(view.Content);
        Assert.True(string.IsNullOrEmpty(previousRenderer.Markdown));
        viewModel.NoteContent = "# Should stay hidden";
        viewModel.NotePreviewMode = false;
        viewModel.NoteSplitPreviewMode = true;
        Assert.Null(view.Content);
        view.DataContext = null;
    }

    private static MarkdownScrollViewer Renderer(NotePreviewView view)
    {
        Assert.True(view.DataContext is MainWindowViewModel { IsUnlocked: true, IsNotePreviewPaneVisible: true });
        Assert.NotNull(view.Content);
        // ContentControl applies its template during layout; inspect the same attached graph that
        // displays the note to the user instead of an untemplated standalone control.
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(view.GetVisualDescendants().OfType<MarkdownScrollViewer>());
    }

    private static TextBlock PlainText(NotePreviewView view) =>
        Assert.Single(view.GetLogicalDescendants().OfType<ScrollViewer>()
            .Select(scroll => scroll.Content).OfType<TextBlock>());

    private sealed class PreviewHarness(Window window, ServiceProvider services,
        MainWindowViewModel viewModel, NotePreviewView view) : IDisposable
    {
        public Window Window { get; } = window;
        public MainWindowViewModel ViewModel { get; } = viewModel;
        public NotePreviewView View { get; } = view;

        public static PreviewHarness Open(string content, bool showPreview = true)
        {
            var anchor = new Monica.App.MainWindow();
            var services = Monica.App.App.ConfigureServices(anchor);
            var viewModel = services.GetRequiredService<MainWindowViewModel>();
            viewModel.IsUnlocked = true;
            viewModel.AddNoteCommand.Execute(null);
            viewModel.NoteContent = content;
            viewModel.SetNoteViewModeCommand.Execute(showPreview ? "preview" : "edit");
            var view = new NotePreviewView { DataContext = viewModel };
            var window = new Window { Width = 900, Height = 600, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return new PreviewHarness(window, services, viewModel, view);
        }

        public void Settle() => Dispatcher.UIThread.RunJobs();

        public void Dispose()
        {
            Window.Close();
            View.DataContext = null;
            services.Dispose();
        }
    }
}
