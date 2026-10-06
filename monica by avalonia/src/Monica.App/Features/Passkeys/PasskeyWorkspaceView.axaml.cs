using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Monica.App.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Passkeys;

public partial class PasskeyWorkspaceView : UserControl
{
    private PasskeyWorkspaceViewModel? _observed;

    public PasskeyWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveViewModel();
        SizeChanged += (_, _) => ApplyLayout();
        KeyDown += OnKeyDown;
        AttachedToVisualTree += (_, _) => ObserveViewModel();
        DetachedFromVisualTree += (_, _) => StopObserving();
    }

    public void UpdateResponsiveLayoutForWidth(double width)
    {
        var narrow = width > 0 && width < LifecycleWorkspaceLayout.NarrowBreakpoint;
        var details = narrow && _observed?.HasSelection == true;
        LifecycleWorkspaceLayout.ApplyContent(PasskeyMasterDetailGrid, PasskeyListRegion, PasskeyDetailRegion,
            PasskeyBackButton, narrow, details, 340);
    }

    private void ObserveViewModel()
    {
        StopObserving();
        _observed = DataContext as PasskeyWorkspaceViewModel;
        if (_observed is not null)
        {
            _observed.PropertyChanged += OnViewModelChanged;
            _ = _observed.EnsureLoadedAsync();
        }
        ApplyLayout();
    }

    private void StopObserving()
    {
        if (_observed is not null) _observed.PropertyChanged -= OnViewModelChanged;
        _observed = null;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PasskeyWorkspaceViewModel.SelectedItem) or nameof(PasskeyWorkspaceViewModel.HasSelection))
            Dispatcher.UIThread.Post(ApplyLayout);
    }

    private void ApplyLayout() => UpdateResponsiveLayoutForWidth(Bounds.Width);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            PasskeySearchBox.Focus();
            PasskeySearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _observed is { } viewModel)
        {
            if (viewModel.SearchText.Length > 0) viewModel.ClearSearchCommand.Execute(null);
            else if (viewModel.HasSelection) viewModel.BackToListCommand.Execute(null);
            else return;
            e.Handled = true;
        }
    }
}
