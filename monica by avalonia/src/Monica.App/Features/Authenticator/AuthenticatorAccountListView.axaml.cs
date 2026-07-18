using Avalonia.Controls;

namespace Monica.App.Features.Authenticator;

public partial class AuthenticatorAccountListView : UserControl
{
    public AuthenticatorAccountListView()
    {
        InitializeComponent();
    }

    public ListBox AccountList => AuthenticatorAccountList;
}
