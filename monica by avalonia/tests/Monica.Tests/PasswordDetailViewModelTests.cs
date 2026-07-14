using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class PasswordDetailViewModelTests
{
    [Fact]
    public void Password_detail_clear_sensitive_state_releases_decrypted_presentation_values()
    {
        var crypto = CreateUnlockedCrypto();
        var entry = new PasswordEntry
        {
            Id = 42,
            Title = "Private account",
            Username = "private-user",
            Password = crypto.EncryptString("plain-secret"),
            AuthenticatorKey = "JBSWY3DPEHPK3PXP",
            CreditCardCvv = "123"
        };
        var historyEntry = new PasswordHistoryEntry
        {
            Id = 7,
            EntryId = entry.Id,
            Password = crypto.EncryptString("older-secret")
        };
        var details = new PasswordDetailViewModel(
            new LocalizationService(),
            new CapturingClipboardService(),
            crypto,
            new TotpService(),
            entry,
            [entry],
            category: null,
            boundNote: null,
            attachments: [],
            customFields: [],
            passwordHistory: [new PasswordHistoryDisplayItem(historyEntry, "older-secret", true)]);
        var passwordField = Assert.Single(
            details.Groups.SelectMany(group => group.Fields),
            field => field.IsSensitive && field.CopyValue == "plain-secret");
        var history = Assert.Single(details.PasswordHistory);

        Assert.Equal(details.L.Get("ShowPassword"), passwordField.VisibilityActionLabel);
        Assert.Equal(details.L.Get("ShowPassword"), history.VisibilityActionLabel);
        passwordField.IsVisible = true;
        history.IsVisible = true;
        Assert.Equal(details.L.Get("HidePassword"), passwordField.VisibilityActionLabel);
        Assert.Equal(details.L.Get("HidePassword"), history.VisibilityActionLabel);

        details.ClearSensitiveState();

        Assert.True(details.IsSensitiveStateCleared);
        Assert.Equal(0, details.Entry.Id);
        Assert.Empty(details.Title);
        Assert.Empty(details.Subtitle);
        Assert.Empty(details.Groups);
        Assert.Empty(details.Attachments);
        Assert.Empty(details.PasswordHistory);
        Assert.Empty(passwordField.DisplayValue);
        Assert.Empty(passwordField.CopyValue);
        Assert.False(passwordField.CanCopy);
        Assert.Empty(history.Password);
        Assert.False(history.CanCopy);
        Assert.False(history.IsVisible);
    }

    private static CryptoService CreateUnlockedCrypto()
    {
        var crypto = new CryptoService();
        crypto.InitializeSession("master-password", Enumerable.Repeat((byte)7, 16).ToArray());
        return crypto;
    }

    private sealed class CapturingClipboardService : IClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
