using Avalonia;
using Avalonia.Controls;

namespace Monica.App;

public partial class PasswordDetailDialog : UserControl
{
    public static readonly StyledProperty<bool> ShowHeaderProperty =
        AvaloniaProperty.Register<PasswordDetailDialog, bool>(nameof(ShowHeader), true);

    public PasswordDetailDialog()
    {
        InitializeComponent();
    }

    public bool ShowHeader
    {
        get => GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }
}
