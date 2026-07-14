using Avalonia.Controls;

namespace Monica.App.Features.Wallet;

public partial class WalletWorkbenchView : UserControl
{
    public WalletWorkbenchView()
    {
        InitializeComponent();
    }

    public void SetBackButtonVisible(bool isVisible) => BackToWalletListButton.IsVisible = isVisible;

    public void SetCompactSupplementVisible(bool isVisible) => WalletCompactSupplement.IsVisible = isVisible;
}
