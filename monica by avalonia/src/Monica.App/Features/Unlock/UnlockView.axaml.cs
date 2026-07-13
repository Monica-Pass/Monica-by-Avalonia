using Avalonia.Controls;
using Avalonia.Threading;

namespace Monica.App.Features.Unlock;

public partial class UnlockView : UserControl
{
    public UnlockView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
            Dispatcher.UIThread.Post(() => MasterPasswordInput.Focus(), DispatcherPriority.Input);
    }
}
