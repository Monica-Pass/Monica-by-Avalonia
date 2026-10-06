using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Controls;
using Monica.App.Features.Passkeys;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Passkeys;
using Monica.Data.Passkeys;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class PasskeyWorkflowUiTests
{
    public PasskeyWorkflowUiTests() => AvaloniaUiThreadTestContext.VerifyAccess();

    [Fact]
    public void Navigation_renders_search_and_public_account_details_and_lock_clears_the_projection()
    {
        var window = new Monica.App.MainWindow();
        var store = new FakeStore();
        using var services = Monica.App.App.ConfigureServices(window, registrations => registrations.AddSingleton<IPasskeyStore>(store));
        var main = services.GetRequiredService<MainWindowViewModel>();
        window.DataContext = main;
        window.Show();
        try
        {
            main.IsUnlocked = true;
            main.SelectSectionCommand.Execute("Passkeys");
            Dispatcher.UIThread.RunJobs();
            var workspace = Assert.Single(window.GetVisualDescendants().OfType<PasskeyWorkspaceView>());
            var list = workspace.FindControl<ListBox>("PasskeyList")!;
            var search = workspace.FindControl<TextBox>("PasskeySearchBox")!;
            Assert.Same(main.Passkeys, workspace.DataContext);
            Assert.Equal(2, main.Passkeys.Items.Count);
            Assert.Equal(1, store.ListCalls);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(search)));
            Assert.Contains("Passkeys", Assert.Single(window.GetVisualDescendants().OfType<WorkspaceHostView>()).CreatedSections);

            list.SelectedItem = main.Passkeys.Items[0];
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Alice", workspace.FindControl<SelectableTextBlock>("PasskeyAccountText")!.Text);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("private-key-sentinel") == true);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text?.Contains("private-key-sentinel") == true);

            search.Text = "BOB";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Bob", Assert.Single(main.Passkeys.Items).UserName);
            Assert.Null(main.Passkeys.SelectedItem);
            list.SelectedItem = main.Passkeys.Items[0];
            Dispatcher.UIThread.RunJobs();
            var held = main.Passkeys.SelectedItem!;
            main.IsUnlocked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(main.Passkeys.Items);
            Assert.Empty(held.UserName);
            Assert.Empty(held.CredentialId);
            Assert.Null(main.Passkeys.SelectedItem);
            Assert.Empty(window.GetVisualDescendants().OfType<PasskeyWorkspaceView>());
            Assert.Equal(0, store.PrivateKeyReads);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task Narrow_layout_provides_back_navigation_and_keyboard_search_without_adding_another_workspace()
    {
        var store = new FakeStore();
        var localization = new Monica.App.Services.LocalizationService();
        localization.SetLanguage("en-US");
        var model = new PasskeyWorkspaceViewModel(store, new Monica.App.Services.DisabledConfirmationDialogService(),
            localization, () => true, () => CancellationToken.None);
        await model.RefreshAsync();
        var view = new PasskeyWorkspaceView { DataContext = model };
        var window = new Window { Width = 680, Height = 500, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Border>("PasskeyListRegion")!.IsVisible);
            Assert.False(view.FindControl<Border>("PasskeyDetailRegion")!.IsVisible);
            model.SelectedItem = model.Items[0];
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.FindControl<Border>("PasskeyListRegion")!.IsVisible);
            Assert.True(view.FindControl<Border>("PasskeyDetailRegion")!.IsVisible);
            var back = view.FindControl<Button>("PasskeyBackButton")!;
            Assert.True(back.IsVisible);
            back.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(model.SelectedItem);
            Assert.True(view.FindControl<Border>("PasskeyListRegion")!.IsVisible);

            var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control, Source = back };
            view.RaiseEvent(args);
            Dispatcher.UIThread.RunJobs();
            Assert.True(args.Handled);
            Assert.True(view.FindControl<TextBox>("PasskeySearchBox")!.IsFocused);
        }
        finally { window.Close(); model.ClearSensitiveState(); }
    }

    [Fact]
    public async Task Platform_selection_displays_remove_record_and_language_changes_update_the_live_page()
    {
        var localization = new Monica.App.Services.LocalizationService();
        localization.SetLanguage("en-US");
        var model = new PasskeyWorkspaceViewModel(new FakeStore(), new Monica.App.Services.DisabledConfirmationDialogService(),
            localization, () => true, () => CancellationToken.None);
        await model.RefreshAsync();
        var view = new PasskeyWorkspaceView { DataContext = model };
        var window = new Window { Content = view, Width = 1000, Height = 600 };
        window.Show();
        try
        {
            model.SelectedItem = model.Items[1];
            Dispatcher.UIThread.RunJobs();
            var delete = view.FindControl<Button>("PasskeyDeleteButton")!;
            Assert.Equal("Remove local record", delete.Content);
            localization.SetLanguage("zh-CN");
            model.RefreshLocalization();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("移除本地记录", delete.Content);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "通行密钥");
        }
        finally { window.Close(); model.ClearSensitiveState(); }
    }

    private sealed class FakeStore : IPasskeyStore
    {
        public int ListCalls { get; private set; }
        public int PrivateKeyReads { get; private set; }
        public Task<IReadOnlyList<PasskeyEntry>> ListAllAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<PasskeyEntry>>([
                new() { Id = 1, RpId = "example.test", RpName = "Example", UserName = "Alice", CredentialId = "public-one", PasskeyMode = PasskeyModes.BitwardenCompatible, PrivateKeyAlias = "private-key-sentinel" },
                new() { Id = 2, RpId = "other.test", RpName = "Other", UserName = "Bob", CredentialId = "public-two", PasskeyMode = PasskeyModes.WindowsHello, PrivateKeyAlias = "private-key-sentinel" }
            ]);
        }
        public Task<string?> ResolvePrivateKeyAsync(PasskeyEntry entry, CancellationToken cancellationToken = default)
        { PrivateKeyReads++; return Task.FromResult<string?>("private-key-sentinel"); }
        public Task<long> SaveAsync(PasskeyEntry entry, string? privateKeyPkcs8Base64 = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PasskeyEntry?> GetAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PasskeyEntry?> FindAsync(string credentialId, string? rpId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PasskeyEntry>> ListByRpIdAsync(string rpId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkUsedAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
