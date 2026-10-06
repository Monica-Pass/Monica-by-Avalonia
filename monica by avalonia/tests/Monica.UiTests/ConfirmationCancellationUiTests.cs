using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Monica.App.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class ConfirmationCancellationUiTests
{
    private const string PrivateMessage = "Remove alice@private.example from private.example?";
    private const string PrivateTitle = "alice@private.example";
    private const string RequiredPhrase = "REMOVE alice@private.example";

    public ConfirmationCancellationUiTests() => AvaloniaUiThreadTestContext.VerifyAccess();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Session_cancellation_closes_the_real_dialog_and_scrubs_retained_controls(
        bool typedConfirmation, bool cancelFromWorker)
    {
        var window = new Window { Width = 800, Height = 600 };
        using var cancellation = new CancellationTokenSource();
        window.Show();
        try
        {
            var service = new ConfirmationDialogService(() => window, new LocalizationService());
            var pending = typedConfirmation
                ? service.ConfirmTypedAsync(PrivateTitle, PrivateMessage, RequiredPhrase,
                    "Enter the phrase for alice@private.example", "Remove", "Cancel", cancellation.Token)
                : service.ConfirmAsync(PrivateTitle, PrivateMessage, "Remove", "Cancel", cancellation.Token);
            Dispatcher.UIThread.RunJobs();
            var dialog = FindDialog(window);
            var heldText = ContentText(dialog);
            var heldInput = typedConfirmation
                ? Assert.Single(((StackPanel)dialog.Content!).Children.OfType<TextBox>())
                : null;
            if (heldInput is not null) heldInput.Text = "entered-private-secret";
            Assert.Contains(heldText, text => text.Text == PrivateMessage);

            if (cancelFromWorker) await Task.Run(cancellation.Cancel, TestContext.Current.CancellationToken);
            else cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();

            // Content must be scrubbed before the library's close animation finishes.
            Assert.All(heldText, text => Assert.Equal("", text.Text));
            Assert.Null(dialog.Title);
            Assert.Null(dialog.Content);
            if (heldInput is not null)
            {
                Assert.Equal("", heldInput.Text);
                Assert.Equal("", heldInput.PlaceholderText);
            }
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AwaitDialogAsync(pending));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.Empty(window.GetVisualDescendants().OfType<FAContentDialog>());
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("private.example", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task Cancellation_between_the_initial_check_and_ShowAsync_still_dismisses_the_overlay()
    {
        var window = new Window { Width = 800, Height = 600 };
        using var cancellation = new CancellationTokenSource();
        window.Show();
        try
        {
            var service = new ConfirmationDialogService(() =>
            {
                cancellation.Cancel(); // owner is resolved immediately before ShowAsync
                return window;
            }, new LocalizationService());
            var pending = service.ConfirmAsync(PrivateTitle, PrivateMessage, "Remove", "Cancel", cancellation.Token);
            Dispatcher.UIThread.RunJobs();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AwaitDialogAsync(pending));
            Assert.Empty(window.GetVisualDescendants().OfType<FAContentDialog>());
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task Cancellation_during_the_primary_button_click_cannot_approve_the_operation()
    {
        var window = new Window { Width = 800, Height = 600 };
        using var cancellation = new CancellationTokenSource();
        window.Show();
        try
        {
            var service = new ConfirmationDialogService(() => window, new LocalizationService());
            var pending = service.ConfirmAsync(PrivateTitle, PrivateMessage, "Remove", "Cancel", cancellation.Token);
            Dispatcher.UIThread.RunJobs();
            var dialog = FindDialog(window);
            var heldText = ContentText(dialog);
            dialog.PrimaryButtonClick += (_, _) => cancellation.Cancel();

            ClickDialogButton(dialog, "PrimaryButton");

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AwaitDialogAsync(pending));
            Assert.All(heldText, text => Assert.Equal("", text.Text));
            Assert.Empty(window.GetVisualDescendants().OfType<FAContentDialog>());
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Normal_confirmation_buttons_preserve_the_user_decision(bool approve)
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        try
        {
            var service = new ConfirmationDialogService(() => window, new LocalizationService());
            var pending = service.ConfirmAsync(PrivateTitle, PrivateMessage, "Remove", "Cancel", TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            var dialog = FindDialog(window);
            var heldText = ContentText(dialog);

            ClickDialogButton(dialog, approve ? "PrimaryButton" : "CloseButton");

            Assert.Equal(approve, await AwaitDialogAsync(pending));
            Assert.All(heldText, text => Assert.Equal("", text.Text));
            Assert.Null(dialog.Content);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Typed_confirmation_checks_the_phrase_before_scrubbing_the_input(
        bool approve, bool correctPhrase, bool expected)
    {
        var window = new Window { Width = 800, Height = 600 };
        window.Show();
        try
        {
            var service = new ConfirmationDialogService(() => window, new LocalizationService());
            var pending = service.ConfirmTypedAsync(PrivateTitle, PrivateMessage, RequiredPhrase,
                "Type the phrase", "Remove", "Cancel", TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            var dialog = FindDialog(window);
            var input = Assert.Single(((StackPanel)dialog.Content!).Children.OfType<TextBox>());
            input.Text = correctPhrase ? " " + RequiredPhrase + " " : "incorrect-private-text";

            ClickDialogButton(dialog, approve ? "PrimaryButton" : "CloseButton");

            Assert.Equal(expected, await AwaitDialogAsync(pending));
            Assert.Equal("", input.Text);
            Assert.Equal("", input.PlaceholderText);
            Assert.Null(dialog.Content);
        }
        finally { window.Close(); }
    }

    private static FAContentDialog FindDialog(Window window) =>
        Assert.Single(window.GetVisualDescendants().OfType<FAContentDialog>());

    private static TextBlock[] ContentText(FAContentDialog dialog) => dialog.Content switch
    {
        TextBlock text => [text],
        StackPanel panel => panel.Children.OfType<TextBlock>().ToArray(),
        _ => throw new InvalidOperationException("Expected confirmation content.")
    };

    private static void ClickDialogButton(FAContentDialog dialog, string name) =>
        Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => button.Name == name)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Task<bool> AwaitDialogAsync(Task<bool> pending) =>
        pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
}
