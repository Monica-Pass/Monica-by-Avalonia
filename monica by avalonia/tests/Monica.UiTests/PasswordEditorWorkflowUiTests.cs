using Avalonia.Automation;
using Avalonia.Controls;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasswordEditorWorkflowUiTests
{
    public PasswordEditorWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Password_editor_focuses_the_invalid_field_and_exposes_inline_feedback()
    {
        var localization = new LocalizationService();
        var editor = new PasswordEditorViewModel(localization, new PasswordGeneratorService(), null, [], "");
        var view = new Monica.App.PasswordEditorDialog { DataContext = editor };
        var window = new Window { Content = view };

        Assert.NotNull(view.FindControl<ScrollViewer>("PasswordEditorFormScrollViewer"));
        Assert.NotNull(view.FindControl<Border>("PasswordIdentitySection"));
        Assert.NotNull(view.FindControl<Border>("PasswordCredentialSection"));
        Assert.NotNull(view.FindControl<Expander>("PasswordGeneratorOptionsExpander"));
        Assert.NotNull(view.FindControl<Border>("PasswordNotesSection"));

        window.Show();
        try
        {
            Assert.False(editor.Validate());
            view.FocusValidationTarget();

            var titleBox = view.FindControl<TextBox>("PasswordEditorTitleBox")!;
            var titleError = view.FindControl<TextBlock>("PasswordEditorTitleValidationText")!;
            Assert.True(titleBox.IsFocused);
            Assert.True(titleError.IsVisible);
            Assert.Equal(localization.Get("PasswordTitleRequired"), titleError.Text);

            editor.Title = "GitHub";
            Assert.False(editor.Validate());
            view.FocusValidationTarget();

            var passwordBox = view.FindControl<TextBox>("PasswordEditorPasswordBox")!;
            var passwordError = view.FindControl<TextBlock>("PasswordEditorPasswordValidationText")!;
            var visibilityButton = view.FindControl<Button>("PasswordEditorVisibilityButton")!;
            Assert.True(passwordBox.IsFocused);
            Assert.True(passwordError.IsVisible);
            Assert.Equal(localization.Get("PasswordValueRequired"), passwordError.Text);
            Assert.Equal(localization.Get("ShowPassword"), AutomationProperties.GetName(visibilityButton));
        }
        finally
        {
            window.Close();
        }
    }
}
