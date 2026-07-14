using Avalonia.Controls;

namespace Monica.App.Features.Authenticator;

public partial class AuthenticatorCodeConsoleView : UserControl
{
    public AuthenticatorCodeConsoleView()
    {
        InitializeComponent();
    }

    public void SetBackButtonVisible(bool isVisible) => BackToAuthenticatorListButton.IsVisible = isVisible;
}
