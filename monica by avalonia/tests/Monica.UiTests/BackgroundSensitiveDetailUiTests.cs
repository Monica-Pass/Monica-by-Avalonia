using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.UiTests;

[Collection(AvaloniaUiTestCollection.Name)]
public sealed class BackgroundSensitiveDetailUiTests
{
    public BackgroundSensitiveDetailUiTests()
    {
        AvaloniaUiThreadTestContext.VerifyAccess();
    }

    [Fact]
    public async Task Minimize_releases_sensitive_details_and_restores_them_on_demand()
    {
        var window = new Monica.App.MainWindow();
        using var services = Monica.App.App.ConfigureServices(window);
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var password = new PasswordEntry
        {
            Id = 1001,
            Title = "Background password",
            Username = "background-user",
            Password = "background-secret"
        };
        var totp = new SecureItem
        {
            Id = 1002,
            ItemType = VaultItemType.Totp,
            Title = "Background authenticator",
            Notes = "background-account"
        };
        var wallet = new SecureItem
        {
            Id = 1003,
            ItemType = VaultItemType.BankCard,
            Title = "Background wallet",
            ItemData = "{\"cardNumber\":\"4111111111111111\",\"cvv\":\"123\"}"
        };
        viewModel.Passwords.Add(password);
        viewModel.TotpItems.Add(totp);
        viewModel.WalletItems.Add(wallet);
        viewModel.SelectedPassword = password;
        var passwordDetails = new PasswordDetailViewModel(
            services.GetRequiredService<ILocalizationService>(),
            services.GetRequiredService<IClipboardService>(),
            services.GetRequiredService<ICryptoService>(),
            services.GetRequiredService<ITotpService>(),
            password,
            [password],
            category: null,
            boundNote: null,
            attachments: [],
            customFields: []);
        viewModel.SelectedPasswordDetails = passwordDetails;
        viewModel.SelectedTotpItem = totp;
        viewModel.SelectedWalletItem = wallet;
        viewModel.SelectedSection = "Cards";
        window.Show();
        try
        {
            window.DataContext = viewModel;
            viewModel.IsUnlocked = true;
            Assert.NotNull(viewModel.SelectedPasswordDetails);
            Assert.NotNull(viewModel.SelectedTotpDetails);
            var walletDetails = Assert.IsType<WalletItemDetailsViewModel>(viewModel.SelectedWalletDetails);
            Assert.Contains(walletDetails.Fields, field => field.Value == "4111 1111 1111 1111");

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();

            Assert.Null(viewModel.SelectedPasswordDetails);
            Assert.Null(viewModel.SelectedTotpDetails);
            Assert.Null(viewModel.SelectedWalletDetails);
            Assert.True(passwordDetails.IsSensitiveStateCleared);
            Assert.True(walletDetails.IsSensitiveStateCleared);
            Assert.Empty(walletDetails.Fields);
            Assert.Same(password, viewModel.SelectedPassword);
            Assert.Same(totp, viewModel.SelectedTotpItem);
            Assert.Same(wallet, viewModel.SelectedWalletItem);
            Assert.Equal("background-secret", password.Password);
            Assert.Equal("123", WalletItemDataCodec.DecodeBankCard(wallet).Cvv);

            await Task.Delay(250, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(viewModel.SelectedPasswordDetails);

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

            Assert.Null(viewModel.SelectedPasswordDetails);
            Assert.Null(viewModel.SelectedTotpDetails);
            Assert.NotNull(viewModel.SelectedWalletDetails);

            viewModel.SelectedSection = "Totp";
            Assert.NotNull(viewModel.SelectedTotpDetails);
            viewModel.SelectedSection = "Passwords";
            await AssertEventuallyAsync(
                () => viewModel.SelectedPasswordDetails?.Entry.Id == password.Id,
                "Password details were not restored after returning to Password Vault.");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task AssertEventuallyAsync(Func<bool> condition, string failureMessage)
    {
        for (var attempt = 0; attempt < 40 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), failureMessage);
    }
}
