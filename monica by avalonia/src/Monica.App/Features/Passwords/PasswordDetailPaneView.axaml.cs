using Avalonia.Controls;

namespace Monica.App.Features.Passwords;

public partial class PasswordDetailPaneView : UserControl
{
    public PasswordDetailPaneView()
    {
        InitializeComponent();
    }

    public bool ShowBackButton
    {
        get => BackToPasswordListButton.IsVisible;
        set => BackToPasswordListButton.IsVisible = value;
    }
}
