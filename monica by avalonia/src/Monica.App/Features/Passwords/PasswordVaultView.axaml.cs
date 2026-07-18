using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Features;
using Monica.App.ViewModels;

namespace Monica.App.Features.Passwords;

public partial class PasswordVaultView : UserControl
{
    public const double NarrowLayoutBreakpoint = 760;
    public const double WideLayoutBreakpoint = 1180;

    private MainWindowViewModel? _observedViewModel;
    private PasswordDetailPaneView? _passwordDetailPane;

    public PasswordVaultView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
        Dispatcher.UIThread.Post(VaultEditorDialogWarmup.EnsurePasswordWarmed, DispatcherPriority.SystemIdle);
    }

    public bool IsNarrowLayout { get; private set; }

    public bool IsWideLayout { get; private set; }

    public bool IsSearchBox(object? source) => PasswordVaultToolbar.IsSearchBox(source);

    public bool IsNonSearchTextEditingSource(object? source) =>
        PasswordVaultToolbar.IsNonSearchTextEditingSource(source);

    public void SelectAdjacentPassword(MainWindowViewModel viewModel, int delta)
    {
        var visiblePasswords = viewModel.VisiblePasswordNavigationEntries.ToList();
        if (visiblePasswords.Count == 0)
        {
            return;
        }

        var currentIndex = viewModel.SelectedPassword is null
            ? -1
            : visiblePasswords.FindIndex(item => item.Id == viewModel.SelectedPassword.Id);
        var nextIndex = currentIndex < 0
            ? (delta > 0 ? 0 : visiblePasswords.Count - 1)
            : Math.Clamp(currentIndex + delta, 0, visiblePasswords.Count - 1);

        viewModel.SelectedPassword = visiblePasswords[nextIndex];
        if (viewModel.SelectedPasswordListRow is { } row)
        {
            Dispatcher.UIThread.Post(() => ScrollIntoView(row));
        }
    }

    public void FocusSearch() => PasswordVaultToolbar.FocusSearch();

    public bool IsSearchFocused => PasswordVaultToolbar.IsSearchFocused;

    public void ScrollIntoView(PasswordListRow row) => PasswordListPane.ScrollIntoView(row);

    public void FocusDetails()
    {
        EnsurePasswordDetailPane();
        PasswordDetailRegion.Focus();
    }

    public bool IsDetailFocused => PasswordDetailRegion.IsFocused;

    public void FocusPasswordList() => Dispatcher.UIThread.Post(PasswordListPane.FocusList);

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowLayoutBreakpoint;
        IsWideLayout = width >= WideLayoutBreakpoint;
        ApplyResponsiveLayout();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += ViewModelOnPropertyChanged;
            if (_observedViewModel.SelectedPassword is not null)
            {
                EnsurePasswordDetailPane();
            }
        }

        ApplyResponsiveLayout();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.SelectedPassword))
        {
            return;
        }

        if (_observedViewModel?.SelectedPassword is not null)
        {
            EnsurePasswordDetailPane();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyResponsiveLayout();
        }
        else
        {
            Dispatcher.UIThread.Post(ApplyResponsiveLayout);
        }
    }

    private void ApplyResponsiveLayout()
    {
        if (PasswordMasterDetailGrid.ColumnDefinitions.Count < 3)
        {
            return;
        }

        var hasSelection = _observedViewModel?.SelectedPassword is not null;
        var detailPane = hasSelection ? EnsurePasswordDetailPane() : _passwordDetailPane;
        var columns = PasswordMasterDetailGrid.ColumnDefinitions;
        PasswordFolderFilters.ShowCompactFolderPicker = !IsWideLayout;

        if (IsNarrowLayout)
        {
            columns[0].Width = new GridLength(1, GridUnitType.Star);
            columns[1].Width = new GridLength(0);
            columns[2].Width = new GridLength(0);
            Grid.SetColumn(PasswordFolderNavigationRegion, 0);
            Grid.SetColumn(PasswordListRegion, 0);
            Grid.SetColumn(PasswordDetailRegion, 0);
            PasswordListRegion.Margin = new Thickness(0);
            PasswordDetailRegion.Margin = new Thickness(0);
            PasswordFolderNavigationRegion.IsVisible = false;
            PasswordListRegion.IsVisible = !hasSelection;
            PasswordDetailRegion.IsVisible = hasSelection;
            if (detailPane is not null)
            {
                detailPane.ShowBackButton = hasSelection;
            }
            if (!hasSelection)
            {
                FocusPasswordList();
            }

            return;
        }

        columns[0].Width = new GridLength(IsWideLayout ? 220 : 320);
        columns[1].Width = IsWideLayout
            ? new GridLength(332)
            : new GridLength(1, GridUnitType.Star);
        columns[2].Width = IsWideLayout
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        Grid.SetColumn(PasswordFolderNavigationRegion, 0);
        Grid.SetColumn(PasswordListRegion, IsWideLayout ? 1 : 0);
        Grid.SetColumn(PasswordDetailRegion, IsWideLayout ? 2 : 1);
        PasswordListRegion.Margin = IsWideLayout ? new Thickness(12, 0, 0, 0) : new Thickness(0);
        PasswordDetailRegion.Margin = new Thickness(12, 0, 0, 0);
        PasswordFolderNavigationRegion.IsVisible = IsWideLayout;
        PasswordListRegion.IsVisible = true;
        PasswordDetailRegion.IsVisible = true;
        if (detailPane is not null)
        {
            detailPane.ShowBackButton = false;
        }
    }

    private PasswordDetailPaneView EnsurePasswordDetailPane()
    {
        if (_passwordDetailPane is not null)
        {
            return _passwordDetailPane;
        }

        _passwordDetailPane = new PasswordDetailPaneView { Name = "PasswordDetailPane" };
        PasswordDetailPaneHost.Content = _passwordDetailPane;
        return _passwordDetailPane;
    }
}
