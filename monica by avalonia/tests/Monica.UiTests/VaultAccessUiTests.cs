using Avalonia.Automation;
using Avalonia;
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
        var feedback = view.FindControl<TextBlock>("VaultAccessFeedbackText");
        Assert.NotNull(feedback);
        Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(feedback));

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

    [Fact]
    public void Vault_access_uses_stable_compact_form_margins_at_narrow_width()
    {
        var view = new UnlockView();

        view.UpdateResponsiveLayoutForWidth(480);

        Assert.True(view.IsCompactLayout);
        Assert.Equal(new Thickness(20, 16), view.FindControl<StackPanel>("VaultAccessPanel")!.Margin);

        view.UpdateResponsiveLayoutForWidth(900);

        Assert.False(view.IsCompactLayout);
        Assert.Equal(new Thickness(32, 24), view.FindControl<StackPanel>("VaultAccessPanel")!.Margin);
    }

    [Fact]
    public void Disabled_unlock_submit_declares_readable_semantic_tokens()
    {
        var xaml = File.ReadAllText(FindUnlockFeatureFile("UnlockView.axaml"));

        Assert.Contains("Button.unlockPrimaryCommand:disabled TextBlock", xaml, StringComparison.Ordinal);
        Assert.Contains("ControlFillColorSecondaryBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"Opacity\" Value=\"0.72\" />", xaml, StringComparison.Ordinal);
    }

    private static string FindUnlockFeatureFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Monica.App",
                "Features",
                "Unlock",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate {fileName} from the test output directory.");
    }
}
