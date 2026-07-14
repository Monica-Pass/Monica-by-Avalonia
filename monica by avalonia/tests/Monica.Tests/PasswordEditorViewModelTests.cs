using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Services;

namespace Monica.Tests;

public sealed class PasswordEditorViewModelTests
{
    [Fact]
    public void Password_editor_validation_targets_the_first_invalid_field_and_clears_when_corrected()
    {
        var editor = CreateEditor();

        Assert.False(editor.Validate());
        Assert.Equal(PasswordEditorValidationTarget.Title, editor.ValidationTarget);
        Assert.True(editor.HasTitleValidationError);
        Assert.False(editor.HasPasswordValidationError);

        editor.Title = "GitHub";

        Assert.False(editor.HasTitleValidationError);
        Assert.False(editor.Validate());
        Assert.Equal(PasswordEditorValidationTarget.Password, editor.ValidationTarget);
        Assert.True(editor.HasPasswordValidationError);

        editor.PasswordLines = "correct horse battery staple";

        Assert.False(editor.HasPasswordValidationError);
        Assert.True(editor.Validate());
        Assert.Equal(PasswordEditorValidationTarget.None, editor.ValidationTarget);
    }

    [Fact]
    public void Password_editor_clear_sensitive_state_releases_all_user_data_references()
    {
        var editor = CreateEditor();
        editor.Title = "Private account";
        editor.WebsiteLines = "https://private.example";
        editor.Username = "private-user";
        editor.PasswordLines = "plain-password";
        editor.Notes = "recovery note";
        editor.AuthenticatorKey = "otpauth://totp/private";
        editor.Email = "private@example.com";
        editor.Phone = "123456";
        editor.AddressLine = "private address";
        editor.CreditCardNumber = "4111111111111111";
        editor.CreditCardCvv = "123";
        editor.PasskeyBindings = "passkey-private";
        editor.SshKeyData = "private-key";
        editor.WifiMetadata = "wifi-secret";
        editor.CustomFieldsText = "!Recovery=private-value";
        editor.IsPasswordVisible = true;

        editor.ClearSensitiveState();

        Assert.True(editor.IsSensitiveStateCleared);
        Assert.Null(editor.Source);
        Assert.Empty(editor.CategoryOptions);
        Assert.Empty(editor.BoundNoteOptions);
        Assert.False(editor.IsPasswordVisible);
        Assert.Empty(editor.Title);
        Assert.Empty(editor.WebsiteLines);
        Assert.Empty(editor.Username);
        Assert.Empty(editor.PasswordLines);
        Assert.Empty(editor.Notes);
        Assert.Empty(editor.AuthenticatorKey);
        Assert.Empty(editor.Email);
        Assert.Empty(editor.Phone);
        Assert.Empty(editor.AddressLine);
        Assert.Empty(editor.CreditCardNumber);
        Assert.Empty(editor.CreditCardCvv);
        Assert.Empty(editor.PasskeyBindings);
        Assert.Empty(editor.SshKeyData);
        Assert.Empty(editor.WifiMetadata);
        Assert.Empty(editor.CustomFieldsText);
    }

    private static PasswordEditorViewModel CreateEditor() =>
        new(new LocalizationService(), new PasswordGeneratorService(), null, [], "");
}
