using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.ViewModels;

namespace Monica.App.Features.Passwords;

public partial class PasswordVaultView : UserControl
{
    public const double NarrowLayoutBreakpoint = 760;

    private MainWindowViewModel? _observedViewModel;

    public PasswordVaultView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateResponsiveLayoutForWidth(e.NewSize.Width);
        DataContextChanged += OnDataContextChanged;
    }

    public bool IsNarrowLayout { get; private set; }

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

    public void FocusDetails() => PasswordDetailRegion.Focus();

    public bool IsDetailFocused => PasswordDetailRegion.IsFocused;

    public void FocusPasswordList() => Dispatcher.UIThread.Post(PasswordListPane.FocusList);

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        IsNarrowLayout = width > 0 && width < NarrowLayoutBreakpoint;
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
        }

        ApplyResponsiveLayout();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.SelectedPassword))
        {
            return;
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
        if (PasswordMasterDetailGrid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        var hasSelection = _observedViewModel?.SelectedPassword is not null;
        var columns = PasswordMasterDetailGrid.ColumnDefinitions;
        columns[0].Width = IsNarrowLayout
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(300);
        columns[1].Width = IsNarrowLayout
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);

        if (IsNarrowLayout)
        {
            Grid.SetColumn(PasswordListRegion, 0);
            Grid.SetColumn(PasswordDetailRegion, 0);
            PasswordListRegion.IsVisible = !hasSelection;
            PasswordDetailRegion.IsVisible = hasSelection;
            PasswordDetailRegion.Margin = new Thickness(0);
            PasswordDetailPane.ShowBackButton = hasSelection;
            if (!hasSelection)
            {
                FocusPasswordList();
            }

            return;
        }

        Grid.SetColumn(PasswordListRegion, 0);
        Grid.SetColumn(PasswordDetailRegion, 1);
        PasswordListRegion.IsVisible = true;
        PasswordDetailRegion.IsVisible = true;
        PasswordDetailRegion.Margin = new Thickness(12, 0, 0, 0);
        PasswordDetailPane.ShowBackButton = false;
    }
}
