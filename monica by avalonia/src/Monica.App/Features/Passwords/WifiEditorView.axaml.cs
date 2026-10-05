using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Passwords;

public partial class WifiEditorView : UserControl
{
    public WifiEditorView() => InitializeComponent();

    internal void FocusValidationTarget()
    {
        var target = this.FindControl<TextBox>("WifiSsidBox");
        if ((DataContext as PasswordEditorViewModel)?.ValidationTarget == PasswordEditorValidationTarget.WifiSsid)
        {
            target?.BringIntoView();
            target?.Focus();
        }
    }
}
