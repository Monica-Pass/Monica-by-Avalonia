using System.ComponentModel;
using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Sync;

public partial class SyncPageHostView : UserControl
{
    private MainWindowViewModel? _viewModel;
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.OrdinalIgnoreCase);

    public SyncPageHostView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
            ShowPage(_viewModel.SelectedSyncPage);
        }
        else
        {
            SyncPageContent.Content = null;
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedSyncPage) && _viewModel is not null)
        {
            ShowPage(_viewModel.SelectedSyncPage);
        }
    }

    private void ShowPage(string? page)
    {
        var key = string.IsNullOrWhiteSpace(page) ? "Configuration" : page;
        if (!_pages.TryGetValue(key, out var view))
        {
            view = CreatePage(key);
            _pages[key] = view;
        }

        view.DataContext = _viewModel;
        SyncPageContent.Content = view;
    }

    private static UserControl CreatePage(string page) => page.ToLowerInvariant() switch
    {
        "backup" => new SyncBackupView(),
        "sources" => new SyncSourcesView(),
        "import" => new SyncImportView(),
        "export" => new SyncExportView(),
        _ => new SyncConfigurationView()
    };
}
