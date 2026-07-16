using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Platform.Services;

namespace Monica.Tests;

public sealed class PasswordDetailViewModelTests
{
    [Fact]
    public void Password_attachment_add_status_is_localized_for_simplified_chinese()
    {
        var localization = new LocalizationService();
        localization.SetLanguage("zh-CN");

        Assert.Equal("正在添加附件...", localization.Get("AddingAttachment"));
        Assert.Equal("无法添加附件。", localization.Get("AttachmentAddFailed"));
        Assert.Equal("已将 recovery.txt 添加到 GitHub", localization.Format("AddedAttachmentFormat", "recovery.txt", "GitHub"));
    }

    [Theory]
    [InlineData("secure_attachments/recovery.enc")]
    [InlineData("mdbx:attachment-1")]
    public void Password_attachment_display_never_exposes_internal_storage_location(string storagePath)
    {
        var item = new PasswordAttachmentItem(
            new LocalizationService(),
            new Attachment
            {
                FileName = "recovery.txt",
                ContentType = "text/plain",
                StoragePath = storagePath,
                SizeBytes = 128
            });

        Assert.Equal("128 B - text/plain", item.DisplayValue);
        Assert.DoesNotContain(storagePath, item.DisplayValue, StringComparison.OrdinalIgnoreCase);
    }

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

    [Fact]
    public async Task Password_detail_cannot_save_attachment_after_sensitive_state_is_cleared()
    {
        var saveCalls = 0;
        var details = new PasswordDetailViewModel(
            new LocalizationService(),
            new CapturingClipboardService(),
            CreateUnlockedCrypto(),
            new TotpService(),
            new PasswordEntry { Id = 42, Title = "Private account" },
            [],
            category: null,
            boundNote: null,
            attachments:
            [
                new Attachment
                {
                    Id = 7,
                    FileName = "recovery.txt",
                    StoragePath = "mdbx:attachment-7"
                }
            ],
            customFields: [],
            saveAttachment: _ =>
            {
                saveCalls++;
                return Task.FromResult(new PasswordAttachmentSaveResult(PasswordAttachmentSaveOutcome.Saved));
            });
        var attachment = Assert.Single(details.Attachments);

        details.ClearSensitiveState();
        await details.SaveAttachmentCommand.ExecuteAsync(attachment);

        Assert.Equal(0, saveCalls);
        Assert.Empty(details.StatusText);
    }

    [Fact]
    public async Task Password_detail_attachment_add_blocks_duplicate_requests_and_inserts_once()
    {
        var addCalls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attachment = new Attachment
        {
            Id = 9,
            OwnerType = "PASSWORD",
            OwnerId = 42,
            FileName = "recovery.txt",
            ContentType = "text/plain",
            StoragePath = "mdbx:attachment-9",
            SizeBytes = 128,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var details = new PasswordDetailViewModel(
            new LocalizationService(),
            new CapturingClipboardService(),
            CreateUnlockedCrypto(),
            new TotpService(),
            new PasswordEntry { Id = 42, Title = "Private account" },
            [],
            category: null,
            boundNote: null,
            attachments: [],
            customFields: [],
            addAttachment: async _ =>
            {
                addCalls++;
                entered.TrySetResult();
                await release.Task;
                return new PasswordAttachmentAddResult(
                    PasswordAttachmentAddOutcome.Added,
                    attachment,
                    "Attachment added");
            });

        var first = details.AddAttachmentCommand.ExecuteAsync(null);
        await entered.Task;
        var second = details.AddAttachmentCommand.ExecuteAsync(null);

        Assert.True(details.IsAddingAttachment);
        Assert.Equal(1, addCalls);
        release.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.False(details.IsAddingAttachment);
        Assert.Equal(1, addCalls);
        Assert.Equal("recovery.txt", Assert.Single(details.Attachments).FileName);
        Assert.Equal("Attachment added", details.StatusText);
    }

    [Fact]
    public async Task Password_detail_pending_attachment_add_does_not_publish_after_sensitive_clear()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var details = new PasswordDetailViewModel(
            new LocalizationService(),
            new CapturingClipboardService(),
            CreateUnlockedCrypto(),
            new TotpService(),
            new PasswordEntry { Id = 42, Title = "Private account" },
            [],
            category: null,
            boundNote: null,
            attachments: [],
            customFields: [],
            addAttachment: async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                return new PasswordAttachmentAddResult(
                    PasswordAttachmentAddOutcome.Added,
                    new Attachment
                    {
                        Id = 10,
                        OwnerType = "PASSWORD",
                        OwnerId = 42,
                        FileName = "late.txt",
                        StoragePath = "mdbx:attachment-10"
                    },
                    "Late attachment added");
            });

        var pending = details.AddAttachmentCommand.ExecuteAsync(null);
        await entered.Task;
        details.ClearSensitiveState();
        release.TrySetResult();
        await pending;

        Assert.True(details.IsSensitiveStateCleared);
        Assert.False(details.IsAddingAttachment);
        Assert.Empty(details.Attachments);
        Assert.Empty(details.StatusText);
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
