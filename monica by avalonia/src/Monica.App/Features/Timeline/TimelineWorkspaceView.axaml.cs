using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Timeline;

public partial class TimelineWorkspaceView : UserControl
{
    private MainWindowViewModel? _observedViewModel;

    public TimelineWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
    }

    public bool IsNarrowLayout { get; private set; }
    public bool IsMediumLayout { get; private set; }

    public void FocusSearch()
    {
        TimelineSearchBox.Focus();
        TimelineSearchBox.SelectAll();
    }

    public void FocusList() => Dispatcher.UIThread.Post(() => TimelineEntryListView.EntryList.Focus());
    public void FocusDetails() => TimelineDetailRegion.Focus();

    public void SelectAdjacent(MainWindowViewModel viewModel, int delta)
    {
        var items = viewModel.FilteredTimelineEntries.ToList();
        if (items.Count == 0) return;
        var index = viewModel.SelectedTimelineEntry is null ? -1 : items.IndexOf(viewModel.SelectedTimelineEntry);
        var next = index < 0 ? (delta > 0 ? 0 : items.Count - 1) : Math.Clamp(index + delta, 0, items.Count - 1);
        viewModel.SelectedTimelineEntry = items[next];
        TimelineEntryListView.EntryList.ScrollIntoView(items[next]);
    }

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < LifecycleWorkspaceLayout.NarrowBreakpoint;
        IsMediumLayout = width >= LifecycleWorkspaceLayout.NarrowBreakpoint && width < LifecycleWorkspaceLayout.WideBreakpoint;
        ApplyResponsiveLayout(width);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null) _observedViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is not null) _observedViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        ApplyResponsiveLayout(Bounds.Width);
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.TimelineNarrowShowsList) or nameof(MainWindowViewModel.SelectedTimelineEntry))
        {
            Dispatcher.UIThread.Post(() => ApplyResponsiveLayout(Bounds.Width));
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        var showDetails = IsNarrowLayout && _observedViewModel?.TimelineNarrowShowsList == false && _observedViewModel.SelectedTimelineEntry is not null;
        LifecycleWorkspaceLayout.ApplyContent(TimelineMasterDetailGrid, TimelineListRegion, TimelineDetailRegion, TimelineInspectorView.BackButton, IsNarrowLayout, showDetails, IsMediumLayout ? 340 : 420);
        LifecycleWorkspaceLayout.ApplyHeader(TimelineSearchRegion, TimelineCommandBar, width > 0 && width < LifecycleWorkspaceLayout.WideHeaderBreakpoint);
    }
}
