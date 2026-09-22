using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.ViewModels;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class UnlockFeedbackUiTests
{
    public UnlockFeedbackUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    // The unlock workflow reports every failure through StatusMessage, and while locked the banner
    // below the password box is the only surface that renders it. Nothing else covers that chain.
    [Fact]
    public async Task Unlock_failure_renders_the_error_banner_and_corrected_input_clears_it()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();

        window.Show();
        try
        {
            window.DataContext = viewModel;
            Dispatcher.UIThread.RunJobs();

            await viewModel.InitializeCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            var banner = Banner(window);
            var feedback = Feedback(window);

            // The lock screen always carries a neutral status line; a failure is the `error` class.
            Assert.True(banner.IsVisible);
            Assert.DoesNotContain("error", banner.Classes);

            await viewModel.UnlockCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(banner.IsVisible);
            Assert.Contains("error", banner.Classes);
            Assert.Equal(viewModel.L.Get("EnterMasterPassword"), feedback.Text);
            Assert.NotEqual("EnterMasterPassword", feedback.Text);

            viewModel.MasterPassword = "corrected-input";
            Dispatcher.UIThread.RunJobs();

            Assert.False(banner.IsVisible);
            Assert.DoesNotContain("error", banner.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    private static TextBlock Feedback(Visual root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(text => text.Name == "VaultAccessFeedbackText")
        ?? throw new InvalidOperationException("Unlock feedback text is not in the visual tree.");

    private static Border Banner(Visual root) =>
        Feedback(root).Parent as Border
            ?? throw new InvalidOperationException("Unlock feedback banner is not in the visual tree.");

    // Once unlocked, the same status line earns an amber strip above the workspace. Nothing else
    // in the suite reaches that binding, so a mistyped style class or property name would keep the
    // strip permanently hidden and every test would still be green.
    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public async Task Failed_operation_renders_the_workspace_strip_and_an_information_clears_it(
        string language)
    {
        using var library = LibraryUiHarness.Open();
        library.ViewModel.L.SetLanguage(language);
        library.Settle();
        var strip = Strip(library.Window);

        Assert.False(strip.IsVisible);

        library.ViewModel.NewFolderName = "";
        await library.ViewModel.CreateVaultFolderCommand.ExecuteAsync(null);
        library.Settle();

        Assert.True(strip.IsVisible, language);
        // The class and the style selector can drift apart without affecting IsVisible, which would
        // leave an unstyled strip that reads as ordinary content.
        Assert.Equal(1d, strip.BorderThickness.Bottom);
        Assert.NotNull(strip.BorderBrush);
        Assert.Equal(library.ViewModel.L.Get("FolderNameRequired"), Message(strip));
        Assert.NotEqual("FolderNameRequired", Message(strip));

        await library.ViewModel.RenameSelectedVaultFolderCommand.ExecuteAsync(null);
        library.Settle();

        Assert.False(strip.IsVisible);
        Assert.Equal(library.ViewModel.L.Get("SelectFolderToManage"), Message(strip));
    }

    // Switching language in Settings must retranslate the sentence that is already on screen, not
    // just the chrome around it. Driven through SettingsLanguage so the production wiring — the
    // settings callback, the localization service notification and the view model refresh — is all
    // in the path, and read off the rendered strip rather than the view model property.
    [Fact]
    public async Task Language_switch_retranslates_the_status_line_rendered_in_the_strip()
    {
        using var library = LibraryUiHarness.Open();
        library.ViewModel.NewFolderName = "";

        await library.ViewModel.CreateVaultFolderCommand.ExecuteAsync(null);
        library.Settle();

        var strip = Strip(library.Window);
        Assert.True(strip.IsVisible);
        var english = Message(strip);
        Assert.Equal(library.ViewModel.L.Get("FolderNameRequired"), english);

        library.ViewModel.SettingsLanguage = "zh-CN";
        library.Settle();

        var chinese = library.ViewModel.L.Get("FolderNameRequired");
        Assert.NotEqual(english, chinese);
        Assert.Equal(chinese, Message(strip));
        // The tone is a fact about the operation, not about the wording, so it survives the flip.
        Assert.True(strip.IsVisible);
    }

    private static Border Strip(Visual root) =>
        root.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border => border.Classes.Contains("workspaceFailureStatus"))
        ?? throw new InvalidOperationException("Workspace failure strip is not in the visual tree.");

    private static string Message(Border strip) =>
        strip.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Workspace failure strip carries no message text.");
}
