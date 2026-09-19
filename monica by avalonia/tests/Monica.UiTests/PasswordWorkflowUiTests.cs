using System.Windows.Input;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Features.Passwords;
using Monica.App.Features.Vault;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasswordWorkflowUiTests
{
    public PasswordWorkflowUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    // The password list page owned the search field, its clear button and the spoken result count.
    // The library is the one search field left, so it has to carry all three.
    [Fact]
    public async Task Password_search_narrows_the_tree_and_reports_what_is_left()
    {
        using var library = LibraryUiHarness.Open("Passwords");
        // updated-desc is the default sort, so the expected row order only means something if the
        // timestamps are authored here instead of falling out of DateTimeOffset.UtcNow per entry.
        var now = DateTimeOffset.UtcNow;
        library.ViewModel.Passwords.Add(new PasswordEntry
        {
            Id = 41,
            Title = "Alpha mail",
            CreatedAt = now.AddMinutes(-2),
            UpdatedAt = now.AddMinutes(-1)
        });
        library.ViewModel.Passwords.Add(new PasswordEntry
        {
            Id = 42,
            Title = "Beta bank",
            CreatedAt = now.AddMinutes(-4),
            UpdatedAt = now.AddMinutes(-3)
        });
        library.Settle();

        var searchBox = library.Workspace.FindControl<TextBox>("VaultSearchBox")!;
        var status = library.Workspace.FindControl<TextBlock>("VaultFilteredStatusText")!;
        var clear = library.Workspace.FindControl<Button>("VaultSearchClearButton")!;
        var localization = library.Services.GetRequiredService<ILocalizationService>();

        Assert.True(searchBox.IsVisible);
        Assert.False(clear.IsVisible);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(searchBox)));

        searchBox.Text = "Alpha";
        library.Settle();

        Assert.Equal("Alpha", library.ViewModel.VaultSearchText);
        Assert.True(clear.IsVisible);
        Assert.Equal(localization.Format("VaultFilteredStatusFormat", 1, 2), status.Text);
        await library.AwaitTreeRefresh();
        Assert.Equal(["p:41"], EntryKeys(library.ViewModel));

        Assert.Same(library.ViewModel.ClearVaultSearchCommand, clear.Command);
        clear.Command!.Execute(null);
        library.Settle();

        Assert.Empty(library.ViewModel.VaultSearchText);
        Assert.False(clear.IsVisible);
        Assert.Equal("", status.Text);
        await library.AwaitTreeRefresh();
        Assert.Equal(["p:41", "p:42"], EntryKeys(library.ViewModel));
    }

    [Fact]
    public void Password_detail_is_the_only_surface_a_credential_row_opens()
    {
        using var library = LibraryUiHarness.Open("Passwords");
        library.ViewModel.Passwords.Add(new PasswordEntry
        {
            Id = 43,
            Title = "Mail",
            Username = "jo@example.test",
            Website = "https://example.test"
        });
        library.Settle();

        Assert.Null(library.SurfaceHost.Content);
        Assert.False(library.ViewModel.HasVaultSelection);

        library.SelectFirstEntry();

        var detail = Assert.IsType<PasswordDetailPaneView>(library.SurfaceHost.Content);
        Assert.Equal(VaultSurface.Password, library.ViewModel.SelectedVaultSurface);
        Assert.Same(library.ViewModel, detail.DataContext);
        Assert.NotNull(library.ViewModel.SelectedPassword);
        Assert.Single(library.Window.GetVisualDescendants().OfType<PasswordDetailPaneView>());
    }

    [Fact]
    public void Password_detail_promotes_copy_actions_and_keeps_recovery_in_place()
    {
        using var library = LibraryUiHarness.Open("Passwords");
        library.ViewModel.Passwords.Add(new PasswordEntry { Id = 44, Title = "Bank" });
        library.Settle();
        library.SelectFirstEntry();

        var detail = Assert.IsType<PasswordDetailPaneView>(library.SurfaceHost.Content);
        var selected = library.ViewModel.SelectedPassword!;

        AssertCommand(detail, "PasswordDetailCopyPasswordButton", library.ViewModel.CopyPasswordCommand, selected);
        AssertCommand(detail, "PasswordDetailCopyUsernameButton", library.ViewModel.CopyUsernameCommand, selected);
        AssertCommand(detail, "PasswordDetailCopyWebsiteButton", library.ViewModel.CopyWebsiteCommand, selected);
        Assert.NotNull(detail.FindControl<Button>("PasswordDetailMoreButton"));
        Assert.NotNull(detail.FindControl<StackPanel>("PasswordDetailLoadingState"));
        Assert.NotNull(detail.FindControl<StackPanel>("PasswordDetailErrorState"));
        Assert.Equal(
            library.ViewModel.RetrySelectedPasswordDetailsCommand,
            detail.FindControl<Button>("RetryPasswordDetailsButton")!.Command);
    }

    private static void AssertCommand(
        PasswordDetailPaneView detail,
        string name,
        ICommand expected,
        PasswordEntry selected)
    {
        var button = detail.FindControl<Button>(name)!;
        Assert.Same(expected, button.Command);
        Assert.Same(selected, button.CommandParameter);
    }

    private static string[] EntryKeys(MainWindowViewModel viewModel) =>
        [.. viewModel.VaultTreeRows.OfType<VaultTreeEntryRow>().Select(row => row.Key)];
}
