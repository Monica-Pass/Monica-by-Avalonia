using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Monica.App.Controls;
using Monica.App.Features.Generator;
using Monica.App.Features.Notes;
using Monica.App.Features.Passwords;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class MainWindow
{
    private int _workspaceActivationVersion;

    private WorkspaceHostView? CurrentWorkspaceHost =>
        this.GetVisualDescendants().OfType<WorkspaceHostView>().FirstOrDefault();

    private WorkspaceHostView WorkspaceHost =>
        CurrentWorkspaceHost ?? throw new InvalidOperationException("The unlocked workspace shell is not active.");

    private PasswordVaultView PasswordVaultView =>
        WorkspaceHost.GetOrCreate<PasswordVaultView>("Passwords");

    private NoteWorkspaceView NoteWorkspaceView =>
        WorkspaceHost.GetOrCreate<NoteWorkspaceView>("Notes");

    private GeneratorWorkspaceView GeneratorWorkspaceView =>
        WorkspaceHost.GetOrCreate<GeneratorWorkspaceView>("Generator");

    private void WorkspaceHost_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OtherWorkspaceViewportWidth = e.NewSize.Width;
            viewModel.OtherWorkspaceViewportHeight = e.NewSize.Height;
        }
    }

    private void UpdateWorkspaceActivation(MainWindowViewModel? viewModel)
    {
        var activationVersion = ++_workspaceActivationVersion;
        var currentHost = CurrentWorkspaceHost;
        if (viewModel is not { IsUnlocked: true })
        {
            if (currentHost is not null)
            {
                currentHost.IsActive = false;
            }

            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (activationVersion == _workspaceActivationVersion &&
                    DataContext is MainWindowViewModel { IsUnlocked: true } &&
                    CurrentWorkspaceHost is { } workspaceHost)
                {
                    workspaceHost.IsActive = true;
                }
            },
            DispatcherPriority.Background);
    }
}
