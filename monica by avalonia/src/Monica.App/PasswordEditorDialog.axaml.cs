using Avalonia.Controls;
using Monica.App.ViewModels;
using Monica.App.Features.Passwords;

namespace Monica.App;

public partial class PasswordEditorDialog : UserControl
{
    public PasswordEditorDialog()
    {
        InitializeComponent();
    }

    internal void FocusValidationTarget()
    {
        if ((DataContext as PasswordEditorViewModel)?.ValidationTarget == PasswordEditorValidationTarget.WifiSsid)
        {
            this.FindControl<WifiEditorView>("WifiEditor")?.FocusValidationTarget();
            return;
        }

        if ((DataContext as PasswordEditorViewModel)?.ValidationTarget == PasswordEditorValidationTarget.WifiMetadata)
        {
            this.FindControl<Expander>("AdvancedLoginExpander")!.IsExpanded = true;
        }

        var target = (DataContext as PasswordEditorViewModel)?.ValidationTarget switch
        {
            PasswordEditorValidationTarget.Title => this.FindControl<TextBox>("PasswordEditorTitleBox"),
            PasswordEditorValidationTarget.Password => this.FindControl<TextBox>("PasswordEditorPasswordBox"),
            PasswordEditorValidationTarget.WifiMetadata => this.FindControl<TextBox>("WifiMetadataBox"),
            _ => null
        };

        target?.BringIntoView();
        target?.Focus();
    }
}
