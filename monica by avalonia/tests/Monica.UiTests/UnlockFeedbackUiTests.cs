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

    // The unlock workflow reports every failure through StatusMessage, and the lock screen is the
    // only surface that turns it into a banner. Nothing but this renders that chain.
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
}
