using Avalonia.Controls;
using Avalonia.Threading;
using Monica.App.Features.Unlock;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class VaultAccessUiTests
{
    public VaultAccessUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public void Vault_access_exposes_labeled_revealable_default_submit_controls()
    {
        var view = new UnlockView();

        Assert.NotNull(view.FindControl<StackPanel>("VaultAccessInitializingPanel"));
        Assert.NotNull(view.FindControl<StackPanel>("VaultAccessForm"));
        Assert.NotNull(view.FindControl<TextBlock>("MasterPasswordLabel"));
        Assert.NotNull(view.FindControl<TextBox>("MasterPasswordInput"));
        Assert.NotNull(view.FindControl<Button>("ToggleMasterPasswordVisibilityButton"));
        Assert.NotNull(view.FindControl<TextBlock>("ConfirmMasterPasswordLabel"));
        Assert.NotNull(view.FindControl<TextBox>("ConfirmMasterPasswordInput"));
        Assert.NotNull(view.FindControl<Button>("ToggleConfirmMasterPasswordVisibilityButton"));
        Assert.NotNull(view.FindControl<Border>("CreateVaultPasswordRequirementsPanel"));
        Assert.NotNull(view.FindControl<TextBlock>("CreateVaultPasswordLengthStatusText"));
        Assert.NotNull(view.FindControl<TextBlock>("CreateVaultPasswordConfirmationStatusText"));
        Assert.NotNull(view.FindControl<ProgressBar>("UnlockProgress"));

        var submit = view.FindControl<Button>("UnlockButton");
        Assert.NotNull(submit);
        Assert.True(submit.IsDefault);
    }

    [Fact]
    public void Vault_access_focuses_the_master_password_for_keyboard_entry()
    {
        var view = new UnlockView();
        var window = new Window { Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<TextBox>("MasterPasswordInput")!.IsFocused);
        window.Close();
    }
}
