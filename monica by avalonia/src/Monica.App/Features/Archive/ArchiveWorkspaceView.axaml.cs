using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Archive;

public partial class ArchiveWorkspaceView : UserControl
{
    private MainWindowViewModel? _observedViewModel;

    public ArchiveWorkspaceView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
    }

    public bool IsNarrowLayout { get; private set; }
    public bool IsMediumLayout { get; private set; }

    public void FocusSearch()
    {
        ArchiveSearchBox.Focus();
        ArchiveSearchBox.SelectAll();
    }

    public void FocusList() => Dispatcher.UIThread.Post(() => ArchivePasswordList.Focus());
    public void FocusDetails() => ArchiveDetailRegion.Focus();

    public void SelectAdjacent(MainWindowViewModel viewModel, int delta)
    {
        var items = viewModel.FilteredArchivedPasswords.ToList();
        SelectAdjacentItem(items, viewModel.SelectedArchivedPassword, delta, item => viewModel.SelectedArchivedPassword = item);
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
        if (e.PropertyName is nameof(MainWindowViewModel.ArchiveNarrowShowsList) or nameof(MainWindowViewModel.SelectedArchivedPassword))
        {
            Dispatcher.UIThread.Post(() => ApplyResponsiveLayout(Bounds.Width));
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        var showDetails = IsNarrowLayout && _observedViewModel?.ArchiveNarrowShowsList == false && _observedViewModel.SelectedArchivedPassword is not null;
        LifecycleWorkspaceLayout.ApplyContent(ArchiveMasterDetailGrid, ArchiveListRegion, ArchiveDetailRegion, BackToArchiveListButton, IsNarrowLayout, showDetails, IsMediumLayout ? 280 : 300);
        LifecycleWorkspaceLayout.ApplyHeader(ArchiveSearchRegion, ArchiveHeaderCommands, width > 0 && width < LifecycleWorkspaceLayout.WideHeaderBreakpoint);
    }

    private void SelectAdjacentItem(IReadOnlyList<Monica.Core.Models.PasswordEntry> items, Monica.Core.Models.PasswordEntry? selected, int delta, Action<Monica.Core.Models.PasswordEntry> select)
    {
        if (items.Count == 0) return;
        var index = selected is null ? -1 : items.ToList().FindIndex(item => item.Id == selected.Id);
        var next = index < 0 ? (delta > 0 ? 0 : items.Count - 1) : Math.Clamp(index + delta, 0, items.Count - 1);
        select(items[next]);
        ArchivePasswordList.ScrollIntoView(items[next]);
    }
}
