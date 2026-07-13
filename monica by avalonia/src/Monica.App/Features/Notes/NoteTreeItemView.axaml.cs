using Avalonia;
using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App.Features.Notes;

public partial class NoteTreeItemView : UserControl
{
    public static readonly StyledProperty<MainWindowViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<NoteTreeItemView, MainWindowViewModel?>(nameof(ViewModel));

    public NoteTreeItemView()
    {
        InitializeComponent();
    }

    public MainWindowViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
