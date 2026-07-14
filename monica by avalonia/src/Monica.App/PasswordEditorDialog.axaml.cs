using Avalonia.Controls;
using Monica.App.ViewModels;

namespace Monica.App;

public partial class PasswordEditorDialog : UserControl
{
    public PasswordEditorDialog()
    {
        InitializeComponent();
    }

    internal void FocusValidationTarget()
    {
        var target = (DataContext as PasswordEditorViewModel)?.ValidationTarget switch
        {
            PasswordEditorValidationTarget.Title => this.FindControl<TextBox>("PasswordEditorTitleBox"),
            PasswordEditorValidationTarget.Password => this.FindControl<TextBox>("PasswordEditorPasswordBox"),
            _ => null
        };

        target?.BringIntoView();
        target?.Focus();
    }
}
