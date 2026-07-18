using Avalonia.Controls;

namespace Monica.App.Features.Passwords;

public partial class PasswordFolderFilterView : UserControl
{
    public PasswordFolderFilterView()
    {
        InitializeComponent();
    }

    public bool ShowCompactFolderPicker
    {
        get => CompactFolderCommands.IsVisible;
        set => CompactFolderCommands.IsVisible = value;
    }
}
