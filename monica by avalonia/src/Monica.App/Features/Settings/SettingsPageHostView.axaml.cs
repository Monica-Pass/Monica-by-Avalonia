using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Settings;

public partial class SettingsPageHostView : UserControl
{
    private readonly Dictionary<string, UserControl> _pages = new(StringComparer.OrdinalIgnoreCase);
    private MainWindowViewModel? _viewModel;

    public SettingsPageHostView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        foreach (var page in _pages.Values)
        {
            page.DataContext = null;
        }

        _pages.Clear();
        _viewModel = null;
        SettingsPageContent.Content = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) =>
        AttachViewModel(DataContext as MainWindowViewModel);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        AttachViewModel(DataContext as MainWindowViewModel);

    private void AttachViewModel(MainWindowViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        _viewModel = viewModel;
        foreach (var page in _pages.Values)
        {
            page.DataContext = viewModel;
        }

        if (_viewModel is null)
        {
            SettingsPageContent.Content = null;
            return;
        }

        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        ShowPage(_viewModel.SelectedSettingsPage);
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedSettingsPage) && _viewModel is not null)
        {
            ShowPage(_viewModel.SelectedSettingsPage);
        }
    }

    private void ShowPage(string? page)
    {
        var key = string.IsNullOrWhiteSpace(page) ? "General" : page;
        if (!_pages.TryGetValue(key, out var view))
        {
            view = CreatePage(key);
            _pages[key] = view;
        }

        view.DataContext = _viewModel;
        SettingsPageContent.Content = view;
    }

    private static UserControl CreatePage(string page) => page.ToLowerInvariant() switch
    {
        "security" => new SettingsSecurityView(),
        "securityrecovery" => new SettingsRecoveryView(),
        "data" => new SettingsDataView(),
        "desktop" => new SettingsDesktopView(),
        "integrations" => new SettingsIntegrationsView(),
        "about" => new SettingsAboutView(),
        "danger" => new SettingsDangerView(),
        _ => new SettingsGeneralView()
    };
}
