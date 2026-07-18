using Avalonia.Controls;

namespace Monica.App.Features.Wallet;

public partial class WalletItemListView : UserControl
{
    public WalletItemListView()
    {
        InitializeComponent();
    }

    public ListBox ItemList => WalletItemList;
}
