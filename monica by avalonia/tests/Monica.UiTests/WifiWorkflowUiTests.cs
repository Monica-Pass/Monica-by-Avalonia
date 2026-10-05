using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Monica.App;
using Monica.App.Features.Passwords;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class WifiWorkflowUiTests
{
    [Fact]
    public void Wifi_form_binds_common_fields_import_and_invalid_field_focus()
    {
        using var editor = new PasswordEditorViewModel(new LocalizationService(), new PasswordGeneratorService(),
            new PasswordEntry { Title = "Router", LoginType = PasswordLoginType.Wifi }, [], "password");
        var view = new PasswordEditorDialog { DataContext = editor };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var wifi = view.FindControl<WifiEditorView>("WifiEditor")!;
            Assert.True(wifi.IsVisible);
            var ssid = wifi.FindControl<TextBox>("WifiSsidBox")!;
            ssid.Text = "";
            Assert.False(editor.Validate());
            view.FocusValidationTarget();
            Assert.True(ssid.IsFocused);

            ssid.Text = "Home";
            wifi.FindControl<CheckBox>("WifiHiddenNetworkBox")!.IsChecked = true;
            Assert.Equal("Home", editor.WifiSsid);
            Assert.True(editor.WifiHiddenNetwork);
            var import = wifi.FindControl<Expander>("WifiQrImportExpander")!;
            import.IsExpanded = true;
            wifi.FindControl<TextBox>("WifiQrTextBox")!.Text = "WIFI:T:nopass;S:Guest;;";
            wifi.FindControl<Button>("WifiImportTextButton")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Guest", ssid.Text);
            Assert.True(editor.Validate());
            Assert.False(view.FindControl<Border>("PasswordCredentialSection")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Wifi_detail_requires_explicit_qr_reveal_and_removes_bitmap_on_lock()
    {
        var crypto = new CryptoService();
        crypto.InitializeSession("wifi-ui-test-master", Enumerable.Repeat((byte)7, 16).ToArray());
        var entry = new PasswordEntry
        {
            Title = "Home",
            LoginType = PasswordLoginType.Wifi,
            Password = crypto.EncryptString("password"),
            WifiMetadata = """{"ssid":"家用网络"}"""
        };
        using var detail = new PasswordDetailViewModel(new LocalizationService(), new Clipboard(), crypto,
            new TotpService(), entry, [entry], null, null, [], []);
        var view = new PasswordDetailDialog { DataContext = detail };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var wifi = Assert.Single(view.GetVisualDescendants().OfType<WifiDetailView>());
            var preview = wifi.FindControl<Border>("WifiQrPreview")!;
            Assert.False(preview.IsVisible);
            wifi.FindControl<Button>("WifiShowQrButton")!.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(preview.IsVisible);
            Assert.NotNull(detail.WifiQrImage);
            detail.ClearSensitiveState();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(detail.WifiQrImage);
            Assert.False(preview.IsVisible);
            Assert.False(wifi.FindControl<Button>("WifiCopyQrButton")!.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class Clipboard : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
