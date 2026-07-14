using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.RecycleBin;

public partial class RecycleBinWorkspaceView : UserControl
{
    private MainWindowViewModel? _observedViewModel;

    public RecycleBinWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
    }

    public bool IsNarrowLayout { get; private set; }
    public bool IsMediumLayout { get; private set; }

    public void FocusSearch()
    {
        RecycleBinSearchBox.Focus();
        RecycleBinSearchBox.SelectAll();
    }

    public void FocusList() => Dispatcher.UIThread.Post(() => RecycleBinPasswordList.Focus());
    public void FocusDetails() => RecycleBinDetailRegion.Focus();

    public void SelectAdjacent(MainWindowViewModel viewModel, int delta)
    {
        var items = viewModel.FilteredDeletedPasswords.ToList();
        if (items.Count == 0) return;
        var index = viewModel.SelectedDeletedPassword is null
            ? -1
            : items.FindIndex(item => item.Id == viewModel.SelectedDeletedPassword.Id);
        var next = index < 0 ? (delta > 0 ? 0 : items.Count - 1) : Math.Clamp(index + delta, 0, items.Count - 1);
        viewModel.SelectedDeletedPassword = items[next];
        RecycleBinPasswordList.ScrollIntoView(items[next]);
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
        if (e.PropertyName is nameof(MainWindowViewModel.RecycleBinNarrowShowsList) or nameof(MainWindowViewModel.SelectedDeletedPassword))
        {
            Dispatcher.UIThread.Post(() => ApplyResponsiveLayout(Bounds.Width));
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        var showDetails = IsNarrowLayout && _observedViewModel?.RecycleBinNarrowShowsList == false && _observedViewModel.SelectedDeletedPassword is not null;
        LifecycleWorkspaceLayout.ApplyContent(RecycleBinMasterDetailGrid, RecycleBinListRegion, RecycleBinDetailRegion, BackToRecycleBinListButton, IsNarrowLayout, showDetails, IsMediumLayout ? 280 : 300);
        LifecycleWorkspaceLayout.ApplyHeader(RecycleBinSearchRegion, RecycleBinHeaderCommands, width > 0 && width < LifecycleWorkspaceLayout.WideHeaderBreakpoint);
    }
}
