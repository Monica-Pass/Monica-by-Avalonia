using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Monica.App.ViewModels;

namespace Monica.App.Features.Sync;

public partial class KeePassBrowsePane : UserControl
{
    // The two columns only behave while they are handed a height to fit their content into. Below this
    // much they would be unusable anyway, so the page is allowed to scroll past its own heading instead
    // of hiding the tree and the entry form behind the bottom edge of the window. Measured: a 1280x800
    // window leaves 196 for this pane, which shows the tree and the first fields of the form, so the
    // floor sits under that rather than above it.
    private const double WorkspaceMinHeight = 160;
    private const double PageBottomMargin = 20;
    private const double ScrollPolicyHysteresis = 8;

    private MainWindowViewModel? _observedViewModel;
    private ScrollViewer? _pageScroller;
    private bool _applyingWorkspaceHeight;

    public KeePassBrowsePane()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveViewModel(DataContext as MainWindowViewModel);
        ObserveViewModel(DataContext as MainWindowViewModel);
        Loaded += (_, _) => AttachToPageScroller();
        Unloaded += (_, _) => ReleasePageScroller();
    }

    private void ObserveViewModel(MainWindowViewModel? viewModel)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observedViewModel = viewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // The form sits at the top of this column, so whoever just pressed "new entry" should not have to
    // find the title field wherever the list happened to be left scrolled.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HasKeePassEditor)
            && _observedViewModel?.HasKeePassEditor == true)
        {
            KeePassDetailScroller.ScrollToHome();
        }

        if (e.PropertyName is nameof(MainWindowViewModel.KeePassImportTabSelected)
            or nameof(MainWindowViewModel.HasKeePassImportPreview))
        {
            QueueWorkspaceHeight();
        }
    }

    private void AttachToPageScroller()
    {
        var scroller = FindPageScroller();
        if (ReferenceEquals(scroller, _pageScroller))
        {
            QueueWorkspaceHeight();
            return;
        }

        if (_pageScroller is not null)
        {
            _pageScroller.LayoutUpdated -= OnPageLayoutUpdated;
        }

        _pageScroller = scroller;
        if (_pageScroller is not null)
        {
            _pageScroller.LayoutUpdated += OnPageLayoutUpdated;
        }

        QueueWorkspaceHeight();
    }

    private void ReleasePageScroller()
    {
        if (_pageScroller is not null)
        {
            _pageScroller.LayoutUpdated -= OnPageLayoutUpdated;
        }

        _pageScroller = null;
    }

    private ScrollViewer? FindPageScroller()
    {
        Visual? ancestor = this;
        while (ancestor is not null)
        {
            if (ancestor is ScrollViewer scroller)
            {
                return scroller;
            }

            ancestor = ancestor.GetVisualParent();
        }

        return null;
    }

    private void OnPageLayoutUpdated(object? sender, EventArgs e)
    {
        QueueWorkspaceHeight();
    }

    private void QueueWorkspaceHeight()
    {
        if (_applyingWorkspaceHeight)
        {
            return;
        }

        _applyingWorkspaceHeight = true;
        try
        {
            ApplyWorkspaceHeight();
        }
        finally
        {
            _applyingWorkspaceHeight = false;
        }
    }

    /// <summary>
    /// A browsed .kdbx is a workspace, not a document: its two columns scroll inside themselves, which
    /// needs a bounded height. The room left under the page heading is measured here and handed over as
    /// this pane's height, and the page stops scrolling while that fits. When the window is too short for
    /// it, the page keeps scrolling instead - a heading that has scrolled off the top is recoverable,
    /// content that cannot be reached is not.
    /// </summary>
    private void ApplyWorkspaceHeight()
    {
        var scroller = _pageScroller;
        if (scroller is null)
        {
            return;
        }

        var ownsThePage = IsVisible
            && _observedViewModel is { KeePassImportTabSelected: true, HasKeePassImportPreview: true };
        if (!ownsThePage)
        {
            if (!double.IsNaN(Height))
            {
                Height = double.NaN;
            }

            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            return;
        }

        // Document coordinates rather than viewport ones: the page scrolls this pane around, and it also
        // scrolls on its own when a field in the form takes focus. Measuring against the viewport would
        // make the room this pane gets depend on where the person happens to be scrolled.
        if (scroller.Content is not Visual document
            || scroller.Viewport.Height <= 0
            || this.TranslatePoint(new Point(0, 0), document) is not { } paneTop)
        {
            return;
        }

        var room = scroller.Viewport.Height - paneTop.Y - PageBottomMargin;
        var workspaceHeight = Math.Max(WorkspaceMinHeight, room);
        // An unset height reads back as NaN, and Math.Abs(NaN - x) > 0.5 is false: comparing alone would
        // refuse the first assignment forever.
        if (double.IsNaN(Height) || Math.Abs(Height - workspaceHeight) > 0.5)
        {
            Height = workspaceHeight;
        }

        // Re-enabling the page scrollbar narrows the content a little, which can make the heading taller
        // and the room smaller again; the band keeps that from flipping every pass.
        var keepPageStill = scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled
            ? room >= WorkspaceMinHeight - ScrollPolicyHysteresis
            : room >= WorkspaceMinHeight + ScrollPolicyHysteresis;
        var policy = keepPageStill ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        if (scroller.VerticalScrollBarVisibility != policy)
        {
            scroller.VerticalScrollBarVisibility = policy;
        }
    }
}
