using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Monica.App.Features.Unlock;

/// <summary>
/// Keeps MainWindow's cold visual composition small while the full Vault Access
/// form is parsed on the UI dispatcher after the first frame is available.
/// </summary>
public sealed class UnlockViewHost : UserControl
{
    private bool _viewCreated;
    private bool _creationQueued;

    public UnlockViewHost()
    {
        Content = CreateLoadingPlaceholder();
        QueueUnlockViewCreation();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (IsVisible)
        {
            QueueUnlockViewCreation();
        }
        else
        {
            ReleaseUnlockView();
        }
    }

    private void QueueUnlockViewCreation()
    {
        if (_viewCreated || _creationQueued)
        {
            return;
        }

        Content ??= CreateLoadingPlaceholder();
        _creationQueued = true;
        Dispatcher.UIThread.Post(EnsureUnlockView, DispatcherPriority.Background);
    }

    private void EnsureUnlockView()
    {
        _creationQueued = false;
        if (_viewCreated)
        {
            return;
        }

        if (!IsVisible)
        {
            return;
        }

        _viewCreated = true;
        Content = new UnlockView();
    }

    private void ReleaseUnlockView()
    {
        _creationQueued = false;
        if (Content is UnlockView view)
        {
            view.Dispose();
        }

        Content = null;
        _viewCreated = false;
    }

    private static Control CreateLoadingPlaceholder() =>
        new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children =
            {
                new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Monica",
                            FontSize = 24,
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new ProgressBar
                        {
                            Width = 220,
                            Height = 4,
                            IsIndeterminate = true
                        }
                    }
                }
            }
        };
}
