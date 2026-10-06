using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Markdown.Avalonia;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NotePreviewView : UserControl
{
    private MainWindowViewModel? _subscribedViewModel;
    private bool _isDetached;

    public NotePreviewView()
    {
        // Editing a note should not load the Markdown renderer or its dependencies. The preview
        // graph is built when the user first chooses preview or split mode.
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isDetached = false;
        AttachViewModel(DataContext as MainWindowViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _isDetached = true;
        AttachViewModel(null);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) =>
        AttachViewModel(_isDetached ? null : DataContext as MainWindowViewModel);

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (!ReferenceEquals(_subscribedViewModel, viewModel))
        {
            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            ClearPreview();
            _subscribedViewModel = viewModel;
            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        UpdatePreview();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsUnlocked) &&
            _subscribedViewModel is { IsUnlocked: false })
        {
            AttachViewModel(null);
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.IsUnlocked) or
            nameof(MainWindowViewModel.IsNotePreviewPaneVisible))
        {
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        if (_subscribedViewModel is not { IsUnlocked: true })
        {
            ClearPreview();
            return;
        }

        if (_subscribedViewModel.IsNotePreviewPaneVisible && Content is null)
        {
            InitializeComponent();
        }
    }

    private void ClearPreview()
    {
        if (Content is Control content)
        {
            ClearRenderedPreview(content);
        }

        Content = null;
    }

    // Keep the renderer type out of the editing path's JIT dependency set. This method is reached
    // only after the preview has actually been built.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearRenderedPreview(Control content)
    {
        // Snapshot first: resetting a Markdown document or an item source removes descendants.
        // Invalid bindings can keep their last successful value when DataContext disappears, so
        // explicitly erase rendered values as well as detaching the old vault's data context.
        var controls = content.GetVisualDescendants().OfType<Control>()
            .Concat(content.GetLogicalDescendants().OfType<Control>())
            .Prepend(content)
            .Distinct()
            .ToArray();
        foreach (var control in controls)
        {
            control.DataContext = null;
            switch (control)
            {
                case MarkdownScrollViewer markdown:
                    markdown.Markdown = string.Empty;
                    break;
                case TextBlock text:
                    text.Text = string.Empty;
                    break;
                case Image image:
                    image.Source = null;
                    break;
                case ItemsControl items:
                    items.ItemsSource = null;
                    break;
            }
        }
    }
}
