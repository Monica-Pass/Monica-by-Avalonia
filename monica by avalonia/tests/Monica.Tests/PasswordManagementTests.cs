using System.Diagnostics;
using System.Collections.Concurrent;
using Monica.App.Services;
using Monica.App.ViewModels;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Data.Services;
using Monica.Platform.Services;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace Monica.Tests;

public sealed class PasswordManagementTests
{
    [Fact]
    public async Task ViewModel_adds_password_from_editor_dialog()
    {
        var harness = CreateHarness();
        var category = new Category { Name = "Work", SortOrder = 1 };
        await harness.Repository.SaveCategoryAsync(category);
        await harness.ViewModel.LoadAsync();
        harness.Crypto.InitializeSession("correct password", new byte[16]);

        harness.Dialog.ConfigureNext(editor =>
        {
            editor.Title = "GitHub";
            editor.WebsiteLines = "github.com\nhttps://github.example";
            editor.Username = "dev@example.com";
            editor.PasswordLines = "plain-secret";
            editor.Notes = "Recovery account";
            editor.AuthenticatorKey = "JBSWY3DPEHPK3PXP";
            editor.AppName = "GitHub";
            editor.AppPackageName = "com.github.android";
            editor.Email = "security@example.com|recovery@example.com";
            editor.Phone = "15551234567";
            editor.AddressLine = "1 Octocat Way";
            editor.City = "San Francisco";
            editor.State = "CA";
            editor.ZipCode = "94107";
            editor.Country = "US";
            editor.CreditCardNumber = "4111111111111111";
            editor.CreditCardHolder = "Monica User";
            editor.CreditCardExpiry = "12/29";
            editor.CreditCardCvv = "123";
            editor.PasskeyBindings = """[{"rpId":"github.com"}]""";
            editor.WifiMetadata = """{"ssid":"Monica"}""";
            editor.SshKeyData = "ssh-ed25519 AAAA";
            editor.SelectedLoginType = editor.LoginTypeOptions.Single(choice => choice.Value == PasswordLoginType.SshKey);
            editor.IsFavorite = true;
            editor.SelectedCategory = editor.CategoryOptions.Single(choice => choice.Id == category.Id);
            editor.SelectedCustomIconType = editor.CustomIconTypeOptions.Single(choice => choice.Value == "SIMPLE_ICON");
            editor.CustomIconValue = "github";
        });

        await harness.ViewModel.AddPasswordCommand.ExecuteAsync(null);

        var saved = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "CREATE" && item.Title == "GitHub");
        Assert.Equal("GitHub", saved.Title);
        Assert.Equal("github.com, https://github.example", saved.Website);
        Assert.Equal("dev@example.com", saved.Username);
        Assert.Equal("Recovery account", saved.Notes);
        Assert.Equal("JBSWY3DPEHPK3PXP", saved.AuthenticatorKey);
        Assert.Equal("GitHub", saved.AppName);
        Assert.Equal("com.github.android", saved.AppPackageName);
        Assert.Equal("security@example.com|recovery@example.com", saved.Email);
        Assert.Equal("15551234567", saved.Phone);
        Assert.Equal("1 Octocat Way", saved.AddressLine);
        Assert.Equal("San Francisco", saved.City);
        Assert.Equal("CA", saved.State);
        Assert.Equal("94107", saved.ZipCode);
        Assert.Equal("US", saved.Country);
        Assert.Equal("4111111111111111", saved.CreditCardNumber);
        Assert.Equal("Monica User", saved.CreditCardHolder);
        Assert.Equal("12/29", saved.CreditCardExpiry);
        Assert.Equal("123", saved.CreditCardCvv);
        Assert.Equal("""[{"rpId":"github.com"}]""", saved.PasskeyBindings);
        Assert.Equal("""{"ssid":"Monica"}""", saved.WifiMetadata);
        Assert.Equal("ssh-ed25519 AAAA", saved.SshKeyData);
        Assert.Equal(PasswordLoginType.SshKey, saved.LoginType);
        Assert.Equal("SIMPLE_ICON", saved.CustomIconType);
        Assert.Equal("github", saved.CustomIconValue);
        Assert.True(saved.CustomIconUpdatedAt > 0);
        Assert.Equal("GI", saved.AvatarText);
        Assert.Equal(category.Id, saved.CategoryId);
        Assert.True(saved.IsFavorite);
        Assert.NotEqual("plain-secret", saved.Password);
        Assert.Equal("plain-secret", harness.Crypto.DecryptString(saved.Password));
        Assert.Equal("GitHub", Assert.Single(harness.ViewModel.Passwords).Title);
    }

    [Fact]
    public async Task ViewModel_saves_bound_note_and_custom_fields()
    {
        var harness = CreateHarness();
        var payload = NoteContentCodec.BuildSavePayload("Recovery note", "codes", "", true);
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = payload.Title,
            Notes = payload.NotesCache,
            ItemData = payload.ItemData,
            ImagePaths = payload.ImagePaths
        };
        await harness.Repository.SaveSecureItemAsync(note);
        await harness.ViewModel.LoadAsync();

        harness.Dialog.ConfigureNext(editor =>
        {
            Assert.Contains(editor.BoundNoteOptions, option => option.Id == note.Id && option.Title == "Recovery note");
            editor.Title = "With extras";
            editor.Username = "extra-user";
            editor.PasswordLines = "extra-secret";
            editor.SelectedBoundNote = editor.BoundNoteOptions.Single(option => option.Id == note.Id);
            editor.CustomFieldsText = "Security question=First school\n!Backup code=123456";
        });

        await harness.ViewModel.AddPasswordCommand.ExecuteAsync(null);

        var saved = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Equal(note.Id, saved.BoundNoteId);
        var fields = await harness.Repository.GetCustomFieldsAsync(saved.Id);
        Assert.Equal(2, fields.Count);
        Assert.Equal("Security question", fields[0].Title);
        Assert.Equal("First school", fields[0].Value);
        Assert.False(fields[0].IsProtected);
        Assert.Equal("Backup code", fields[1].Title);
        Assert.Equal("123456", fields[1].Value);
        Assert.True(fields[1].IsProtected);
        Assert.Equal([saved.Id], await harness.Repository.SearchEntryIdsByCustomFieldContentAsync("school"));
    }

    [Fact]
    public void Password_editor_generates_appends_reveals_and_reports_strength()
    {
        var localization = new LocalizationService();
        var editor = new PasswordEditorViewModel(
            localization,
            new PasswordGeneratorService(),
            null,
            [],
            "");

        editor.GeneratorLength = 18;
        editor.GeneratorIncludeUppercase = false;
        editor.GeneratorIncludeLowercase = true;
        editor.GeneratorIncludeNumbers = true;
        editor.GeneratorIncludeSymbols = false;

        editor.GeneratePasswordCommand.Execute(null);

        var first = Assert.Single(editor.GetPasswordRows());
        Assert.Equal(18, first.Length);
        Assert.DoesNotContain(first, char.IsUpper);
        Assert.DoesNotContain(first, c => !char.IsLetterOrDigit(c));
        Assert.Contains(first, char.IsDigit);
        Assert.NotEqual(0, editor.PasswordMaskChar);
        Assert.Contains("1", editor.PasswordRowCountText);
        Assert.Contains("/5", editor.PasswordEditorStrengthText);
        Assert.True(editor.PasswordEditorStrengthValue > 0);

        editor.AddGeneratedPasswordRowCommand.Execute(null);

        Assert.Equal(2, editor.GetPasswordRows().Count);
        editor.TogglePasswordVisibilityCommand.Execute(null);
        Assert.True(editor.IsPasswordVisible);
        Assert.Equal('\0', editor.PasswordMaskChar);
        Assert.Equal(localization.Get("HidePassword"), editor.TogglePasswordVisibilityLabel);
    }

    [Fact]
    public void Password_editor_preserves_and_clears_custom_icon_metadata()
    {
        var localization = new LocalizationService();
        var source = new PasswordEntry
        {
            Title = "GitHub",
            Password = "stored",
            CustomIconType = "UPLOADED",
            CustomIconValue = "icons/github.png",
            CustomIconUpdatedAt = 123
        };
        var editor = new PasswordEditorViewModel(
            localization,
            new PasswordGeneratorService(),
            source,
            [],
            "plain-secret");

        Assert.Equal("UPLOADED", editor.SelectedCustomIconType?.Value);
        Assert.Equal("icons/github.png", editor.CustomIconValue);
        Assert.True(editor.IsCustomIconValueEnabled);

        var unchanged = editor.BuildEntry("stored");
        Assert.Equal("UPLOADED", unchanged.CustomIconType);
        Assert.Equal("icons/github.png", unchanged.CustomIconValue);
        Assert.Equal(123, unchanged.CustomIconUpdatedAt);

        editor.SelectedCustomIconType = editor.CustomIconTypeOptions.Single(choice => choice.Value == "SIMPLE_ICON");
        editor.CustomIconValue = "github";
        var changed = editor.BuildEntry("stored");
        Assert.Equal("SIMPLE_ICON", changed.CustomIconType);
        Assert.Equal("github", changed.CustomIconValue);
        Assert.True(changed.CustomIconUpdatedAt > 123);

        editor.SelectedCustomIconType = editor.CustomIconTypeOptions.Single(choice => choice.Value == "NONE");
        var cleared = editor.BuildEntry("stored");
        Assert.Equal("NONE", cleared.CustomIconType);
        Assert.Null(cleared.CustomIconValue);
        Assert.False(editor.IsCustomIconValueEnabled);
    }

    [Fact]
    public async Task ViewModel_saves_password_authenticator_as_bound_totp_and_searches_rich_fields()
    {
        var harness = CreateHarness();
        await harness.ViewModel.LoadAsync();

        harness.Dialog.ConfigureNext(editor =>
        {
            editor.Title = "GitHub";
            editor.Username = "dev@example.com";
            editor.PasswordLines = "secret";
            editor.Notes = "recovery words live elsewhere";
            editor.AuthenticatorKey = "otpauth://totp/GitHub:dev%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&period=45&digits=8";
            editor.AppName = "GitHub Desktop";
            editor.Email = "security@example.com";
            editor.PasskeyBindings = """[{"rpId":"github.com"}]""";
            editor.CustomFieldsText = "Recovery hint=blue";
        });

        await harness.ViewModel.AddPasswordCommand.ExecuteAsync(null);

        var saved = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.True(saved.HasAuthenticator);
        var displayed = Assert.Single(harness.ViewModel.Passwords);
        Assert.Matches("^[0-9]{8}$", displayed.TotpCode);
        var boundTotp = Assert.Single(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(saved.Id));
        Assert.Equal(saved.Id, boundTotp.BoundPasswordId);
        Assert.Equal("GitHub", boundTotp.Title);
        Assert.Contains("JBSWY3DPEHPK3PXP", boundTotp.ItemData, StringComparison.Ordinal);
        Assert.Single(harness.ViewModel.TotpItems, item => item.BoundPasswordId == saved.Id);

        harness.ViewModel.SearchText = "blue";
        Assert.Equal([saved.Id], harness.ViewModel.FilteredPasswords.Select(item => item.Id).ToArray());
        harness.ViewModel.SearchText = "GitHub Desktop";
        Assert.Equal([saved.Id], harness.ViewModel.FilteredPasswords.Select(item => item.Id).ToArray());
        harness.ViewModel.SearchText = "github.com";
        Assert.Equal([saved.Id], harness.ViewModel.FilteredPasswords.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task ViewModel_adds_grouped_passwords_from_multiple_password_lines()
    {
        var harness = CreateHarness();
        await harness.ViewModel.LoadAsync();
        harness.Crypto.InitializeSession("correct password", new byte[16]);

        harness.Dialog.ConfigureNext(editor =>
        {
            editor.Title = "Grouped";
            editor.WebsiteLines = "example.com\nhttps://example.org\nexample.com";
            editor.Username = "group-user";
            editor.PasswordLines = "first-secret\nsecond-secret";
        });

        await harness.ViewModel.AddPasswordCommand.ExecuteAsync(null);

        var saved = (await harness.Repository.GetPasswordsAsync()).OrderBy(item => item.Id).ToArray();
        Assert.Equal(2, saved.Length);
        Assert.All(saved, item =>
        {
            Assert.Equal("Grouped", item.Title);
            Assert.Equal("example.com, https://example.org", item.Website);
            Assert.Equal("group-user", item.Username);
        });
        Assert.Equal("first-secret", harness.Crypto.DecryptString(saved[0].Password));
        Assert.Equal("second-secret", harness.Crypto.DecryptString(saved[1].Password));
        Assert.NotNull(saved[0].ReplicaGroupId);
        Assert.Equal(saved[0].ReplicaGroupId, saved[1].ReplicaGroupId);
        Assert.Equal(2, harness.ViewModel.Passwords.Count);
    }

    [Fact]
    public async Task ViewModel_edits_existing_password_and_preserves_id()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("correct password", new byte[16]);
        var category = new Category { Name = "Personal" };
        await harness.Repository.SaveCategoryAsync(category);
        var existingFields = new[]
        {
            new CustomField { Title = "Old field", Value = "old", IsProtected = false }
        };
        const string replicaGroupId = "test-edit-group";
        var entry = new PasswordEntry
        {
            Title = "Old",
            Website = "https://old.example",
            Username = "old-user",
            Password = harness.Crypto.EncryptString("old-secret"),
            Notes = "old notes",
            ReplicaGroupId = replicaGroupId
        };
        await harness.Repository.SavePasswordAsync(entry);
        await harness.Repository.ReplaceCustomFieldsAsync(entry.Id, existingFields);
        var sibling = new PasswordEntry
        {
            Title = "Old",
            Website = "https://old.example",
            Username = "old-user",
            Password = harness.Crypto.EncryptString("sibling-secret"),
            ReplicaGroupId = replicaGroupId
        };
        await harness.Repository.SavePasswordAsync(sibling);
        var removedSibling = new PasswordEntry
        {
            Title = "Old",
            Website = "https://old.example",
            Username = "old-user",
            Password = harness.Crypto.EncryptString("remove-me"),
            ReplicaGroupId = replicaGroupId
        };
        await harness.Repository.SavePasswordAsync(removedSibling);
        await harness.ViewModel.LoadAsync();

        harness.Dialog.ConfigureNext(editor =>
        {
            Assert.Equal(["old-secret", "sibling-secret", "remove-me"], SplitRows(editor.PasswordLines));
            Assert.Equal("Old field=old", editor.CustomFieldsText);
            editor.Title = "Updated";
            editor.WebsiteLines = "https://updated.example";
            editor.Username = "new-user";
            editor.PasswordLines = "new-secret\nsecond-new-secret";
            editor.Notes = "new notes";
            editor.SsoProvider = "GITHUB";
            editor.PasskeyBindings = """[{"credentialId":"abc"}]""";
            editor.WifiMetadata = """{"ssid":"Updated"}""";
            editor.CustomFieldsText = "New field=new";
            editor.SelectedLoginType = editor.LoginTypeOptions.Single(choice => choice.Value == PasswordLoginType.Sso);
            editor.SelectedCategory = editor.CategoryOptions.Single(choice => choice.Id == category.Id);
        });

        await harness.ViewModel.EditPasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == entry.Id));

        var saved = (await harness.Repository.GetPasswordsAsync()).OrderBy(item => item.Id).ToArray();
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "UPDATE" && item.Title == "Updated");
        Assert.Equal(2, saved.Length);
        Assert.Equal([entry.Id, sibling.Id], saved.Select(item => item.Id).ToArray());
        Assert.All(saved, item =>
        {
            Assert.Equal("Updated", item.Title);
            Assert.Equal("https://updated.example", item.Website);
            Assert.Equal("new-user", item.Username);
            Assert.Equal("new notes", item.Notes);
            Assert.Equal("GITHUB", item.SsoProvider);
            Assert.Equal("""[{"credentialId":"abc"}]""", item.PasskeyBindings);
            Assert.Equal("""{"ssid":"Updated"}""", item.WifiMetadata);
            Assert.Equal(PasswordLoginType.Sso, item.LoginType);
            Assert.Equal(category.Id, item.CategoryId);
        });
        Assert.Equal("new-secret", harness.Crypto.DecryptString(saved[0].Password));
        Assert.Equal("second-new-secret", harness.Crypto.DecryptString(saved[1].Password));
        Assert.Equal(2, harness.ViewModel.Passwords.Count);
        var updatedFields = await harness.Repository.GetCustomFieldsAsync(entry.Id);
        var updatedField = Assert.Single(updatedFields);
        Assert.Equal("New field", updatedField.Title);
        Assert.Equal("new", updatedField.Value);
        var deleted = (await harness.Repository.GetPasswordsAsync(includeDeleted: true)).Single(item => item.Id == removedSibling.Id);
        Assert.True(deleted.IsDeleted);

        var firstHistory = Assert.Single(await harness.Repository.GetPasswordHistoryAsync(entry.Id));
        var secondHistory = Assert.Single(await harness.Repository.GetPasswordHistoryAsync(sibling.Id));
        Assert.Equal("old-secret", harness.Crypto.DecryptString(firstHistory.Password));
        Assert.Equal("sibling-secret", harness.Crypto.DecryptString(secondHistory.Password));
        Assert.Empty(await harness.Repository.GetPasswordHistoryAsync(removedSibling.Id));
    }

    [Fact]
    public async Task ViewModel_keeps_password_history_deduplicated_and_limited()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("correct password", new byte[16]);
        var entry = new PasswordEntry
        {
            Title = "History",
            Username = "history-user",
            Password = harness.Crypto.EncryptString("secret-00")
        };
        await harness.Repository.SavePasswordAsync(entry);
        await harness.ViewModel.LoadAsync();

        for (var index = 1; index <= 12; index++)
        {
            var next = $"secret-{index:00}";
            harness.Dialog.ConfigureNext(editor =>
            {
                editor.Title = "History";
                editor.Username = "history-user";
                editor.PasswordLines = next;
            });

            await harness.ViewModel.EditPasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.Single());
        }

        harness.Dialog.ConfigureNext(editor =>
        {
            editor.Title = "History";
            editor.Username = "history-user";
            editor.PasswordLines = "secret-12";
        });

        await harness.ViewModel.EditPasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.Single());

        var history = await harness.Repository.GetPasswordHistoryAsync(entry.Id);
        var plainHistory = history.Select(item => harness.Crypto.DecryptString(item.Password)).ToArray();

        Assert.Equal(10, history.Count);
        Assert.Equal("secret-11", plainHistory[0]);
        Assert.Equal("secret-02", plainHistory[^1]);
        Assert.DoesNotContain("secret-12", plainHistory);
        Assert.Equal(history.Select(item => item.Id).Distinct().Count(), history.Count);
    }

    [Fact]
    public async Task ViewModel_edit_updates_existing_bound_totp_and_removes_it_when_authenticator_is_cleared()
    {
        var harness = CreateHarness();
        var entry = new PasswordEntry
        {
            Title = "Old",
            Username = "dev",
            Password = "secret",
            AuthenticatorKey = "JBSWY3DPEHPK3PXP"
        };
        await harness.Repository.SavePasswordAsync(entry);
        var duplicateTotp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Duplicate",
            BoundPasswordId = entry.Id,
            ItemData = TotpDataResolver.ToItemData(TotpDataResolver.FromAuthenticatorKey("JBSWY3DPEHPK3PXP")!)
        };
        await harness.Repository.SaveSecureItemAsync(duplicateTotp);
        await harness.ViewModel.LoadAsync();

        harness.Dialog.ConfigureNext(editor =>
        {
            editor.Title = "Updated";
            editor.Username = "dev";
            editor.PasswordLines = "secret";
            editor.AuthenticatorKey = "otpauth://totp/Updated:dev?secret=JBSWY3DPEHPK3PXP&issuer=Updated&period=60";
        });

        await harness.ViewModel.EditPasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.Single());

        var updatedTotp = Assert.Single(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id));
        Assert.Equal(duplicateTotp.Id, updatedTotp.Id);
        Assert.Equal("Updated", updatedTotp.Title);
        Assert.Contains(@"""period"":60", updatedTotp.ItemData, StringComparison.Ordinal);

        harness.Dialog.ConfigureNext(editor =>
        {
            editor.Title = "No TOTP";
            editor.Username = "dev";
            editor.PasswordLines = "secret";
            editor.AuthenticatorKey = "";
        });

        await harness.ViewModel.EditPasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.Single());

        Assert.Empty(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id));
        var deleted = Assert.Single(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(entry.Id, includeDeleted: true));
        Assert.True(deleted.IsDeleted);
    }

    [Fact]
    public async Task ViewModel_delete_password_moves_entire_password_group_to_recycle_bin()
    {
        var harness = CreateHarness();
        const string replicaGroupId = "test-delete-group";
        var first = new PasswordEntry { Title = "Grouped", Website = "example.com", Username = "dev", Password = "one", ReplicaGroupId = replicaGroupId };
        var second = new PasswordEntry { Title = "Grouped", Website = "example.com", Username = "dev", Password = "two", ReplicaGroupId = replicaGroupId };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.DeletePasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));

        Assert.Empty(await harness.Repository.GetPasswordsAsync());
        Assert.All(await harness.Repository.GetPasswordsAsync(includeDeleted: true), item => Assert.True(item.IsDeleted));
        Assert.Empty(harness.ViewModel.Passwords);
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "DELETE" && item.Title == "Grouped");
    }

    [Fact]
    public async Task ViewModel_similar_passwords_without_replica_group_stay_independent()
    {
        var harness = CreateHarness();
        var first = new PasswordEntry { Title = "GitHub", Website = "github.com", Username = "dev", Password = "one" };
        var second = new PasswordEntry { Title = "GitHub", Website = "github.com", Username = "dev", Password = "two" };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));

        Assert.Single(harness.DetailDialog.LastSiblings);

        await harness.ViewModel.DeletePasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));

        Assert.Single(harness.ViewModel.Passwords, item => item.Id == second.Id);
        Assert.Single(harness.ViewModel.DeletedPasswords, item => item.Id == first.Id);
    }

    [Fact]
    public async Task ViewModel_restores_and_permanently_deletes_password_group_from_recycle_bin()
    {
        var harness = CreateHarness();
        const string replicaGroupId = "test-recycle-group";
        var first = new PasswordEntry
        {
            Title = "Recoverable",
            Website = "example.com",
            Username = "dev",
            Password = "one",
            AuthenticatorKey = "JBSWY3DPEHPK3PXP",
            ReplicaGroupId = replicaGroupId
        };
        var second = new PasswordEntry
        {
            Title = "Recoverable",
            Website = "example.com",
            Username = "dev",
            Password = "two",
            ReplicaGroupId = replicaGroupId
        };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.Repository.ReplaceCustomFieldsAsync(first.Id, [new CustomField { Title = "Question", Value = "Answer" }]);
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Recoverable",
            BoundPasswordId = first.Id,
            ItemData = TotpDataResolver.ToItemData(TotpDataResolver.FromAuthenticatorKey(first.AuthenticatorKey)!)
        };
        await harness.Repository.SaveSecureItemAsync(totp);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.DeletePasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));

        Assert.Empty(harness.ViewModel.Passwords);
        Assert.Equal(2, harness.ViewModel.DeletedPasswords.Count);
        Assert.DoesNotContain(harness.ViewModel.TotpItems, item => item.BoundPasswordId == first.Id);
        Assert.Empty(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(first.Id));

        await harness.ViewModel.RestorePasswordCommand.ExecuteAsync(harness.ViewModel.DeletedPasswords.First(item => item.Id == first.Id));

        Assert.Equal(2, harness.ViewModel.Passwords.Count);
        Assert.Empty(harness.ViewModel.DeletedPasswords);
        Assert.Single(harness.ViewModel.TotpItems, item => item.BoundPasswordId == first.Id);
        Assert.Single(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(first.Id));
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "RESTORE" && item.Title == "Recoverable");

        await harness.ViewModel.DeletePasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));
        await harness.ViewModel.DeletePasswordPermanentlyCommand.ExecuteAsync(harness.ViewModel.DeletedPasswords.First(item => item.Id == first.Id));

        Assert.Empty(harness.ViewModel.DeletedPasswords);
        Assert.Empty(await harness.Repository.GetPasswordsAsync(includeDeleted: true));
        Assert.Empty(await harness.Repository.GetCustomFieldsAsync(first.Id));
        Assert.Empty(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(first.Id, includeDeleted: true));
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "PURGE" && item.Title == "Recoverable");
    }

    [Fact]
    public async Task Dangerous_delete_commands_do_not_mutate_when_confirmation_is_cancelled()
    {
        var confirmation = new FakeConfirmationDialogService(result: false);
        var webDav = new FakeWebDavBackupService();
        var harness = CreateHarness(webDavBackupService: webDav, confirmationDialogService: confirmation);
        harness.Crypto.InitializeSession("correct password", new byte[16]);

        var deletedPassword = new PasswordEntry { Title = "Deleted login", Password = harness.Crypto.EncryptString("deleted-secret") };
        var attachmentPassword = new PasswordEntry { Title = "Attachment login", Password = harness.Crypto.EncryptString("attachment-secret") };
        await harness.Repository.SavePasswordAsync(deletedPassword);
        await harness.Repository.SavePasswordAsync(attachmentPassword);
        await harness.Repository.SoftDeletePasswordAsync(deletedPassword.Id);
        await harness.Repository.SaveAttachmentAsync(new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = attachmentPassword.Id,
            FileName = "cancelled.txt",
            StoragePath = "secure_attachments/cancelled.enc",
            SizeBytes = 64
        });
        await harness.Repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = attachmentPassword.Id,
            Password = harness.Crypto.EncryptString("old-secret"),
            LastUsedAt = DateTimeOffset.UtcNow
        });

        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Cancel note",
            ItemData = NoteContentCodec.BuildSavePayload("Cancel note", "body", "", true).ItemData
        };
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Cancel totp",
            ItemData = TotpDataResolver.ToItemData(TotpDataResolver.FromAuthenticatorKey("JBSWY3DPEHPK3PXP")!)
        };
        var wallet = new SecureItem
        {
            ItemType = VaultItemType.BankCard,
            Title = "Cancel card",
            ItemData = WalletItemDataCodec.EncodeBankCard(new BankCardWalletData
            {
                CardNumber = "4111111111111111",
                CardholderName = "Monica"
            })
        };
        await harness.Repository.SaveSecureItemAsync(note);
        await harness.Repository.SaveSecureItemAsync(totp);
        await harness.Repository.SaveSecureItemAsync(wallet);

        await harness.ViewModel.LoadAsync();
        await webDav.UploadTextAsync(new WebDavProfile { RootPath = "/Monica" }, "cancelled.monica.json", "{}");
        harness.ViewModel.WebDavEnabled = true;
        harness.ViewModel.WebDavServerUrl = "https://dav.example.com/";
        harness.ViewModel.WebDavRemotePath = "/Monica";
        await harness.ViewModel.LoadWebDavBackupsCommand.ExecuteAsync(null);

        await harness.ViewModel.DeletePasswordPermanentlyCommand.ExecuteAsync(harness.ViewModel.DeletedPasswords.Single(item => item.Id == deletedPassword.Id));
        await harness.ViewModel.DeleteNoteCommand.ExecuteAsync(harness.ViewModel.NoteItems.Single(item => item.Id == note.Id));
        await harness.ViewModel.DeleteTotpCommand.ExecuteAsync(harness.ViewModel.TotpItems.Single(item => item.Id == totp.Id));
        await harness.ViewModel.DeleteWalletItemCommand.ExecuteAsync(harness.ViewModel.WalletItems.Single(item => item.Id == wallet.Id));
        await harness.ViewModel.DeleteWebDavBackupCommand.ExecuteAsync(harness.ViewModel.WebDavBackupHistory.Single());
        await harness.ViewModel.EmptyRecycleBinCommand.ExecuteAsync(null);

        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(harness.ViewModel.Passwords.Single(item => item.Id == attachmentPassword.Id));
        var details = Assert.IsType<PasswordDetailViewModel>(harness.DetailDialog.LastDetails);
        var attachment = Assert.Single(details.Attachments);
        var history = Assert.Single(details.PasswordHistory);
        await details.DeleteAttachmentCommand.ExecuteAsync(attachment);
        await details.DeleteHistoryPasswordCommand.ExecuteAsync(history);
        await details.ClearPasswordHistoryCommand.ExecuteAsync(null);

        Assert.Single(harness.ViewModel.DeletedPasswords, item => item.Id == deletedPassword.Id);
        Assert.Single(await harness.Repository.GetPasswordsAsync(includeDeleted: true), item => item.Id == deletedPassword.Id && item.IsDeleted);
        Assert.Single(await harness.Repository.GetSecureItemsAsync(VaultItemType.Note), item => item.Id == note.Id);
        Assert.Single(await harness.Repository.GetSecureItemsAsync(VaultItemType.Totp), item => item.Id == totp.Id);
        Assert.Single(await harness.Repository.GetSecureItemsAsync(VaultItemType.BankCard), item => item.Id == wallet.Id);
        Assert.Single(harness.ViewModel.WebDavBackupHistory);
        Assert.Empty(webDav.DeletedPaths);
        Assert.Single(await harness.Repository.GetAttachmentsAsync("PASSWORD", attachmentPassword.Id));
        Assert.Single(await harness.Repository.GetPasswordHistoryAsync(attachmentPassword.Id));
        Assert.Single(details.Attachments);
        Assert.Single(details.PasswordHistory);
        Assert.Equal(9, confirmation.Requests.Count);
        Assert.Equal(3, confirmation.TypedRequests.Count);
        Assert.Contains(confirmation.TypedRequests, request =>
            request.Title == harness.ViewModel.L.Get("DeletePermanentlyConfirmationTitle") &&
            request.RequiredPhrase == harness.ViewModel.L.Get("PermanentDeleteConfirmationPhrase"));
        Assert.Contains(confirmation.TypedRequests, request =>
            request.Title == harness.ViewModel.L.Get("DeleteWebDavBackupConfirmationTitle") &&
            request.RequiredPhrase == harness.ViewModel.L.Get("DeleteWebDavBackupConfirmationPhrase"));
        Assert.Contains(confirmation.TypedRequests, request =>
            request.Title == harness.ViewModel.L.Get("EmptyRecycleBinConfirmationTitle") &&
            request.RequiredPhrase == harness.ViewModel.L.Get("EmptyRecycleBinConfirmationPhrase"));
    }

    [Fact]
    public async Task Empty_recycle_bin_requires_typed_confirmation_and_purges_deleted_passwords()
    {
        var confirmation = new FakeConfirmationDialogService();
        var harness = CreateHarness(confirmationDialogService: confirmation);
        harness.Crypto.InitializeSession("correct password", new byte[16]);

        var first = new PasswordEntry { Title = "Deleted one", Password = harness.Crypto.EncryptString("one") };
        var second = new PasswordEntry { Title = "Deleted two", Password = harness.Crypto.EncryptString("two") };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.Repository.SoftDeletePasswordAsync(first.Id);
        await harness.Repository.SoftDeletePasswordAsync(second.Id);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.EmptyRecycleBinCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.DeletedPasswords);
        Assert.Empty(await harness.Repository.GetPasswordsAsync(includeDeleted: true));
        Assert.Equal(
            harness.ViewModel.L.Format("EmptiedRecycleBinFormat", 2),
            harness.ViewModel.StatusMessage);
        Assert.Contains(confirmation.TypedRequests, request =>
            request.Title == harness.ViewModel.L.Get("EmptyRecycleBinConfirmationTitle") &&
            request.RequiredPhrase == harness.ViewModel.L.Get("EmptyRecycleBinConfirmationPhrase"));
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "PURGE" && item.Title == first.Title);
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "PURGE" && item.Title == second.Title);
    }

    [Fact]
    public void ViewModel_recoverable_status_requires_unlocked_failed_nonloading_state()
    {
        var harness = CreateHarness();

        harness.ViewModel.StatusMessage = "Vault load failed";

        Assert.False(harness.ViewModel.HasRecoverableStatusMessage);

        harness.ViewModel.IsUnlocked = true;
        harness.ViewModel.IsLoadingVault = true;

        Assert.False(harness.ViewModel.HasRecoverableStatusMessage);

        harness.ViewModel.IsLoadingVault = false;

        Assert.True(harness.ViewModel.HasRecoverableStatusMessage);

        harness.ViewModel.StatusMessage = "Vault unlocked";

        Assert.False(harness.ViewModel.HasRecoverableStatusMessage);
    }

    [Fact]
    public async Task ViewModel_archives_unarchives_and_deletes_password_group()
    {
        var harness = CreateHarness();
        const string replicaGroupId = "test-archive-group";
        var first = new PasswordEntry
        {
            Title = "Archive me",
            Website = "archive.example",
            Username = "dev",
            Password = "one",
            AuthenticatorKey = "JBSWY3DPEHPK3PXP",
            ReplicaGroupId = replicaGroupId
        };
        var second = new PasswordEntry
        {
            Title = "Archive me",
            Website = "archive.example",
            Username = "dev",
            Password = "two",
            ReplicaGroupId = replicaGroupId
        };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Archive me",
            BoundPasswordId = first.Id,
            ItemData = TotpDataResolver.ToItemData(TotpDataResolver.FromAuthenticatorKey(first.AuthenticatorKey)!)
        };
        await harness.Repository.SaveSecureItemAsync(totp);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ArchivePasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));

        Assert.Empty(harness.ViewModel.Passwords);
        Assert.Equal(2, harness.ViewModel.ArchivedPasswords.Count);
        Assert.Empty(await harness.Repository.GetPasswordsAsync());
        Assert.All(await harness.Repository.GetPasswordsAsync(includeArchived: true), item =>
        {
            Assert.True(item.IsArchived);
            Assert.NotNull(item.ArchivedAt);
        });
        Assert.Empty(harness.ViewModel.TotpItems);
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "ARCHIVE" && item.Title == "Archive me");

        harness.ViewModel.SearchText = "archive.example";
        Assert.Equal(2, harness.ViewModel.FilteredArchivedPasswords.Count());
        harness.ViewModel.SearchText = "missing";
        Assert.Empty(harness.ViewModel.FilteredArchivedPasswords);
        harness.ViewModel.SearchText = "";

        await harness.ViewModel.UnarchivePasswordCommand.ExecuteAsync(harness.ViewModel.ArchivedPasswords.First(item => item.Id == first.Id));

        Assert.Equal(2, harness.ViewModel.Passwords.Count);
        Assert.Empty(harness.ViewModel.ArchivedPasswords);
        Assert.All(await harness.Repository.GetPasswordsAsync(), item =>
        {
            Assert.False(item.IsArchived);
            Assert.Null(item.ArchivedAt);
        });
        Assert.Single(harness.ViewModel.TotpItems, item => item.BoundPasswordId == first.Id);
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "UNARCHIVE" && item.Title == "Archive me");

        await harness.ViewModel.ArchivePasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));
        await harness.ViewModel.DeletePasswordCommand.ExecuteAsync(harness.ViewModel.ArchivedPasswords.First(item => item.Id == first.Id));

        Assert.Empty(harness.ViewModel.ArchivedPasswords);
        Assert.Equal(2, harness.ViewModel.DeletedPasswords.Count);
        Assert.All(await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true), item =>
        {
            Assert.True(item.IsDeleted);
            Assert.False(item.IsArchived);
        });
    }

    [Fact]
    public async Task ViewModel_refreshes_archive_and_recycle_items_source_after_load()
    {
        var harness = CreateHarness();
        await harness.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Archived on load",
            Website = "archive-load.example",
            Username = "archived-user",
            Password = "one",
            IsArchived = true,
            ArchivedAt = DateTimeOffset.UtcNow
        });
        await harness.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Deleted on load",
            Website = "deleted-load.example",
            Username = "deleted-user",
            Password = "two",
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow
        });
        var changed = new HashSet<string>();
        harness.ViewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                changed.Add(e.PropertyName);
            }
        };

        await harness.ViewModel.LoadAsync();

        Assert.Single(harness.ViewModel.FilteredArchivedPasswords);
        Assert.Single(harness.ViewModel.FilteredDeletedPasswords);
        Assert.Contains(nameof(MainWindowViewModel.FilteredArchivedPasswords), changed);
        Assert.Contains(nameof(MainWindowViewModel.FilteredDeletedPasswords), changed);
        Assert.Contains(nameof(MainWindowViewModel.HasFilteredArchivedPasswords), changed);
        Assert.Contains(nameof(MainWindowViewModel.HasFilteredDeletedPasswords), changed);
    }

    [Fact]
    public async Task ViewModel_archives_selected_passwords()
    {
        var harness = CreateHarness();
        var first = new PasswordEntry { Title = "First", Website = "one.example", Username = "one-user", Password = "one" };
        var second = new PasswordEntry { Title = "Second", Website = "two.example", Username = "two-user", Password = "two" };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.ViewModel.LoadAsync();

        foreach (var item in harness.ViewModel.Passwords)
        {
            item.IsSelected = true;
        }

        await harness.ViewModel.ArchiveSelectedPasswordsCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.Passwords);
        Assert.Equal(2, harness.ViewModel.ArchivedPasswords.Count);
        Assert.Empty(await harness.Repository.GetPasswordsAsync());
        Assert.Equal(harness.ViewModel.L.Format("ArchivedSelectedPasswordsFormat", 2), harness.ViewModel.StatusMessage);
        Assert.False(harness.ViewModel.HasSelectedPasswords);
    }

    [Fact]
    public async Task ViewModel_shows_password_details_and_copies_individual_fields()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("correct password", new byte[16]);
        var category = new Category { Name = "Engineering", SortOrder = 1 };
        const string replicaGroupId = "test-detail-group";
        await harness.Repository.SaveCategoryAsync(category);
        var notePayload = NoteContentCodec.BuildSavePayload("Recovery", "backup codes stored here", "ops", true);
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = notePayload.Title,
            Notes = notePayload.NotesCache,
            ItemData = notePayload.ItemData,
            ImagePaths = notePayload.ImagePaths
        };
        await harness.Repository.SaveSecureItemAsync(note);
        var first = new PasswordEntry
        {
            Title = "GitHub",
            Website = "github.com",
            Username = "dev@example.com",
            Password = harness.Crypto.EncryptString("primary-secret"),
            Notes = "main account",
            CategoryId = category.Id,
            BoundNoteId = note.Id,
            AuthenticatorKey = "otpauth://totp/GitHub:dev%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&period=45&digits=8",
            AppName = "GitHub Desktop",
            PasskeyBindings = """[{"rpId":"github.com"}]""",
            CustomIconType = "SIMPLE_ICON",
            CustomIconValue = "github",
            CustomIconUpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ReplicaGroupId = replicaGroupId
        };
        await harness.Repository.SavePasswordAsync(first);
        var second = new PasswordEntry
        {
            Title = "GitHub",
            Website = "github.com",
            Username = "dev@example.com",
            Password = harness.Crypto.EncryptString("backup-secret"),
            CategoryId = category.Id,
            BoundNoteId = note.Id,
            ReplicaGroupId = replicaGroupId
        };
        await harness.Repository.SavePasswordAsync(second);
        await harness.Repository.ReplaceCustomFieldsAsync(first.Id, [
            new CustomField { Title = "Recovery hint", Value = "blue", SortOrder = 0 },
            new CustomField { Title = "Backup code", Value = "654321", IsProtected = true, SortOrder = 1 }
        ]);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));

        var details = Assert.IsType<PasswordDetailViewModel>(harness.DetailDialog.LastDetails);
        Assert.Equal(2, harness.DetailDialog.LastSiblings.Count);
        Assert.Equal("Engineering", harness.DetailDialog.LastCategory?.Name);
        Assert.Equal(note.Id, harness.DetailDialog.LastBoundNote?.Id);
        Assert.Equal(2, harness.DetailDialog.LastCustomFields.Count);
        Assert.Equal("GI", details.Initial);

        var fields = details.Groups.SelectMany(group => group.Fields).ToArray();
        Assert.Contains(fields, field => field.Label == details.L.Username && field.DisplayValue == "dev@example.com");
        Assert.Contains(fields, field => field.Label == $"{details.L.Password} 1" && field.DisplayValue == "primary-secret");
        Assert.Contains(fields, field => field.Label == $"{details.L.Password} 2" && field.DisplayValue == "backup-secret");
        Assert.Contains(fields, field => field.Label == details.L.Category && field.DisplayValue == "Engineering");
        Assert.Contains(fields, field => field.Label == details.L.BoundNote && field.DisplayValue.Contains("backup codes", StringComparison.Ordinal));
        Assert.Contains(fields, field => field.Label == details.L.TotpCode && field.DisplayValue.Length == 8);
        Assert.Contains(fields, field => field.Label == details.L.CustomIconType && field.DisplayValue == details.L.Get("CustomIconSimple"));
        Assert.Contains(fields, field => field.Label == details.L.CustomIconValue && field.DisplayValue == "github");
        Assert.Contains(fields, field => field.Label == "Backup code" && field.DisplayValue == "654321" && field.IsSensitive);
        Assert.Empty(details.PasswordHistory);

        var customFieldsGroup = details.Groups.Single(group => group.Title == details.L.CustomFields);
        Assert.False(customFieldsGroup.IsExpanded);
        Assert.Empty(customFieldsGroup.VisibleFields);
        customFieldsGroup.IsExpanded = true;
        Assert.Contains(customFieldsGroup.VisibleFields, field => field.Label == "Backup code");

        var backupCode = fields.Single(field => field.Label == "Backup code");
        Assert.True(backupCode.CanToggleVisibility);
        Assert.Equal("************", backupCode.DisplayText);
        details.ToggleFieldVisibilityCommand.Execute(backupCode);
        Assert.Equal("654321", backupCode.DisplayText);
        details.ToggleFieldVisibilityCommand.Execute(backupCode);
        Assert.Equal("************", backupCode.DisplayText);

        await details.CopyFieldCommand.ExecuteAsync(backupCode);

        Assert.Equal("654321", harness.Clipboard.Text);
        Assert.Equal(details.L.Format("CopiedFieldFormat", "Backup code"), details.StatusText);
    }

    [Fact]
    public async Task ViewModel_shows_password_history_and_detail_commands_manage_it()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("correct password", new byte[16]);
        var entry = new PasswordEntry
        {
            Title = "GitHub",
            Username = "dev",
            Password = harness.Crypto.EncryptString("current-secret")
        };
        await harness.Repository.SavePasswordAsync(entry);
        var older = new PasswordHistoryEntry
        {
            EntryId = entry.Id,
            Password = harness.Crypto.EncryptString("older-secret"),
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await harness.Repository.SavePasswordHistoryAsync(older);
        var latest = new PasswordHistoryEntry
        {
            EntryId = entry.Id,
            Password = harness.Crypto.EncryptString("latest-secret"),
            LastUsedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        await harness.Repository.SavePasswordHistoryAsync(latest);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(harness.ViewModel.Passwords.Single());

        var details = Assert.IsType<PasswordDetailViewModel>(harness.DetailDialog.LastDetails);
        Assert.True(details.HasPasswordHistory);
        Assert.Equal(["latest-secret", "older-secret"], details.PasswordHistory.Select(item => item.Password).ToArray());
        Assert.True(details.PasswordHistory[0].IsLatest);
        Assert.False(details.PasswordHistory[0].IsVisible);

        details.ToggleHistoryPasswordCommand.Execute(details.PasswordHistory[0]);
        Assert.True(details.PasswordHistory[0].IsVisible);
        Assert.Equal("latest-secret", details.PasswordHistory[0].DisplayPassword);

        await details.CopyHistoryPasswordCommand.ExecuteAsync(details.PasswordHistory[0]);
        Assert.Equal("latest-secret", harness.Clipboard.Text);

        await details.DeleteHistoryPasswordCommand.ExecuteAsync(details.PasswordHistory[0]);
        Assert.Equal(["older-secret"], (await harness.Repository.GetPasswordHistoryAsync(entry.Id)).Select(item => harness.Crypto.DecryptString(item.Password)).ToArray());
        Assert.Single(details.PasswordHistory);

        await details.ClearPasswordHistoryCommand.ExecuteAsync(null);
        Assert.Empty(await harness.Repository.GetPasswordHistoryAsync(entry.Id));
        Assert.False(details.HasPasswordHistory);
    }

    [Fact]
    public void ViewModel_embedded_password_details_keeps_latest_selection_when_switching_quickly()
    {
        RunOnStaThread(ViewModelEmbeddedPasswordDetailsKeepsLatestSelectionWhenSwitchingQuicklyCore);
    }

    private static void ViewModelEmbeddedPasswordDetailsKeepsLatestSelectionWhenSwitchingQuicklyCore()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("correct password", new byte[16]);
        var first = new PasswordEntry
        {
            Title = "First",
            Username = "first-user",
            Password = harness.Crypto.EncryptString("first-secret")
        };
        var second = new PasswordEntry
        {
            Title = "Second",
            Username = "second-user",
            Password = harness.Crypto.EncryptString("second-secret")
        };
        harness.Repository.SavePasswordAsync(first).GetAwaiter().GetResult();
        harness.Repository.SavePasswordAsync(second).GetAwaiter().GetResult();
        harness.ViewModel.LoadAsync().GetAwaiter().GetResult();

        var displayedFirst = harness.ViewModel.Passwords.Single(item => item.Id == first.Id);
        var displayedSecond = harness.ViewModel.Passwords.Single(item => item.Id == second.Id);

        harness.ViewModel.SelectedPassword = displayedFirst;
        Assert.False(harness.ViewModel.IsLoadingSelectedPasswordDetails);
        Assert.Null(harness.ViewModel.SelectedPasswordDetails);

        harness.ViewModel.SelectedPassword = displayedSecond;

        WaitForCondition(() =>
            harness.ViewModel.SelectedPasswordDetails?.Entry.Id == second.Id &&
            !harness.ViewModel.IsLoadingSelectedPasswordDetails);

        Assert.Equal(second.Id, harness.ViewModel.SelectedPassword?.Id);
        Assert.Equal(second.Id, harness.ViewModel.SelectedPasswordDetails?.Entry.Id);
        Assert.DoesNotContain(
            harness.ViewModel.SelectedPasswordDetails!.Groups.SelectMany(group => group.Fields),
            field => field.DisplayValue == "first-secret");

        Thread.Sleep(150);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(second.Id, harness.ViewModel.SelectedPasswordDetails?.Entry.Id);
    }

    [Fact]
    public void ViewModel_embedded_password_details_handles_many_rapid_selection_changes()
    {
        RunOnStaThread(ViewModelEmbeddedPasswordDetailsHandlesManyRapidSelectionChangesCore);
    }

    private static void ViewModelEmbeddedPasswordDetailsHandlesManyRapidSelectionChangesCore()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("correct password", new byte[16]);
        const int passwordCount = 30;
        for (var index = 0; index < passwordCount; index++)
        {
            var entry = new PasswordEntry
            {
                Title = $"Account {index:00}",
                Website = $"https://account-{index:00}.example.com",
                Username = $"user-{index:00}",
                Password = harness.Crypto.EncryptString($"secret-{index:00}")
            };
            harness.Repository.SavePasswordAsync(entry).GetAwaiter().GetResult();
            if (index % 3 == 0)
            {
                harness.Repository.ReplaceCustomFieldsAsync(entry.Id,
                [
                    new CustomField { Title = "Recovery", Value = $"field-{index:00}", SortOrder = 0 }
                ]).GetAwaiter().GetResult();
            }

            if (index % 4 == 0)
            {
                harness.Repository.SaveAttachmentAsync(new Attachment
                {
                    OwnerType = "PASSWORD",
                    OwnerId = entry.Id,
                    FileName = $"file-{index:00}.txt",
                    ContentType = "text/plain",
                    StoragePath = $"secure_attachments/file-{index:00}.enc",
                    SizeBytes = 128 + index
                }).GetAwaiter().GetResult();
            }

            if (index % 5 == 0)
            {
                harness.Repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
                {
                    EntryId = entry.Id,
                    Password = harness.Crypto.EncryptString($"old-secret-{index:00}"),
                    LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(-index)
                }).GetAwaiter().GetResult();
            }
        }

        harness.ViewModel.LoadAsync().GetAwaiter().GetResult();
        var visiblePasswords = harness.ViewModel.Passwords
            .OrderBy(item => item.Title, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(passwordCount, visiblePasswords.Length);

        var stopwatch = Stopwatch.StartNew();
        foreach (var entry in visiblePasswords)
        {
            harness.ViewModel.SelectedPassword = entry;
            Assert.Equal(entry.Id, harness.ViewModel.SelectedPassword?.Id);
        }

        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"Rapid selection setters took {stopwatch.ElapsedMilliseconds} ms.");

        var latest = visiblePasswords[^1];
        WaitForCondition(() =>
            harness.ViewModel.SelectedPasswordDetails?.Entry.Id == latest.Id &&
            !harness.ViewModel.IsLoadingSelectedPasswordDetails);

        Assert.Equal(latest.Id, harness.ViewModel.SelectedPassword?.Id);
        Assert.Equal(latest.Id, harness.ViewModel.SelectedPasswordDetails?.Entry.Id);
        Assert.Contains(
            harness.ViewModel.SelectedPasswordDetails!.Groups.SelectMany(group => group.Fields),
            field => field.DisplayValue == "secret-29");

        Thread.Sleep(200);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(latest.Id, harness.ViewModel.SelectedPasswordDetails?.Entry.Id);
        Assert.DoesNotContain(
            harness.ViewModel.SelectedPasswordDetails!.Groups.SelectMany(group => group.Fields),
            field => field.DisplayValue.StartsWith("secret-", StringComparison.Ordinal) &&
                field.DisplayValue != "secret-29");
    }

    [Fact]
    public void ViewModel_selecting_password_with_large_detail_payload_returns_immediately()
    {
        RunOnStaThread(ViewModelSelectingPasswordWithLargeDetailPayloadReturnsImmediatelyCore);
    }

    private static void ViewModelSelectingPasswordWithLargeDetailPayloadReturnsImmediatelyCore()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("correct password", new byte[16]);
        var entry = new PasswordEntry
        {
            Title = "Large detail account",
            Website = "https://large.example.com",
            Username = "large-user",
            Password = harness.Crypto.EncryptString("large-secret"),
            Notes = new string('n', 4000),
            SshKeyData = new string('s', 4000)
        };
        harness.Repository.SavePasswordAsync(entry).GetAwaiter().GetResult();

        harness.Repository.ReplaceCustomFieldsAsync(
            entry.Id,
            Enumerable.Range(0, 150)
                .Select(index => new CustomField
                {
                    Title = $"Field {index:000}",
                    Value = $"value-{index:000}",
                    SortOrder = index
                })
                .ToArray()).GetAwaiter().GetResult();

        for (var index = 0; index < 40; index++)
        {
            harness.Repository.SaveAttachmentAsync(new Attachment
            {
                OwnerType = "PASSWORD",
                OwnerId = entry.Id,
                FileName = $"attachment-{index:000}.txt",
                ContentType = "text/plain",
                StoragePath = $"secure_attachments/attachment-{index:000}.enc",
                SizeBytes = 1024 + index
            }).GetAwaiter().GetResult();
            harness.Repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
            {
                EntryId = entry.Id,
                Password = harness.Crypto.EncryptString($"history-secret-{index:000}"),
                LastUsedAt = DateTimeOffset.UtcNow.AddMinutes(-index)
            }).GetAwaiter().GetResult();
        }

        harness.ViewModel.LoadAsync().GetAwaiter().GetResult();
        var displayed = Assert.Single(harness.ViewModel.Passwords);

        var stopwatch = Stopwatch.StartNew();
        harness.ViewModel.SelectedPassword = displayed;
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Selecting a large detail payload took {stopwatch.ElapsedMilliseconds} ms.");
        Assert.Equal(displayed.Id, harness.ViewModel.SelectedPassword?.Id);
        Assert.False(harness.ViewModel.IsLoadingSelectedPasswordDetails);
        Assert.Null(harness.ViewModel.SelectedPasswordDetails);

        WaitForCondition(() =>
            harness.ViewModel.SelectedPasswordDetails?.Entry.Id == displayed.Id &&
            !harness.ViewModel.IsLoadingSelectedPasswordDetails);

        Assert.Contains(
            harness.ViewModel.SelectedPasswordDetails!.Groups.SelectMany(group => group.Fields),
            field => field.DisplayValue == "large-secret");
    }

    [Fact]
    public void ViewModel_switching_password_after_details_rendered_keeps_selection_setter_light()
    {
        RunOnStaThread(ViewModelSwitchingPasswordAfterDetailsRenderedKeepsSelectionSetterLightCore);
    }

    private static void ViewModelSwitchingPasswordAfterDetailsRenderedKeepsSelectionSetterLightCore()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("correct password", new byte[16]);
        var first = new PasswordEntry
        {
            Title = "Rendered detail",
            Website = "https://rendered.example.com",
            Username = "rendered-user",
            Password = harness.Crypto.EncryptString("rendered-secret"),
            Notes = new string('a', 3000)
        };
        var second = new PasswordEntry
        {
            Title = "Next detail",
            Website = "https://next.example.com",
            Username = "next-user",
            Password = harness.Crypto.EncryptString("next-secret")
        };
        harness.Repository.SavePasswordAsync(first).GetAwaiter().GetResult();
        harness.Repository.SavePasswordAsync(second).GetAwaiter().GetResult();
        harness.Repository.ReplaceCustomFieldsAsync(
            first.Id,
            Enumerable.Range(0, 120)
                .Select(index => new CustomField
                {
                    Title = $"Rendered field {index:000}",
                    Value = $"rendered-value-{index:000}",
                    SortOrder = index
                })
                .ToArray()).GetAwaiter().GetResult();
        harness.ViewModel.LoadAsync().GetAwaiter().GetResult();

        var displayedFirst = harness.ViewModel.Passwords.Single(item => item.Id == first.Id);
        var displayedSecond = harness.ViewModel.Passwords.Single(item => item.Id == second.Id);
        harness.ViewModel.SelectedPassword = displayedFirst;
        WaitForCondition(() =>
            harness.ViewModel.SelectedPasswordDetails?.Entry.Id == first.Id &&
            harness.ViewModel.HasCurrentSelectedPasswordDetails);
        var renderedFirstDetails = harness.ViewModel.SelectedPasswordDetails;
        Assert.NotNull(renderedFirstDetails);

        var stopwatch = Stopwatch.StartNew();
        harness.ViewModel.SelectedPassword = displayedSecond;
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Switching away from a rendered detail took {stopwatch.ElapsedMilliseconds} ms.");
        Assert.Equal(second.Id, harness.ViewModel.SelectedPassword?.Id);
        Assert.Same(renderedFirstDetails, harness.ViewModel.SelectedPasswordDetails);
        Assert.False(harness.ViewModel.HasCurrentSelectedPasswordDetails);

        WaitForCondition(() =>
            harness.ViewModel.SelectedPasswordDetails?.Entry.Id == second.Id &&
            harness.ViewModel.HasCurrentSelectedPasswordDetails &&
            !harness.ViewModel.IsLoadingSelectedPasswordDetails);

        Assert.Contains(
            harness.ViewModel.SelectedPasswordDetails!.Groups.SelectMany(group => group.Fields),
            field => field.DisplayValue == "next-secret");
    }

    [Fact]
    public async Task ViewModel_caches_filtered_passwords_between_filter_changes()
    {
        var harness = CreateHarness();
        const int passwordCount = 200;
        for (var index = 0; index < passwordCount; index++)
        {
            await harness.Repository.SavePasswordAsync(new PasswordEntry
            {
                Title = $"Account {index:000}",
                Website = index % 2 == 0 ? "https://work.example.com" : "https://personal.example.com",
                Username = $"user-{index:000}",
                Password = $"secret-{index:000}",
                IsFavorite = index % 5 == 0,
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-index)
            });
        }

        await harness.ViewModel.LoadAsync();

        var firstSnapshot = harness.ViewModel.FilteredPasswords;
        var secondSnapshot = harness.ViewModel.FilteredPasswords;

        Assert.Same(firstSnapshot, secondSnapshot);
        Assert.Equal(passwordCount, firstSnapshot.Count);

        harness.ViewModel.SelectedPasswordSort = "title-asc";
        var sortedSnapshot = harness.ViewModel.FilteredPasswords;

        Assert.NotSame(firstSnapshot, sortedSnapshot);
        Assert.Same(sortedSnapshot, harness.ViewModel.FilteredPasswords);

        harness.ViewModel.QuickFilterFavorite = true;
        var favoriteSnapshot = harness.ViewModel.FilteredPasswords;

        Assert.NotSame(sortedSnapshot, favoriteSnapshot);
        Assert.Same(favoriteSnapshot, harness.ViewModel.FilteredPasswords);
        Assert.All(favoriteSnapshot, item => Assert.True(item.IsFavorite));
    }

    [Fact]
    public async Task ViewModel_batches_password_selection_state_notifications()
    {
        var harness = CreateHarness();
        const int passwordCount = 24;
        for (var index = 0; index < passwordCount; index++)
        {
            await harness.Repository.SavePasswordAsync(new PasswordEntry
            {
                Title = $"Account {index:000}",
                Website = "https://selection.example.com",
                Username = $"user-{index:000}",
                Password = $"secret-{index:000}"
            });
        }

        await harness.ViewModel.LoadAsync();

        var selectedCountNotifications = 0;
        var allFilteredNotifications = 0;
        harness.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedPasswordCount))
            {
                selectedCountNotifications++;
            }

            if (e.PropertyName == nameof(MainWindowViewModel.AreAllFilteredPasswordsSelected))
            {
                allFilteredNotifications++;
            }
        };

        harness.ViewModel.AreAllFilteredPasswordsSelected = true;

        Assert.Equal(passwordCount, harness.ViewModel.SelectedPasswordCount);
        Assert.True(harness.ViewModel.AreAllFilteredPasswordsSelected);
        Assert.True(selectedCountNotifications <= 2, $"Selecting all raised {selectedCountNotifications} selected-count notifications.");
        Assert.True(allFilteredNotifications <= 2, $"Selecting all raised {allFilteredNotifications} all-filtered notifications.");

        selectedCountNotifications = 0;
        allFilteredNotifications = 0;

        harness.ViewModel.ClearPasswordSelectionCommand.Execute(null);

        Assert.Equal(0, harness.ViewModel.SelectedPasswordCount);
        Assert.False(harness.ViewModel.HasSelectedPasswords);
        Assert.False(harness.ViewModel.AreAllFilteredPasswordsSelected);
        Assert.True(selectedCountNotifications <= 2, $"Clearing selection raised {selectedCountNotifications} selected-count notifications.");
        Assert.True(allFilteredNotifications <= 2, $"Clearing selection raised {allFilteredNotifications} all-filtered notifications.");
    }

    [Fact]
    public async Task ViewModel_records_and_opens_password_quick_access_items()
    {
        var harness = CreateHarness();
        var first = new PasswordEntry
        {
            Title = "First",
            Website = "first.example",
            Username = "first-user",
            Password = "one"
        };
        var second = new PasswordEntry
        {
            Title = "Second",
            Website = "second.example",
            Username = "second-user",
            Password = "two"
        };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(harness.ViewModel.Passwords.Single(item => item.Id == first.Id));
        await Task.Delay(5);
        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(harness.ViewModel.Passwords.Single(item => item.Id == second.Id));
        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(harness.ViewModel.Passwords.Single(item => item.Id == second.Id));

        Assert.True(harness.ViewModel.HasPasswordQuickAccessItems);
        Assert.Equal(["Second", "First"], harness.ViewModel.RecentPasswordQuickAccessItems.Select(item => item.Entry.Title).ToArray());
        var frequent = harness.ViewModel.FrequentPasswordQuickAccessItems.ToArray();
        Assert.Equal("Second", frequent[0].Entry.Title);
        Assert.Equal(2, frequent[0].OpenCount);
        Assert.Contains("second-user", frequent[0].Subtitle, StringComparison.Ordinal);

        await harness.ViewModel.OpenQuickAccessPasswordCommand.ExecuteAsync(harness.ViewModel.RecentPasswordQuickAccessItems.First(item => item.Entry.Id == first.Id));

        var records = await harness.Repository.GetPasswordQuickAccessRecordsAsync();
        Assert.Equal(2, records.Single(item => item.PasswordId == first.Id).OpenCount);
        Assert.Equal(first.Id, harness.DetailDialog.LastDetails?.Entry.Id);
    }

    [Fact]
    public async Task ViewModel_copies_username_and_batches_selected_passwords()
    {
        var harness = CreateHarness();
        var first = new PasswordEntry { Title = "First", Website = "one.example", Username = "one-user", Password = "one" };
        var second = new PasswordEntry { Title = "Second", Website = "two.example", Username = "two-user", Password = "two" };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.CopyUsernameCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));

        Assert.Equal("one-user", harness.Clipboard.Text);

        var displayedFirst = harness.ViewModel.Passwords.First(item => item.Id == first.Id);
        var displayedSecond = harness.ViewModel.Passwords.First(item => item.Id == second.Id);
        displayedFirst.IsSelected = true;
        displayedSecond.IsSelected = true;

        await harness.ViewModel.FavoriteSelectedPasswordsCommand.ExecuteAsync(null);

        Assert.False(displayedFirst.IsSelected);
        Assert.False(displayedSecond.IsSelected);
        Assert.All(await harness.Repository.GetPasswordsAsync(), item => Assert.True(item.IsFavorite));
        Assert.False(harness.ViewModel.HasSelectedPasswords);

        displayedFirst.IsSelected = true;
        displayedSecond.IsSelected = true;

        await harness.ViewModel.DeleteSelectedPasswordsCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.Passwords);
        Assert.Equal(2, harness.ViewModel.DeletedPasswords.Count);
        Assert.Empty(await harness.Repository.GetPasswordsAsync());
        Assert.All(await harness.Repository.GetPasswordsAsync(includeDeleted: true), item => Assert.True(item.IsDeleted));
        Assert.Equal(harness.ViewModel.L.Format("MovedSelectedPasswordsToRecycleBinFormat", 2), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_adds_edits_favorites_and_deletes_totp_items()
    {
        var harness = CreateHarness();
        await harness.ViewModel.LoadAsync();
        harness.TotpDialog.ConfigureNext(editor =>
        {
            editor.Title = "GitHub";
            editor.Secret = "JBSWY3DPEHPK3PXP";
            editor.Issuer = "GitHub";
            editor.AccountName = "dev@example.com";
            editor.Notes = "primary account";
        });

        await harness.ViewModel.AddTotpCommand.ExecuteAsync(null);

        var item = Assert.Single(harness.ViewModel.TotpItems);
        Assert.Equal("GitHub", item.Title);
        Assert.Equal("primary account", item.Notes);
        Assert.True(item.Id > 0);
        Assert.Single(await harness.Repository.GetSecureItemsAsync(VaultItemType.Totp));

        harness.TotpDialog.ConfigureNext(editor =>
        {
            editor.Title = "GitHub prod";
            editor.Secret = "JBSWY3DPEHPK3PXP";
            editor.AccountName = "prod@example.com";
            editor.IsFavorite = true;
        });

        await harness.ViewModel.EditTotpCommand.ExecuteAsync(item);

        Assert.Equal("GitHub prod", item.Title);
        Assert.True(item.IsFavorite);
        var stored = Assert.Single(await harness.Repository.GetSecureItemsAsync(VaultItemType.Totp));
        Assert.True(stored.IsFavorite);
        Assert.Contains("prod@example.com", stored.ItemData);

        await harness.ViewModel.ToggleTotpFavoriteCommand.ExecuteAsync(item);

        Assert.False(item.IsFavorite);

        item.IsSelected = true;
        await harness.ViewModel.FavoriteSelectedTotpCommand.ExecuteAsync(null);

        Assert.False(item.IsSelected);
        Assert.True(item.IsFavorite);
        Assert.False(harness.ViewModel.HasSelectedTotpItems);

        item.IsSelected = true;
        await harness.ViewModel.DeleteSelectedTotpCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.TotpItems);
        Assert.Empty(await harness.Repository.GetSecureItemsAsync(VaultItemType.Totp));
        Assert.Single(await harness.Repository.GetSecureItemsAsync(VaultItemType.Totp, includeDeleted: true));
        Assert.Equal(harness.ViewModel.L.Format("MovedSelectedTotpToRecycleBinFormat", 1), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_filters_totp_console_by_group_favorite_unbound_and_search()
    {
        var harness = CreateHarness();
        var boundPassword = new PasswordEntry
        {
            Title = "GitHub work",
            Username = "dev@example.com",
            Password = "secret",
            AuthenticatorKey = "otpauth://totp/GitHub:dev@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub"
        };
        await harness.Repository.SavePasswordAsync(boundPassword);
        await harness.Repository.SaveSecureItemAsync(new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "GitHub personal",
            Notes = "primary account",
            IsFavorite = true,
            ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "personal@example.com"))
        });
        await harness.Repository.SaveSecureItemAsync(new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Azure admin",
            Notes = "tenant owner",
            ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "Microsoft", "azure@example.com"))
        });

        await harness.ViewModel.LoadAsync();

        Assert.Equal(3, harness.ViewModel.TotpItems.Count);
        Assert.Equal(3, harness.ViewModel.FilteredTotpItems.Count);
        Assert.Contains(harness.ViewModel.TotpFilterChoices, item => item.Key == "all" && item.Count == 3 && item.IsSelected);
        Assert.Contains(harness.ViewModel.TotpFilterChoices, item => item.Key == "favorites" && item.Count == 1);
        Assert.Contains(harness.ViewModel.TotpFilterChoices, item => item.Key == "unbound" && item.Count == 2);
        Assert.Contains(harness.ViewModel.TotpFilterChoices, item => item.Key == "issuer:GitHub" && item.Count == 2);

        harness.ViewModel.SelectTotpFilterCommand.Execute("favorites");

        var favorite = Assert.Single(harness.ViewModel.FilteredTotpItems);
        Assert.Equal("GitHub personal", favorite.Title);
        Assert.Equal(favorite.Id, harness.ViewModel.SelectedTotpItem?.Id);

        harness.ViewModel.SelectTotpFilterCommand.Execute("unbound");

        Assert.Equal(2, harness.ViewModel.FilteredTotpItems.Count);
        Assert.DoesNotContain(harness.ViewModel.FilteredTotpItems, item => item.BoundPasswordId == boundPassword.Id);

        harness.ViewModel.SelectTotpFilterCommand.Execute("issuer:GitHub");

        Assert.Equal(2, harness.ViewModel.FilteredTotpItems.Count);
        Assert.All(harness.ViewModel.FilteredTotpItems, item => Assert.Contains("GitHub", item.Title));

        harness.ViewModel.SelectTotpFilterCommand.Execute("all");
        harness.ViewModel.SearchText = "azure";

        var searched = Assert.Single(harness.ViewModel.FilteredTotpItems);
        Assert.Equal("Azure admin", searched.Title);

        harness.ViewModel.SearchText = "missing authenticator";

        Assert.False(harness.ViewModel.HasFilteredTotpItems);
        Assert.True(harness.ViewModel.HasTotpFilterOrSearch);

        harness.ViewModel.ClearTotpFiltersCommand.Execute(null);

        Assert.Equal("", harness.ViewModel.SearchText);
        Assert.Equal(3, harness.ViewModel.FilteredTotpItems.Count);
        Assert.Contains(harness.ViewModel.TotpFilterChoices, item => item.Key == "all" && item.IsSelected);
    }

    [Fact]
    public async Task ViewModel_edits_and_deletes_virtual_totp_from_bound_password()
    {
        var harness = CreateHarness();
        var password = new PasswordEntry
        {
            Title = "GitHub",
            Website = "github.com",
            Username = "dev",
            Password = "secret",
            AuthenticatorKey = "JBSWY3DPEHPK3PXP"
        };
        await harness.Repository.SavePasswordAsync(password);
        await harness.ViewModel.LoadAsync();
        var item = Assert.Single(harness.ViewModel.TotpItems, entry => entry.BoundPasswordId == password.Id);

        harness.TotpDialog.ConfigureNext(editor =>
        {
            editor.Title = "GitHub mobile";
            editor.Secret = "JBSWY3DPEHPK3PXP";
            editor.Issuer = "GitHub";
            editor.AccountName = "mobile@example.com";
            editor.IsFavorite = true;
        });

        await harness.ViewModel.EditTotpCommand.ExecuteAsync(item);

        var updatedPassword = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Equal("GitHub mobile", updatedPassword.Title);
        Assert.Equal("mobile@example.com", updatedPassword.Username);
        Assert.Contains("otpauth://totp/", updatedPassword.AuthenticatorKey);
        Assert.True(updatedPassword.IsFavorite);
        var storedTotp = Assert.Single(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(password.Id));
        Assert.True(storedTotp.IsFavorite);

        var displayed = Assert.Single(harness.ViewModel.TotpItems, entry => entry.BoundPasswordId == password.Id);
        await harness.ViewModel.DeleteTotpCommand.ExecuteAsync(displayed);

        updatedPassword = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Empty(updatedPassword.AuthenticatorKey);
        Assert.Empty(harness.ViewModel.TotpItems);
        Assert.Empty(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(password.Id));
    }

    [Fact]
    public async Task ViewModel_adds_edits_shows_and_batch_deletes_wallet_items()
    {
        var harness = CreateHarness();
        await harness.ViewModel.LoadAsync();
        harness.WalletDialog.ConfigureNext(editor =>
        {
            editor.SelectedWalletType = editor.WalletTypeOptions.Single(item => item.Value == VaultItemType.Document);
            editor.Title = "Passport";
            editor.DocumentNumber = "P12345678";
            editor.FullName = "Ada Lovelace";
            editor.IssuedDate = "2020-01-01";
            editor.ExpiryDate = "2030-01-01";
            editor.IssuedBy = "UK";
            editor.ImagePathsText = "front.png\nback.png";
            editor.Notes = "Travel document";
        });

        await harness.ViewModel.AddWalletItemCommand.ExecuteAsync(null);

        var document = Assert.Single(harness.ViewModel.WalletItems);
        Assert.Equal(VaultItemType.Document, document.ItemType);
        Assert.Equal("Passport", document.Title);
        Assert.Contains("P12345678", document.ItemData);
        Assert.Contains("front.png", document.ImagePaths);
        Assert.Equal(document.Id, harness.ViewModel.SelectedWalletItem?.Id);
        Assert.Equal("P12***5678", harness.ViewModel.SelectedWalletDetails?.PrimaryText);
        Assert.True(harness.ViewModel.SelectedWalletDetails?.HasImages);

        harness.WalletDialog.ConfigureNext(editor =>
        {
            editor.SelectedWalletType = editor.WalletTypeOptions.Single(item => item.Value == VaultItemType.BankCard);
            editor.Title = "Work card";
            editor.CardNumber = "4111111111111111";
            editor.CardholderName = "Ada Lovelace";
            editor.BankName = "Monica Bank";
            editor.ExpiryMonth = "12";
            editor.ExpiryYear = "2030";
            editor.Cvv = "123";
            editor.ImagePathsText = "card-front.png";
        });

        await harness.ViewModel.EditWalletItemCommand.ExecuteAsync(document);

        Assert.Equal(VaultItemType.BankCard, document.ItemType);
        Assert.Equal("Work card", document.Title);
        Assert.Contains("4111111111111111", document.ItemData);
        Assert.Equal("**** **** **** 1111", harness.ViewModel.SelectedWalletDetails?.PrimaryText);

        document.IsSelected = true;
        await harness.ViewModel.DeleteSelectedWalletItemsCommand.ExecuteAsync(null);

        Assert.Empty(harness.ViewModel.WalletItems);
        Assert.Empty(await harness.Repository.GetSecureItemsAsync(VaultItemType.BankCard));
        Assert.Single(await harness.Repository.GetSecureItemsAsync(VaultItemType.BankCard, includeDeleted: true));
        Assert.Equal(harness.ViewModel.L.Format("MovedSelectedWalletItemsToRecycleBinFormat", 1), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_load_clears_secure_item_selections_missing_from_repository_snapshot()
    {
        var harness = CreateHarness();
        var notePayload = NoteContentCodec.BuildSavePayload("Recovery", "backup codes", "ops", true, []);
        var note = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = notePayload.Title,
            Notes = notePayload.NotesCache,
            ItemData = notePayload.ItemData,
            ImagePaths = notePayload.ImagePaths
        };
        var wallet = new SecureItem
        {
            ItemType = VaultItemType.Document,
            Title = "Passport",
            ItemData = WalletItemDataCodec.EncodeDocument(new DocumentWalletData
            {
                DocumentNumber = "P12345678"
            }),
            ImagePaths = "[]"
        };
        await harness.Repository.SaveSecureItemAsync(note);
        await harness.Repository.SaveSecureItemAsync(wallet);
        await harness.ViewModel.LoadAsync();
        harness.ViewModel.SelectedNote = Assert.Single(harness.ViewModel.NoteItems);
        harness.ViewModel.SelectedWalletItem = Assert.Single(harness.ViewModel.WalletItems);

        await harness.Repository.SoftDeleteSecureItemAsync(note.Id);
        await harness.Repository.SoftDeleteSecureItemAsync(wallet.Id);
        await harness.ViewModel.LoadAsync();

        Assert.Null(harness.ViewModel.SelectedNote);
        Assert.Equal("", harness.ViewModel.NoteTitle);
        Assert.Equal("", harness.ViewModel.NoteContent);
        Assert.Null(harness.ViewModel.SelectedWalletItem);
        Assert.Null(harness.ViewModel.SelectedWalletDetails);
        Assert.False(harness.ViewModel.HasSelectedWalletItem);
    }

    [Fact]
    public void Wallet_details_hide_mdbx_image_storage_ids()
    {
        var data = new DocumentWalletData
        {
            DocumentNumber = "P12345678",
            FullName = "Ada Lovelace",
            ImagePaths = ["front.png", "mdbx:document-image-1"]
        };
        var details = new WalletItemDetailsViewModel(
            new LocalizationService(),
            new SecureItem
            {
                ItemType = VaultItemType.Document,
                Title = "Passport",
                ItemData = WalletItemDataCodec.EncodeDocument(data),
                ImagePaths = WalletItemDataCodec.EncodeImagePaths(data.ImagePaths)
            });

        Assert.Equal(["front.png"], details.ImagePaths);
        Assert.True(details.HasImages);
        Assert.Equal("front.png", details.FrontImagePath);
        Assert.DoesNotContain(details.ImagePaths, path => path.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Wallet_editor_hides_mdbx_image_storage_ids_but_preserves_them_on_save()
    {
        var data = new DocumentWalletData
        {
            DocumentNumber = "P12345678",
            FullName = "Ada Lovelace",
            ImagePaths = ["front.png", "mdbx:document-image-1"]
        };
        var item = new SecureItem
        {
            ItemType = VaultItemType.Document,
            Title = "Passport",
            ItemData = WalletItemDataCodec.EncodeDocument(data),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(data.ImagePaths)
        };
        var editor = new WalletItemEditorViewModel(new LocalizationService(), item);

        Assert.Contains("front.png", editor.ImagePathsText, StringComparison.Ordinal);
        Assert.DoesNotContain("mdbx:", editor.ImagePathsText, StringComparison.OrdinalIgnoreCase);

        editor.FullName = "Ada Byron";
        editor.ImagePathsText = "front.png\nback.png";
        editor.ApplyTo(item);

        var updated = WalletItemDataCodec.DecodeDocument(item);
        Assert.Equal(["front.png", "back.png", "mdbx:document-image-1"], updated.ImagePaths);
        Assert.Equal(WalletItemDataCodec.EncodeImagePaths(updated.ImagePaths), item.ImagePaths);
    }

    [Fact]
    public async Task ViewModel_moves_selected_passwords_to_category_and_updates_bound_totp()
    {
        var harness = CreateHarness();
        var work = new Category { Name = "Work", SortOrder = 1 };
        var personal = new Category { Name = "Personal", SortOrder = 2 };
        await harness.Repository.SaveCategoryAsync(work);
        await harness.Repository.SaveCategoryAsync(personal);
        var first = new PasswordEntry
        {
            Title = "GitHub",
            Website = "github.com",
            Username = "dev",
            Password = "one",
            CategoryId = work.Id,
            AuthenticatorKey = "JBSWY3DPEHPK3PXP"
        };
        await harness.Repository.SavePasswordAsync(first);
        var second = new PasswordEntry
        {
            Title = "GitHub",
            Website = "github.com",
            Username = "dev",
            Password = "two",
            CategoryId = work.Id
        };
        await harness.Repository.SavePasswordAsync(second);
        var totp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "GitHub",
            BoundPasswordId = first.Id,
            CategoryId = work.Id,
            ItemData = TotpDataResolver.ToItemData(TotpDataResolver.FromAuthenticatorKey(first.AuthenticatorKey)!)
        };
        await harness.Repository.SaveSecureItemAsync(totp);
        await harness.ViewModel.LoadAsync();
        harness.CategoryPicker.SelectNext(personal.Id, personal.Name);

        foreach (var item in harness.ViewModel.Passwords)
        {
            item.IsSelected = true;
        }

        await harness.ViewModel.MoveSelectedPasswordsToCategoryCommand.ExecuteAsync(null);

        Assert.Equal(work.Id, harness.CategoryPicker.LastSelectedCategoryId);
        Assert.Contains(harness.CategoryPicker.LastCategories, item => item.Id == personal.Id);
        Assert.All(await harness.Repository.GetPasswordsAsync(), item => Assert.Equal(personal.Id, item.CategoryId));
        Assert.All(harness.ViewModel.Passwords, item =>
        {
            Assert.Equal(personal.Id, item.CategoryId);
            Assert.False(item.IsSelected);
        });
        var movedTotp = Assert.Single(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(first.Id));
        Assert.Equal(personal.Id, movedTotp.CategoryId);
        Assert.Single(harness.ViewModel.TotpItems, item => item.BoundPasswordId == first.Id && item.CategoryId == personal.Id);
        Assert.Equal(harness.ViewModel.L.Format("MovedSelectedPasswordsToFolderFormat", 2, personal.Name), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_creates_and_filters_password_folders()
    {
        var harness = CreateHarness();
        var work = new Category { Name = "Work", SortOrder = 1 };
        await harness.Repository.SaveCategoryAsync(work);
        await harness.Repository.SavePasswordAsync(new PasswordEntry { Title = "Work Portal", CategoryId = work.Id, Password = "one" });
        await harness.Repository.SavePasswordAsync(new PasswordEntry { Title = "Personal Portal", Password = "two" });
        await harness.ViewModel.LoadAsync();

        Assert.Contains(harness.ViewModel.PasswordFolderFilters, item => item.Name == "All folders");
        Assert.Contains(harness.ViewModel.PasswordFolderFilters, item => item.Name == "Work");
        Assert.Contains(harness.ViewModel.PasswordFolderFilters, item => item.Name == "No folder");
        harness.ViewModel.SelectedPasswordFolderFilter = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == work.Id);
        Assert.Equal(["Work Portal"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.SelectedPasswordFolderFilter = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == -1);
        Assert.Equal(["Personal Portal"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.NewFolderName = "Finance";
        await harness.ViewModel.CreatePasswordFolderCommand.ExecuteAsync(null);

        var finance = Assert.Single(await harness.Repository.GetCategoriesAsync(), item => item.Name == "Finance");
        Assert.Equal(finance.Id, harness.ViewModel.SelectedPasswordFolderFilter?.Id);
        Assert.Empty(harness.ViewModel.NewFolderName);
        Assert.Equal(harness.ViewModel.L.Format("CreatedFolderFormat", "Finance"), harness.ViewModel.StatusMessage);

        harness.ViewModel.NewFolderName = "work";
        await harness.ViewModel.CreatePasswordFolderCommand.ExecuteAsync(null);

        Assert.Equal(work.Id, harness.ViewModel.SelectedPasswordFolderFilter?.Id);
        Assert.Equal(harness.ViewModel.L.Format("SelectedFolderFormat", "Work"), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_builds_nested_password_folder_tree_and_counts_current_query_scope()
    {
        var harness = CreateHarness();
        var work = new Category { Name = "Work", SortOrder = 1 };
        var infra = new Category { Name = "Work/Infra", SortOrder = 2 };
        var prod = new Category { Name = "Work/Infra/Prod", SortOrder = 3 };
        var personal = new Category { Name = "Personal", SortOrder = 4 };
        await harness.Repository.SaveCategoryAsync(work);
        await harness.Repository.SaveCategoryAsync(infra);
        await harness.Repository.SaveCategoryAsync(prod);
        await harness.Repository.SaveCategoryAsync(personal);
        await harness.Repository.SavePasswordAsync(new PasswordEntry { Title = "Work Root", CategoryId = work.Id, Password = "one" });
        await harness.Repository.SavePasswordAsync(new PasswordEntry { Title = "Infra Portal", CategoryId = infra.Id, Password = "two" });
        await harness.Repository.SavePasswordAsync(new PasswordEntry { Title = "Prod Portal", CategoryId = prod.Id, Password = "three" });
        await harness.Repository.SavePasswordAsync(new PasswordEntry { Title = "Personal Prod", CategoryId = personal.Id, Password = "four" });
        await harness.ViewModel.LoadAsync();

        var workNode = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == work.Id);
        var infraNode = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == infra.Id);
        var prodNode = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == prod.Id);

        Assert.Equal(0, workNode.Level);
        Assert.Equal(1, infraNode.Level);
        Assert.Equal(2, prodNode.Level);
        Assert.True(workNode.HasChildren);
        Assert.Equal(3, workNode.Count);
        Assert.Equal(2, infraNode.Count);
        Assert.Equal(1, prodNode.Count);

        harness.ViewModel.SelectedPasswordFolderFilter = workNode;
        Assert.Equal(
            ["Prod Portal", "Infra Portal", "Work Root"],
            harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.SelectedPasswordFolderFilter = infraNode;
        Assert.Equal(
            ["Prod Portal", "Infra Portal"],
            harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.PasswordSearchQuery = "Prod";
        workNode = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == work.Id);
        infraNode = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == infra.Id);
        prodNode = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == prod.Id);
        var personalNode = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == personal.Id);

        Assert.Equal(1, workNode.Count);
        Assert.Equal(1, infraNode.Count);
        Assert.Equal(1, prodNode.Count);
        Assert.Equal(1, personalNode.Count);
        Assert.Equal(2, harness.ViewModel.PasswordFolderFilters.Single(item => item.SelectionKey == "system:all").Count);
    }

    [Fact]
    public async Task Security_baseline_view_model_rejects_plaintext_webdav_backup()
    {
        var webDav = new FakeWebDavBackupService();
        var harness = CreateHarness(webDavBackupService: webDav);
        harness.Crypto.InitializeSession("source password", new byte[16]);
        var category = new Category { Name = "Work", SortOrder = 1 };
        await harness.Repository.SaveCategoryAsync(category);
        await harness.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "GitHub",
            Username = "dev",
            Password = harness.Crypto.EncryptString("plain-webdav-secret"),
            CategoryId = category.Id
        });
        await harness.Repository.SaveSecureItemAsync(new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "Recovery",
            ItemData = NoteContentCodec.BuildSavePayload("Recovery", "markdown body", "", true, ["inline.png"]).ItemData,
            ImagePaths = NoteContentCodec.EncodeStringArray(["inline.png"]),
            CategoryId = category.Id
        });
        await harness.ViewModel.LoadAsync();
        harness.ViewModel.WebDavEnabled = true;
        harness.ViewModel.WebDavServerUrl = "https://dav.example.com/";
        harness.ViewModel.WebDavRemotePath = "/Monica";
        harness.ViewModel.WebDavBackupIncludeTotp = false;
        harness.ViewModel.WebDavBackupIncludeCards = false;
        harness.ViewModel.WebDavBackupIncludeDocuments = false;
        harness.ViewModel.WebDavBackupIncludeImages = false;
        harness.ViewModel.WebDavBackupEncryptionEnabled = false;

        await harness.ViewModel.CreateWebDavBackupCommand.ExecuteAsync(null);

        Assert.Equal("", webDav.UploadedContent);
        Assert.Empty(harness.ViewModel.WebDavBackupHistory);
        Assert.Equal(harness.ViewModel.L.Get("WebDavEncryptionRequired"), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_creates_webdav_backup_from_repository_snapshot()
    {
        var webDav = new FakeWebDavBackupService();
        var harness = CreateHarness(webDavBackupService: webDav);
        harness.Crypto.InitializeSession("source password", new byte[16]);
        var entry = new PasswordEntry
        {
            Title = "Cached Portal",
            Username = "dev",
            Password = harness.Crypto.EncryptString("cached-secret")
        };
        await harness.Repository.SavePasswordAsync(entry);
        await harness.ViewModel.LoadAsync();

        entry.Title = "Fresh Portal";
        entry.Password = harness.Crypto.EncryptString("fresh-secret");
        await harness.Repository.SavePasswordAsync(entry);
        harness.ViewModel.WebDavEnabled = true;
        harness.ViewModel.WebDavServerUrl = "https://dav.example.com/";
        harness.ViewModel.WebDavBackupIncludeTotp = false;
        harness.ViewModel.WebDavBackupIncludeNotes = false;
        harness.ViewModel.WebDavBackupIncludeCards = false;
        harness.ViewModel.WebDavBackupIncludeDocuments = false;
        harness.ViewModel.WebDavBackupIncludeCategories = false;
        harness.ViewModel.WebDavBackupEncryptionEnabled = true;
        harness.ViewModel.WebDavBackupEncryptionPassword = "backup password";

        await harness.ViewModel.CreateWebDavBackupCommand.ExecuteAsync(null);

        Assert.EndsWith(".monica.enc.json", webDav.UploadedPath);
        Assert.DoesNotContain("Fresh Portal", webDav.UploadedContent);
        Assert.DoesNotContain("fresh-secret", webDav.UploadedContent);
        Assert.DoesNotContain("Cached Portal", webDav.UploadedContent);
        Assert.DoesNotContain("cached-secret", webDav.UploadedContent);
    }

    [Fact]
    public async Task ViewModel_restores_webdav_backup_and_rebinds_categories()
    {
        var sourceWebDav = new FakeWebDavBackupService();
        var source = CreateHarness(webDavBackupService: sourceWebDav);
        source.Crypto.InitializeSession("source password", new byte[16]);
        var category = new Category { Name = "Ops", SortOrder = 2 };
        await source.Repository.SaveCategoryAsync(category);
        var password = new PasswordEntry
        {
            Title = "Pager",
            Username = "ops",
            Password = source.Crypto.EncryptString("restore-secret"),
            CategoryId = category.Id
        };
        await source.Repository.SavePasswordAsync(password);
        await source.Repository.SaveSecureItemAsync(new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Pager",
            BoundPasswordId = password.Id,
            CategoryId = category.Id,
            ItemData = TotpDataResolver.ToItemData(TotpDataResolver.FromAuthenticatorKey("JBSWY3DPEHPK3PXP")!)
        });
        await source.ViewModel.LoadAsync();
        source.ViewModel.WebDavEnabled = true;
        source.ViewModel.WebDavServerUrl = "https://dav.example.com/";
        source.ViewModel.WebDavBackupEncryptionEnabled = true;
        source.ViewModel.WebDavBackupEncryptionPassword = "backup password";
        await source.ViewModel.CreateWebDavBackupCommand.ExecuteAsync(null);

        var targetWebDav = new FakeWebDavBackupService
        {
            DownloadContent = sourceWebDav.UploadedContent
        };
        var target = CreateHarness(webDavBackupService: targetWebDav);
        target.Crypto.InitializeSession("target password", new byte[16]);
        target.ViewModel.WebDavEnabled = true;
        target.ViewModel.WebDavServerUrl = "https://dav.example.com/";
        target.ViewModel.WebDavBackupEncryptionPassword = "backup password";
        var item = new WebDavBackupHistoryItem("monica_backup_20260601_120000.monica.enc.json", "/Monica/monica_backup_20260601_120000.monica.enc.json", "2026/06/01 12:00", "1 KB", null);

        await target.ViewModel.RestoreWebDavBackupCommand.ExecuteAsync(item);

        var restoredPassword = Assert.Single(await target.Repository.GetPasswordsAsync());
        Assert.Equal("Pager", restoredPassword.Title);
        Assert.Equal("restore-secret", target.Crypto.DecryptString(restoredPassword.Password));
        var restoredCategory = Assert.Single(await target.Repository.GetCategoriesAsync());
        Assert.Equal("Ops", restoredCategory.Name);
        Assert.Equal(restoredCategory.Id, restoredPassword.CategoryId);
        var restoredTotp = Assert.Single(await target.Repository.GetSecureItemsByBoundPasswordIdAsync(restoredPassword.Id));
        Assert.Equal(restoredCategory.Id, restoredTotp.CategoryId);
        Assert.Equal(target.ViewModel.L.Format("RestoredWebDavBackupFormat", item.FileName, 1, 1, 1), target.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_creates_and_restores_encrypted_webdav_backup()
    {
        var webDav = new FakeWebDavBackupService();
        var source = CreateHarness(webDavBackupService: webDav);
        source.Crypto.InitializeSession("source password", new byte[16]);
        await source.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Encrypted",
            Username = "user",
            Password = source.Crypto.EncryptString("encrypted-secret")
        });
        await source.ViewModel.LoadAsync();
        source.ViewModel.WebDavEnabled = true;
        source.ViewModel.WebDavServerUrl = "https://dav.example.com/";
        source.ViewModel.WebDavBackupEncryptionEnabled = true;
        source.ViewModel.WebDavBackupEncryptionPassword = "backup password";

        await source.ViewModel.CreateWebDavBackupCommand.ExecuteAsync(null);

        Assert.EndsWith(".monica.enc.json", webDav.UploadedPath);
        Assert.DoesNotContain("encrypted-secret", webDav.UploadedContent);

        var targetWebDav = new FakeWebDavBackupService { DownloadContent = webDav.UploadedContent };
        var target = CreateHarness(webDavBackupService: targetWebDav);
        target.Crypto.InitializeSession("target password", new byte[16]);
        target.ViewModel.WebDavEnabled = true;
        target.ViewModel.WebDavServerUrl = "https://dav.example.com/";
        target.ViewModel.WebDavBackupEncryptionPassword = "backup password";
        var item = new WebDavBackupHistoryItem("monica_backup_20260601_120000.monica.enc.json", "/Monica/monica_backup_20260601_120000.monica.enc.json", "2026/06/01 12:00", "1 KB", null);

        await target.ViewModel.RestoreWebDavBackupCommand.ExecuteAsync(item);

        var restored = Assert.Single(await target.Repository.GetPasswordsAsync());
        Assert.Equal("encrypted-secret", target.Crypto.DecryptString(restored.Password));
    }

    [Fact]
    public async Task ViewModel_renames_selected_password_folder()
    {
        var harness = CreateHarness();
        var work = new Category { Name = "Work", SortOrder = 1 };
        var personal = new Category { Name = "Personal", SortOrder = 2 };
        await harness.Repository.SaveCategoryAsync(work);
        await harness.Repository.SaveCategoryAsync(personal);
        await harness.ViewModel.LoadAsync();

        harness.ViewModel.SelectedPasswordFolderFilter = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == work.Id);
        Assert.True(harness.ViewModel.CanManageSelectedPasswordFolder);

        harness.ViewModel.NewFolderName = "Personal";
        await harness.ViewModel.RenameSelectedPasswordFolderCommand.ExecuteAsync(null);
        Assert.Equal(harness.ViewModel.L.Format("FolderAlreadyExistsFormat", "Personal"), harness.ViewModel.StatusMessage);

        harness.ViewModel.NewFolderName = "Engineering";
        await harness.ViewModel.RenameSelectedPasswordFolderCommand.ExecuteAsync(null);

        var renamed = Assert.Single(await harness.Repository.GetCategoriesAsync(), item => item.Id == work.Id);
        Assert.Equal("Engineering", renamed.Name);
        Assert.Equal(work.Id, harness.ViewModel.SelectedPasswordFolderFilter?.Id);
        Assert.Contains(harness.ViewModel.PasswordFolderFilters, item => item.Id == work.Id && item.Name == "Engineering");
        Assert.Empty(harness.ViewModel.NewFolderName);
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "UPDATE" && item.ItemType == "CATEGORY");
        Assert.Equal(harness.ViewModel.L.Format("RenamedFolderFormat", "Work", "Engineering"), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_deletes_selected_password_folder_and_uncategorizes_items()
    {
        var harness = CreateHarness();
        var work = new Category { Name = "Work", SortOrder = 1 };
        await harness.Repository.SaveCategoryAsync(work);
        var password = new PasswordEntry { Title = "Work Portal", CategoryId = work.Id, Password = "one" };
        await harness.Repository.SavePasswordAsync(password);
        var boundTotp = new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Work Portal",
            BoundPasswordId = password.Id,
            CategoryId = work.Id,
            ItemData = """{"secret":"JBSWY3DPEHPK3PXP"}"""
        };
        await harness.Repository.SaveSecureItemAsync(boundTotp);
        await harness.ViewModel.LoadAsync();

        harness.ViewModel.SelectedPasswordFolderFilter = harness.ViewModel.PasswordFolderFilters.Single(item => item.Id == work.Id);
        await harness.ViewModel.DeleteSelectedPasswordFolderCommand.ExecuteAsync(null);

        Assert.DoesNotContain(await harness.Repository.GetCategoriesAsync(), item => item.Id == work.Id);
        var storedPassword = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Null(storedPassword.CategoryId);
        var storedTotp = Assert.Single(await harness.Repository.GetSecureItemsByBoundPasswordIdAsync(password.Id));
        Assert.Null(storedTotp.CategoryId);
        Assert.Null(harness.ViewModel.Passwords.Single().CategoryId);
        Assert.Equal(-1, harness.ViewModel.SelectedPasswordFolderFilter?.Id);
        Assert.False(harness.ViewModel.CanManageSelectedPasswordFolder);
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "DELETE" && item.ItemType == "CATEGORY");
        Assert.Equal(harness.ViewModel.L.Format("DeletedFolderFormat", "Work", 1), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_sorts_password_list_by_selected_display_order()
    {
        var harness = CreateHarness();
        var alpha = new PasswordEntry
        {
            Title = "Alpha",
            Website = "z.example",
            Username = "zoe",
            Password = "one",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        };
        var beta = new PasswordEntry
        {
            Title = "Beta",
            Website = "a.example",
            Username = "amy",
            Password = "two",
            IsFavorite = true,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var gamma = new PasswordEntry
        {
            Title = "Gamma",
            Website = "m.example",
            Username = "max",
            Password = "three",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        };
        await harness.Repository.SavePasswordAsync(alpha);
        await harness.Repository.SavePasswordAsync(beta);
        await harness.Repository.SavePasswordAsync(gamma);
        await harness.ViewModel.LoadAsync();
        await SetPasswordUpdatedAtAsync(harness.DatabasePath, alpha.Id, DateTimeOffset.UtcNow.AddHours(-1));
        await SetPasswordUpdatedAtAsync(harness.DatabasePath, beta.Id, DateTimeOffset.UtcNow.AddHours(-3));
        await SetPasswordUpdatedAtAsync(harness.DatabasePath, gamma.Id, DateTimeOffset.UtcNow.AddHours(-2));
        await harness.ViewModel.LoadAsync();

        Assert.Equal(["Alpha", "Gamma", "Beta"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.SelectedPasswordSort = "title-asc";
        Assert.Equal(["Alpha", "Beta", "Gamma"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.SelectedPasswordSort = "website-asc";
        Assert.Equal(["Beta", "Gamma", "Alpha"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.SelectedPasswordSort = "username-asc";
        Assert.Equal(["Beta", "Gamma", "Alpha"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.SelectedPasswordSort = "favorites-first";
        Assert.Equal(["Beta", "Alpha", "Gamma"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());
    }

    [Fact]
    public async Task ViewModel_exposes_compact_password_list_display_metrics()
    {
        var harness = CreateHarness();
        await harness.ViewModel.LoadAsync();

        Assert.True(harness.ViewModel.ShowPasswordListDetails);
        Assert.Equal(48, harness.ViewModel.PasswordListAvatarSize);
        Assert.Equal(54, harness.ViewModel.PasswordListRowMinHeight);

        harness.ViewModel.CompactPasswordList = true;

        Assert.False(harness.ViewModel.ShowPasswordListDetails);
        Assert.Equal(36, harness.ViewModel.PasswordListAvatarSize);
        Assert.Equal(40, harness.ViewModel.PasswordListRowMinHeight);
        Assert.Equal(new Thickness(12, 8), harness.ViewModel.PasswordListCardPadding);
    }

    [Fact]
    public async Task ViewModel_stacks_selected_passwords_as_manual_sibling_group()
    {
        var harness = CreateHarness();
        var first = new PasswordEntry
        {
            Title = "GitHub",
            Website = "github.com",
            Username = "dev",
            Password = "one"
        };
        var second = new PasswordEntry
        {
            Title = "GitLab",
            Website = "gitlab.com",
            Username = "ops",
            Password = "two"
        };
        await harness.Repository.SavePasswordAsync(first);
        await harness.Repository.SavePasswordAsync(second);
        await harness.ViewModel.LoadAsync();
        var displayedFirst = harness.ViewModel.Passwords.First(item => item.Id == first.Id);
        var displayedSecond = harness.ViewModel.Passwords.First(item => item.Id == second.Id);
        displayedFirst.IsSelected = true;
        displayedSecond.IsSelected = true;

        await harness.ViewModel.StackSelectedPasswordsCommand.ExecuteAsync(null);

        var stacked = (await harness.Repository.GetPasswordsAsync()).OrderBy(item => item.Id).ToArray();
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "STACK" && item.Title == "GitHub");
        Assert.Equal(2, stacked.Length);
        Assert.NotNull(stacked[0].ReplicaGroupId);
        Assert.Equal(stacked[0].ReplicaGroupId, stacked[1].ReplicaGroupId);
        Assert.False(displayedFirst.IsSelected);
        Assert.False(displayedSecond.IsSelected);
        Assert.Equal(harness.ViewModel.L.Format("StackedPasswordCountFormat", 2), harness.ViewModel.StatusMessage);

        harness.Dialog.ConfigureNext(editor =>
        {
            Assert.Equal(["one", "two"], SplitRows(editor.PasswordLines));
            editor.Title = "Git Stack";
            editor.WebsiteLines = "git.example";
            editor.Username = "stack";
            editor.PasswordLines = "one-updated\ntwo-updated";
        });

        await harness.ViewModel.EditPasswordCommand.ExecuteAsync(displayedFirst);

        var edited = (await harness.Repository.GetPasswordsAsync()).OrderBy(item => item.Id).ToArray();
        Assert.Equal("Git Stack", edited[0].Title);
        Assert.Equal("Git Stack", edited[1].Title);
        Assert.Equal(stacked[0].ReplicaGroupId, edited[0].ReplicaGroupId);
        Assert.Equal(stacked[0].ReplicaGroupId, edited[1].ReplicaGroupId);

        await harness.ViewModel.DeletePasswordCommand.ExecuteAsync(harness.ViewModel.Passwords.First(item => item.Id == first.Id));

        Assert.Empty(harness.ViewModel.Passwords);
        Assert.Equal(2, harness.ViewModel.DeletedPasswords.Count);
        Assert.All(await harness.Repository.GetPasswordsAsync(includeDeleted: true), item => Assert.True(item.IsDeleted));
    }

    [Fact]
    public async Task ViewModel_filters_passwords_with_android_style_quick_filters()
    {
        var harness = CreateHarness();
        var category = new Category { Name = "Work" };
        await harness.Repository.SaveCategoryAsync(category);
        var favoriteWith2Fa = new PasswordEntry
        {
            Title = "Favorite 2FA",
            Username = "fav",
            Password = "one",
            IsFavorite = true,
            AuthenticatorKey = "JBSWY3DPEHPK3PXP",
            Notes = "recovery",
            PasskeyBindings = """[{"rpId":"favorite.example"}]""",
            BoundNoteId = 42,
            CategoryId = category.Id
        };
        var uncategorizedLocal = new PasswordEntry
        {
            Title = "Local No Folder",
            Username = "local",
            Password = "two"
        };
        var remoteBitwarden = new PasswordEntry
        {
            Title = "Remote Bitwarden",
            Username = "remote",
            Password = "three",
            BitwardenVaultId = 7,
            BitwardenCipherId = "cipher"
        };
        await harness.Repository.SavePasswordAsync(favoriteWith2Fa);
        await harness.Repository.SavePasswordAsync(uncategorizedLocal);
        await harness.Repository.SavePasswordAsync(remoteBitwarden);
        await harness.ViewModel.LoadAsync();

        harness.ViewModel.QuickFilterFavorite = true;
        Assert.Equal(["Favorite 2FA"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.QuickFilter2Fa = true;
        Assert.Equal(["Favorite 2FA"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.QuickFilterFavorite = false;
        harness.ViewModel.QuickFilter2Fa = false;
        harness.ViewModel.QuickFilterNotes = true;
        Assert.Equal(["Favorite 2FA"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.QuickFilterNotes = false;
        harness.ViewModel.QuickFilterPasskey = true;
        Assert.Equal(["Favorite 2FA"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.QuickFilterPasskey = false;
        harness.ViewModel.QuickFilterBoundNote = true;
        Assert.Equal(["Favorite 2FA"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.QuickFilterBoundNote = false;
        harness.ViewModel.QuickFilterUncategorized = true;
        Assert.Equal(["Local No Folder", "Remote Bitwarden"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).Order().ToArray());

        harness.ViewModel.QuickFilterLocalOnly = true;
        Assert.Equal(["Local No Folder"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.SearchText = "missing";
        Assert.Empty(harness.ViewModel.FilteredPasswords);
    }

    [Fact]
    public async Task ViewModel_filters_searches_and_shows_password_attachments()
    {
        var harness = CreateHarness();
        var withAttachment = new PasswordEntry
        {
            Title = "Passport",
            Website = "travel.example",
            Username = "traveler",
            Password = "one"
        };
        var withoutAttachment = new PasswordEntry
        {
            Title = "No files",
            Username = "plain",
            Password = "two"
        };
        await harness.Repository.SavePasswordAsync(withAttachment);
        await harness.Repository.SavePasswordAsync(withoutAttachment);
        await harness.Repository.SaveAttachmentAsync(new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = withAttachment.Id,
            FileName = "passport-scan.pdf",
            ContentType = "application/pdf",
            StoragePath = "secure_attachments/passport-scan.enc",
            SizeBytes = 4096
        });
        await harness.ViewModel.LoadAsync();

        var displayed = harness.ViewModel.Passwords.Single(item => item.Id == withAttachment.Id);
        Assert.True(displayed.HasAttachments);

        harness.ViewModel.QuickFilterAttachments = true;
        Assert.Equal(["Passport"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        harness.ViewModel.QuickFilterAttachments = false;
        harness.ViewModel.SearchText = "passport-scan";
        Assert.Equal(["Passport"], harness.ViewModel.FilteredPasswords.Select(item => item.Title).ToArray());

        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(displayed);

        var details = Assert.IsType<PasswordDetailViewModel>(harness.DetailDialog.LastDetails);
        var attachmentItem = Assert.Single(details.Attachments, item => item.FileName == "passport-scan.pdf");
        Assert.Contains("4 KB", attachmentItem.DisplayValue, StringComparison.Ordinal);

        await details.CopyAttachmentPathCommand.ExecuteAsync(attachmentItem);

        Assert.Equal("secure_attachments/passport-scan.enc", harness.Clipboard.Text);
    }

    [Fact]
    public async Task ViewModel_adds_password_attachment_metadata_and_logs_timeline_event()
    {
        var harness = CreateHarness();
        var entry = new PasswordEntry { Title = "GitHub", Username = "dev", Password = "one" };
        await harness.Repository.SavePasswordAsync(entry);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.AddPasswordAttachmentMetadataAsync(
            harness.ViewModel.Passwords.Single(),
            "recovery.txt",
            "secure_attachments/recovery.enc",
            128,
            "text/plain");

        var saved = Assert.Single(await harness.Repository.GetAttachmentsAsync("PASSWORD", entry.Id));
        Assert.Equal("recovery.txt", saved.FileName);
        Assert.True(harness.ViewModel.Passwords.Single().HasAttachments);
        Assert.Contains(harness.ViewModel.TimelineEntries, item => item.OperationType == "ATTACHMENT" && item.Title == "GitHub");
        Assert.Equal(harness.ViewModel.L.Format("AddedAttachmentFormat", "recovery.txt", "GitHub"), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task Password_details_manage_attachments_from_dialog()
    {
        var harness = CreateHarness();
        var entry = new PasswordEntry { Title = "GitHub", Username = "dev", Password = "one" };
        await harness.Repository.SavePasswordAsync(entry);
        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = entry.Id,
            FileName = "old.txt",
            ContentType = "text/plain",
            StoragePath = "secure_attachments/old.enc",
            SizeBytes = 32
        };
        await harness.Repository.SaveAttachmentAsync(attachment);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ShowPasswordDetailsCommand.ExecuteAsync(harness.ViewModel.Passwords.Single());
        var details = Assert.IsType<PasswordDetailViewModel>(harness.DetailDialog.LastDetails);
        var displayedAttachment = Assert.Single(details.Attachments);

        await details.CopyAttachmentPathCommand.ExecuteAsync(displayedAttachment);
        Assert.Equal("secure_attachments/old.enc", harness.Clipboard.Text);

        await details.DeleteAttachmentCommand.ExecuteAsync(displayedAttachment);
        Assert.Empty(await harness.Repository.GetAttachmentsAsync("PASSWORD", entry.Id));
        Assert.Empty(details.Attachments);
        Assert.False(details.HasAttachments);
        Assert.False(harness.ViewModel.Passwords.Single().HasAttachments);
        Assert.Equal(harness.ViewModel.L.Format("DeletedAttachmentFormat", "old.txt"), harness.ViewModel.StatusMessage);

        await harness.ViewModel.AddPasswordAttachmentCommand.ExecuteAsync(harness.ViewModel.Passwords.Single());
        var added = Assert.Single(await harness.Repository.GetAttachmentsAsync("PASSWORD", entry.Id));
        Assert.Equal("picked.txt", added.FileName);
        Assert.Equal("secure_attachments/picked.enc", added.StoragePath);
        Assert.True(harness.ViewModel.Passwords.Single().HasAttachments);
    }

    [Fact]
    public async Task Password_details_hide_mdbx_attachment_storage_ids()
    {
        var localization = new LocalizationService();
        var clipboard = new CapturingClipboardService();
        var details = new PasswordDetailViewModel(
            localization,
            clipboard,
            new CryptoService(),
            new TotpService(),
            new PasswordEntry { Id = 42, Title = "MDBX Login", Password = "secret" },
            [],
            category: null,
            boundNote: null,
            attachments:
            [
                new Attachment
                {
                    OwnerType = "PASSWORD",
                    OwnerId = 42,
                    FileName = "recovery.txt",
                    ContentType = "text/plain",
                    StoragePath = "mdbx:attachment-1",
                    SizeBytes = 128
                }
            ],
            customFields: []);

        var attachment = Assert.Single(details.Attachments);

        Assert.False(attachment.CanCopy);
        Assert.DoesNotContain("mdbx:", attachment.DisplayValue, StringComparison.OrdinalIgnoreCase);

        await details.CopyAttachmentPathCommand.ExecuteAsync(attachment);

        Assert.Equal("", clipboard.Text);
    }

    [Fact]
    public async Task ViewModel_analyzes_active_password_security_issues()
    {
        var harness = CreateHarness();
        var oldPasswordDate = DateTimeOffset.UtcNow.AddDays(-370);
        var weak = new PasswordEntry
        {
            Title = "Weak GitHub",
            Website = "https://github.com/login",
            Username = "dev",
            Password = "abc",
            CreatedAt = oldPasswordDate,
            UpdatedAt = oldPasswordDate
        };
        var reusedA = new PasswordEntry
        {
            Title = "Work Portal",
            Website = "https://portal.example.com",
            Username = "one",
            Password = "RepeatedSecret!1234567890"
        };
        var reusedB = new PasswordEntry
        {
            Title = "Second Portal",
            Website = "http://www.portal.example.com/settings",
            Username = "two",
            Password = "RepeatedSecret!1234567890"
        };
        var protectedAccount = new PasswordEntry
        {
            Title = "Protected Microsoft",
            Website = "microsoft.com",
            Username = "safe",
            Password = "UniqueSecret!1234567890",
            AuthenticatorKey = "JBSWY3DPEHPK3PXP"
        };
        var archived = new PasswordEntry
        {
            Title = "Archived Duplicate",
            Website = "portal.example.com",
            Username = "archived",
            Password = "RepeatedSecret!1234567890",
            IsArchived = true,
            ArchivedAt = DateTimeOffset.UtcNow
        };
        var deleted = new PasswordEntry
        {
            Title = "Deleted Weak",
            Website = "github.com",
            Username = "deleted",
            Password = "abc",
            IsDeleted = true,
            DeletedAt = DateTimeOffset.UtcNow
        };

        await harness.Repository.SavePasswordAsync(weak);
        await harness.Repository.SavePasswordAsync(reusedA);
        await harness.Repository.SavePasswordAsync(reusedB);
        await harness.Repository.SavePasswordAsync(protectedAccount);
        await harness.Repository.SavePasswordAsync(archived);
        await harness.Repository.SavePasswordAsync(deleted);
        await SetPasswordUpdatedAtAsync(harness.DatabasePath, weak.Id, oldPasswordDate);
        await harness.ViewModel.LoadAsync();

        Assert.Contains(harness.ViewModel.SecurityIssueItems, item => item.Category == harness.ViewModel.L.WeakPasswords && item.Title == "Weak GitHub");
        Assert.Contains(harness.ViewModel.SecurityIssueItems, item => item.Category == harness.ViewModel.L.DuplicatePasswords && item.Title == "Work Portal");
        Assert.Contains(harness.ViewModel.SecurityIssueItems, item => item.Category == harness.ViewModel.L.DuplicateWebsites && item.Title == "Second Portal");
        Assert.Contains(harness.ViewModel.SecurityIssueItems, item => item.Category == harness.ViewModel.L.MissingTwoFactor && item.Title == "Weak GitHub");
        Assert.Contains(harness.ViewModel.SecurityIssueItems, item => item.Category == harness.ViewModel.L.StalePasswords && item.Title == "Weak GitHub");
        Assert.DoesNotContain(harness.ViewModel.SecurityIssueItems, item => item.Title == "Archived Duplicate" || item.Title == "Deleted Weak");
        Assert.Contains(harness.ViewModel.SecuritySummaryItems, item => item.Label == harness.ViewModel.L.SecurityScore);
    }

    [Fact]
    public async Task ViewModel_checks_compromised_passwords_and_adds_security_issue()
    {
        var pwnedPasswords = new FakePwnedPasswordService(new Dictionary<string, int>
        {
            ["leaked-secret"] = 99
        });
        var harness = CreateHarness(pwnedPasswords);
        harness.Crypto.InitializeSession("target password", new byte[16]);

        await harness.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Leaked Account",
            Website = "https://example.com",
            Username = "dev",
            Password = harness.Crypto.EncryptString("leaked-secret")
        });
        await harness.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Safe Account",
            Website = "https://safe.example.com",
            Username = "dev",
            Password = harness.Crypto.EncryptString("safe-secret")
        });
        await harness.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "Archived Account",
            Password = harness.Crypto.EncryptString("archived-secret"),
            IsArchived = true,
            ArchivedAt = DateTimeOffset.UtcNow
        });

        await harness.ViewModel.LoadAsync();
        await harness.ViewModel.CheckCompromisedPasswordsCommand.ExecuteAsync(null);

        Assert.False(harness.ViewModel.IsCheckingCompromisedPasswords);
        Assert.Contains(harness.ViewModel.SecurityIssueItems, item =>
            item.Category == harness.ViewModel.L.CompromisedPasswords &&
            item.Title == "Leaked Account" &&
            item.Subtitle.Contains("99", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.ViewModel.SecurityIssueItems, item =>
            item.Category == harness.ViewModel.L.CompromisedPasswords &&
            (item.Title == "Safe Account" || item.Title == "Archived Account"));
        Assert.Contains(harness.ViewModel.SecuritySummaryItems, item => item.Label == harness.ViewModel.L.CompromisedPasswords && item.Value == "1");
        Assert.Equal(["leaked-secret", "safe-secret"], pwnedPasswords.CheckedPasswords.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(harness.ViewModel.L.Format("CompromisedPasswordCheckCompleteFormat", 2, 1), harness.ViewModel.CompromisedPasswordStatus);
    }

    [Fact]
    public async Task ViewModel_does_not_save_when_editor_is_cancelled()
    {
        var harness = CreateHarness();
        harness.Dialog.CancelNext();

        await harness.ViewModel.AddPasswordCommand.ExecuteAsync(null);

        Assert.Empty(await harness.Repository.GetPasswordsAsync());
        Assert.Empty(harness.ViewModel.Passwords);
    }

    [Fact]
    public async Task ViewModel_generates_configurable_password_and_copies_it()
    {
        var harness = CreateHarness();
        harness.ViewModel.GeneratorLength = 18;
        harness.ViewModel.GeneratorIncludeUppercase = false;
        harness.ViewModel.GeneratorIncludeLowercase = true;
        harness.ViewModel.GeneratorIncludeNumbers = true;
        harness.ViewModel.GeneratorIncludeSymbols = false;

        harness.ViewModel.GeneratePasswordCommand.Execute(null);
        await harness.ViewModel.CopyGeneratedPasswordCommand.ExecuteAsync(null);

        Assert.Equal(18, harness.ViewModel.GeneratedPassword.Length);
        Assert.DoesNotContain(harness.ViewModel.GeneratedPassword, char.IsUpper);
        Assert.DoesNotContain(harness.ViewModel.GeneratedPassword, c => !char.IsLetterOrDigit(c));
        Assert.Contains(harness.ViewModel.GeneratedPassword, char.IsDigit);
        Assert.Equal(harness.ViewModel.GeneratedPassword, harness.Clipboard.Text);
        Assert.Contains("5", harness.ViewModel.GeneratedPasswordStrengthText);
        Assert.True(harness.ViewModel.HasGeneratedPasswordHistory);
        Assert.Equal(harness.ViewModel.GeneratedPassword, harness.ViewModel.GeneratedPasswordHistory.First().Value);
    }

    [Fact]
    public void ViewModel_generator_supports_templates_modes_and_history_reuse()
    {
        var harness = CreateHarness();
        const string similarCharacters = "0OolI1|`";

        harness.ViewModel.GeneratorExcludeSimilarCharacters = true;
        harness.ViewModel.GeneratorLength = 64;
        harness.ViewModel.GeneratePasswordCommand.Execute(null);

        Assert.DoesNotContain(harness.ViewModel.GeneratedPassword, similarCharacters.Contains);

        harness.ViewModel.GeneratorTemplate = "pin";
        harness.ViewModel.GeneratePasswordCommand.Execute(null);

        Assert.Equal(6, harness.ViewModel.GeneratedPassword.Length);
        Assert.All(harness.ViewModel.GeneratedPassword, character => Assert.True(char.IsDigit(character)));

        harness.ViewModel.GeneratorTemplate = "memorable";
        harness.ViewModel.GeneratorWordCount = 5;
        harness.ViewModel.GeneratePasswordCommand.Execute(null);

        Assert.Equal("passphrase", harness.ViewModel.GeneratorMode);
        Assert.True(harness.ViewModel.GeneratedPassword.Count(character => character == '-') >= 4);

        var historyItem = harness.ViewModel.GeneratedPasswordHistory.Last();
        harness.ViewModel.UseGeneratedPasswordHistoryItemCommand.Execute(historyItem);

        Assert.Equal(historyItem.Value, harness.ViewModel.GeneratedPassword);
        Assert.True(harness.ViewModel.GeneratedPasswordHistory.Count <= 8);
    }

    [Fact]
    public async Task ViewModel_imports_monica_json_and_rebinds_totp_to_new_password_ids()
    {
        var source = CreateHarness();
        source.Crypto.InitializeSession("source password", new byte[16]);
        var entry = new PasswordEntry
        {
            Title = "Imported GitHub",
            Website = "github.com",
            Username = "dev",
            Password = source.Crypto.EncryptString("plain-import-secret"),
            AuthenticatorKey = "JBSWY3DPEHPK3PXP"
        };
        await source.Repository.SavePasswordAsync(entry);
        await source.Repository.ReplaceCustomFieldsAsync(entry.Id,
        [
            new CustomField { Title = "Recovery hint", Value = "blue", IsProtected = true }
        ]);
        await source.Repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = entry.Id,
            Password = source.Crypto.EncryptString("old-import-secret"),
            LastUsedAt = DateTimeOffset.UnixEpoch.AddDays(5)
        });
        await source.Repository.SaveSecureItemAsync(new SecureItem
        {
            ItemType = VaultItemType.Totp,
            Title = "Imported GitHub",
            BoundPasswordId = entry.Id,
            ItemData = """{"secret":"JBSWY3DPEHPK3PXP","period":30,"digits":6,"otpType":"TOTP"}"""
        });
        await source.ViewModel.LoadAsync();
        await source.ViewModel.AddPasswordAttachmentCommand.ExecuteAsync(source.ViewModel.Passwords.Single());
        await source.ViewModel.ExportDataCommand.ExecuteAsync(null);

        var target = CreateHarness();
        target.Crypto.InitializeSession("target password", new byte[16]);
        target.ViewModel.ImportJsonText = source.ViewModel.ExportPreview;
        await target.ViewModel.ImportDataCommand.ExecuteAsync(null);

        var imported = Assert.Single(await target.Repository.GetPasswordsAsync());
        Assert.Equal("Imported GitHub", imported.Title);
        Assert.NotEqual("plain-import-secret", imported.Password);
        Assert.Equal("plain-import-secret", target.Crypto.DecryptString(imported.Password));

        var importedTotp = Assert.Single(await target.Repository.GetSecureItemsByBoundPasswordIdAsync(imported.Id));
        Assert.Equal(imported.Id, importedTotp.BoundPasswordId);
        var importedField = Assert.Single(await target.Repository.GetCustomFieldsAsync(imported.Id));
        Assert.Equal("Recovery hint", importedField.Title);
        Assert.Equal("blue", importedField.Value);
        Assert.True(importedField.IsProtected);
        var importedHistory = Assert.Single(await target.Repository.GetPasswordHistoryAsync(imported.Id));
        Assert.NotEqual("old-import-secret", importedHistory.Password);
        Assert.Equal("old-import-secret", target.Crypto.DecryptString(importedHistory.Password));
        var importedAttachment = Assert.Single(await target.Repository.GetAttachmentsAsync("PASSWORD", imported.Id));
        Assert.Equal("picked.txt", importedAttachment.FileName);
        Assert.Equal("picked attachment"u8.ToArray(), await target.Repository.TryReadAttachmentContentAsync(importedAttachment));
        Assert.Single(target.ViewModel.Passwords);
        Assert.Single(target.ViewModel.TotpItems, item => item.BoundPasswordId == imported.Id);
        Assert.Empty(target.ViewModel.ImportJsonText);
        Assert.Equal(target.ViewModel.L.Format("ImportedMonicaJsonFormat", 1, 1), target.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_imports_monica_json_secure_item_images()
    {
        var source = CreateHarness();
        source.Crypto.InitializeSession("source password", new byte[16]);
        var sourceImageContent = "document image bytes"u8.ToArray();
        source.Attachments.Put("secure_attachments/document-front.png", sourceImageContent);
        var document = new SecureItem
        {
            ItemType = VaultItemType.Document,
            Title = "Passport",
            ItemData = WalletItemDataCodec.EncodeDocument(new DocumentWalletData
            {
                DocumentNumber = "P-123",
                ImagePaths = ["secure_attachments/document-front.png"]
            }),
            ImagePaths = WalletItemDataCodec.EncodeImagePaths(["secure_attachments/document-front.png"])
        };
        await source.Repository.SaveSecureItemAsync(document);
        await source.ViewModel.LoadAsync();
        await source.ViewModel.ExportDataCommand.ExecuteAsync(null);

        var target = CreateHarness();
        target.ViewModel.ImportJsonText = source.ViewModel.ExportPreview;
        await target.ViewModel.ImportDataCommand.ExecuteAsync(null);

        var imported = Assert.Single(await target.Repository.GetSecureItemsAsync(VaultItemType.Document));
        var imagePath = Assert.Single(WalletItemDataCodec.DecodeDocument(imported).ImagePaths);
        Assert.StartsWith("secure_attachments/imported-", imagePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sourceImageContent, target.Attachments.TryRead(imagePath));
        Assert.Equal(target.ViewModel.L.Format("ImportedMonicaJsonFormat", 0, 1), target.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_strips_local_mdbx_bindings_from_monica_json_export()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("source password", new byte[16]);
        var category = new Category
        {
            Name = "Work",
            MdbxDatabaseId = 7,
            MdbxFolderId = "project-1"
        };
        await harness.Repository.SaveCategoryAsync(category);
        var password = new PasswordEntry
        {
            Title = "MDBX login",
            Password = "secret",
            CategoryId = category.Id,
            MdbxDatabaseId = 7,
            MdbxFolderId = "entry-1"
        };
        var secureItem = new SecureItem
        {
            ItemType = VaultItemType.Note,
            Title = "MDBX note",
            CategoryId = category.Id,
            MdbxDatabaseId = 7,
            MdbxFolderId = "entry-2"
        };
        await harness.Repository.SavePasswordAsync(password);
        await harness.Repository.SaveSecureItemAsync(secureItem);
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ExportDataCommand.ExecuteAsync(null);

        var package = new ImportExportService().ImportJson(harness.ViewModel.ExportPreview);
        Assert.Null(Assert.Single(package.Passwords).MdbxDatabaseId);
        Assert.Null(Assert.Single(package.Passwords).MdbxFolderId);
        Assert.Null(Assert.Single(package.SecureItems).MdbxDatabaseId);
        Assert.Null(Assert.Single(package.SecureItems).MdbxFolderId);
        Assert.Null(Assert.Single(package.Categories).MdbxDatabaseId);
        Assert.Null(Assert.Single(package.Categories).MdbxFolderId);
    }

    [Fact]
    public async Task ViewModel_imports_password_csv_and_encrypts_passwords()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("target password", new byte[16]);
        harness.ViewModel.ImportCsvText = "title,website,username,password,notes,authenticatorKey,loginType,wifiMetadata,sshKeyData\r\n"
            + "\"Wi-Fi Lab\",https://example.com,dev,\"plain,csv-secret\",\"line 1\nline 2\",JBSWY3DPEHPK3PXP,Wifi,\"{\"\"ssid\"\":\"\"Lab\"\"}\",ssh-ed25519 AAAA";

        await harness.ViewModel.ImportPasswordCsvCommand.ExecuteAsync(null);

        var imported = Assert.Single(await harness.Repository.GetPasswordsAsync());
        Assert.Equal("Wi-Fi Lab", imported.Title);
        Assert.NotEqual("plain,csv-secret", imported.Password);
        Assert.Equal("plain,csv-secret", harness.Crypto.DecryptString(imported.Password));
        Assert.Equal(PasswordLoginType.Wifi, imported.LoginType);
        Assert.Equal("""{"ssid":"Lab"}""", imported.WifiMetadata);
        Assert.Equal("ssh-ed25519 AAAA", imported.SshKeyData);
        Assert.Single(harness.ViewModel.Passwords);
        Assert.Empty(harness.ViewModel.ImportCsvText);
        Assert.Equal(harness.ViewModel.L.Format("ImportedPasswordCsvFormat", 1), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_clears_vault_data_only_after_danger_confirmation()
    {
        var harness = CreateHarness();
        harness.ViewModel.IsUnlocked = true;
        var password = new PasswordEntry { Title = "Portal", Password = "one" };
        await harness.Repository.SavePasswordAsync(password);
        await harness.Repository.SaveSecureItemAsync(new SecureItem { ItemType = VaultItemType.Note, Title = "Recovery" });
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ClearVaultDataCommand.ExecuteAsync("Passwords");

        Assert.Single(harness.ViewModel.Passwords);
        Assert.Equal(
            harness.ViewModel.L.Format("ClearVaultConfirmationFailedFormat", harness.ViewModel.L.Get("ClearVaultConfirmationPhrase")),
            harness.ViewModel.StatusMessage);

        harness.ViewModel.DangerZoneConfirmationText = harness.ViewModel.L.Get("ClearVaultConfirmationPhrase");
        await harness.ViewModel.ClearVaultDataCommand.ExecuteAsync("Passwords");

        Assert.Empty(harness.ViewModel.Passwords);
        Assert.Single(harness.ViewModel.NoteItems);
        Assert.Empty(await harness.Repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true));
        Assert.Equal("", harness.ViewModel.DangerZoneConfirmationText);
    }

    [Fact]
    public async Task ViewModel_exports_password_csv_with_plaintext_when_unlocked()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("source password", new byte[16]);
        await harness.Repository.SavePasswordAsync(new PasswordEntry
        {
            Title = "GitHub",
            Website = "https://github.com",
            Username = "dev",
            Password = harness.Crypto.EncryptString("plain-export-secret"),
            AuthenticatorKey = "JBSWY3DPEHPK3PXP"
        });
        await harness.ViewModel.LoadAsync();

        await harness.ViewModel.ExportPasswordCsvCommand.ExecuteAsync(null);

        Assert.Contains("plain-export-secret", harness.ViewModel.ExportCsvPreview);
        Assert.Contains("JBSWY3DPEHPK3PXP", harness.ViewModel.ExportCsvPreview);
        Assert.Equal(harness.ViewModel.L.Get("ExportedPasswordCsv"), harness.ViewModel.StatusMessage);
    }

    [Fact]
    public async Task ViewModel_exports_from_repository_snapshot()
    {
        var harness = CreateHarness();
        harness.Crypto.InitializeSession("source password", new byte[16]);
        var entry = new PasswordEntry
        {
            Title = "Cached Export",
            Website = "https://cached.example.com",
            Username = "dev",
            Password = harness.Crypto.EncryptString("cached-export-secret")
        };
        await harness.Repository.SavePasswordAsync(entry);
        await harness.ViewModel.LoadAsync();

        entry.Title = "Fresh Export";
        entry.Website = "https://fresh.example.com";
        entry.Password = harness.Crypto.EncryptString("fresh-export-secret");
        await harness.Repository.SavePasswordAsync(entry);

        await harness.ViewModel.ExportDataCommand.ExecuteAsync(null);
        await harness.ViewModel.ExportPasswordCsvCommand.ExecuteAsync(null);

        Assert.Contains("Fresh Export", harness.ViewModel.ExportPreview);
        Assert.Contains("fresh-export-secret", harness.ViewModel.ExportPreview);
        Assert.DoesNotContain("Cached Export", harness.ViewModel.ExportPreview);
        Assert.DoesNotContain("cached-export-secret", harness.ViewModel.ExportPreview);
        Assert.Contains("https://fresh.example.com", harness.ViewModel.ExportCsvPreview);
        Assert.Contains("fresh-export-secret", harness.ViewModel.ExportCsvPreview);
        Assert.DoesNotContain("https://cached.example.com", harness.ViewModel.ExportCsvPreview);
        Assert.DoesNotContain("cached-export-secret", harness.ViewModel.ExportCsvPreview);
    }

    private static PasswordHarness CreateHarness(
        IPwnedPasswordService? pwnedPasswordService = null,
        IWebDavBackupService? webDavBackupService = null,
        IConfirmationDialogService? confirmationDialogService = null)
    {
        var databasePath = GetTempDatabasePath();
        var factory = new SqliteConnectionFactory(databasePath);
        var migrator = new DatabaseMigrator(factory);
        var attachmentFileService = new FakePasswordAttachmentFileService();
        var repository = new MonicaRepository(factory, migrator, attachmentContentStore: attachmentFileService);
        var crypto = new CryptoService();
        var localization = new LocalizationService();
        var generator = new PasswordGeneratorService();
        var dialog = new FakePasswordEditorDialogService(localization, generator);
        var clipboard = new CapturingClipboardService();
        var detailDialog = new FakePasswordDetailDialogService(localization, clipboard, crypto, new TotpService());
        var categoryPicker = new FakeCategoryPickerDialogService();
        var totpDialog = new FakeTotpEditorDialogService(localization);
        var walletDialog = new FakeWalletItemEditorDialogService(localization);
        var viewModel = new MainWindowViewModel(
            repository,
            new VaultCredentialStore(factory, migrator),
            crypto,
            new TotpService(),
            generator,
            new ImportExportService(),
            new PlatformCapabilityService(),
            new PlatformIntegrationService(),
            clipboard,
            webDavBackupService ?? new FakeWebDavBackupService(),
            new MdbxVaultService(),
            attachmentFileService,
            dialog,
            detailDialog,
            categoryPicker,
            new LegacyVaultDetector(factory),
            new AppSettingsService(GetTempSettingsPath()),
            localization,
            pwnedPasswordService: pwnedPasswordService,
            confirmationDialogService: confirmationDialogService,
            totpEditorDialogService: totpDialog,
            walletItemEditorDialogService: walletDialog);

        return new PasswordHarness(viewModel, repository, crypto, dialog, detailDialog, categoryPicker, totpDialog, walletDialog, clipboard, attachmentFileService, confirmationDialogService, databasePath);
    }

    private static async Task SetPasswordUpdatedAtAsync(string databasePath, long id, DateTimeOffset updatedAt)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE password_entries SET updated_at = $updatedAt WHERE id = $id";
        command.Parameters.AddWithValue("$updatedAt", updatedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static string GetTempDatabasePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string GetTempSettingsPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "monica-tests", $"{Guid.NewGuid():N}.settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string[] SplitRows(string value)
    {
        return value.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries);
    }

    private static void WaitForCondition(Func<bool> predicate, int timeoutMilliseconds = 2000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return;
            }

            Thread.Sleep(20);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(predicate(), "Condition was not satisfied before timeout.");
    }

    private static void RunOnStaThread(Action action) => SharedStaTestThread.Run(action);

    private static readonly StaTestThread SharedStaTestThread = new();

    private sealed class StaTestThread
    {
        private readonly BlockingCollection<StaTestWorkItem> _workItems = [];
        private readonly Thread _thread;

        public StaTestThread()
        {
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "Monica password tests STA"
            };
            if (OperatingSystem.IsWindows())
            {
                _thread.SetApartmentState(ApartmentState.STA);
            }

            _thread.Start();
        }

        public void Run(Action action)
        {
            var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _workItems.Add(new StaTestWorkItem(action, completion));
            Assert.True(completion.Task.Wait(TimeSpan.FromSeconds(10)), "STA test thread timed out.");
            completion.Task.GetAwaiter().GetResult();
        }

        private void RunLoop()
        {
            _ = Dispatcher.UIThread;
            foreach (var workItem in _workItems.GetConsumingEnumerable())
            {
                try
                {
                    workItem.Action();
                    workItem.Completion.SetResult(null);
                }
                catch (Exception ex)
                {
                    workItem.Completion.SetException(ex);
                }
            }
        }
    }

    private sealed record StaTestWorkItem(Action Action, TaskCompletionSource<object?> Completion);

    private sealed record PasswordHarness(
        MainWindowViewModel ViewModel,
        IMonicaRepository Repository,
        ICryptoService Crypto,
        FakePasswordEditorDialogService Dialog,
        FakePasswordDetailDialogService DetailDialog,
        FakeCategoryPickerDialogService CategoryPicker,
        FakeTotpEditorDialogService TotpDialog,
        FakeWalletItemEditorDialogService WalletDialog,
        CapturingClipboardService Clipboard,
        FakePasswordAttachmentFileService Attachments,
        IConfirmationDialogService? ConfirmationDialog,
        string DatabasePath);

    private sealed class CapturingClipboardService : IClipboardService
    {
        public string Text { get; private set; } = "";

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePwnedPasswordService(IReadOnlyDictionary<string, int> counts) : IPwnedPasswordService
    {
        public IReadOnlyList<string> CheckedPasswords { get; private set; } = [];

        public Task<IReadOnlyDictionary<string, int>> CheckPasswordsAsync(IEnumerable<string> plaintextPasswords, CancellationToken cancellationToken = default)
        {
            CheckedPasswords = plaintextPasswords.ToArray();
            IReadOnlyDictionary<string, int> results = CheckedPasswords
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    password => password,
                    password => counts.TryGetValue(password, out var count) ? count : 0,
                    StringComparer.Ordinal);
            return Task.FromResult(results);
        }
    }

    private sealed class FakePasswordAttachmentFileService : IPasswordAttachmentFileService, IAttachmentContentStore
    {
        private readonly Dictionary<string, byte[]> _content = new(StringComparer.OrdinalIgnoreCase);
        private int _nextAttachmentId;

        public void Put(string storagePath, byte[] content)
        {
            _content[storagePath] = content.ToArray();
        }

        public byte[]? TryRead(string storagePath)
        {
            return _content.TryGetValue(storagePath, out var content) ? content.ToArray() : null;
        }

        public Task<PasswordAttachmentFileDraft?> PickAndStoreAttachmentAsync(PasswordEntry entry, CancellationToken cancellationToken = default)
        {
            var content = "picked attachment"u8.ToArray();
            _content["secure_attachments/picked.enc"] = content;
            return Task.FromResult<PasswordAttachmentFileDraft?>(new PasswordAttachmentFileDraft(
                "picked.txt",
                "secure_attachments/picked.enc",
                content.LongLength,
                "text/plain",
                content));
        }

        public Task<PasswordAttachmentFileDraft> StoreAttachmentAsync(string fileName, byte[] content, string contentType = "", CancellationToken cancellationToken = default)
        {
            var storagePath = $"secure_attachments/imported-{++_nextAttachmentId}.enc";
            _content[storagePath] = content.ToArray();
            return Task.FromResult(new PasswordAttachmentFileDraft(
                fileName,
                storagePath,
                content.LongLength,
                contentType,
                content));
        }

        public Task DeleteStoredAttachmentAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            _content.Remove(storagePath);
            return Task.CompletedTask;
        }

        public Task<byte[]?> TryReadAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _content.TryGetValue(attachment.StoragePath, out var content)
                    ? content.ToArray()
                    : null);
        }

        public Task DeleteAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default) =>
            DeleteStoredAttachmentAsync(attachment.StoragePath, cancellationToken);
    }

    private sealed class FakeWebDavBackupService : IWebDavBackupService
    {
        public string UploadedPath { get; private set; } = "";
        public string UploadedContent { get; private set; } = "";
        public string DownloadContent { get; init; } = "";
        public List<string> DeletedPaths { get; } = [];

        public string NormalizeRemotePath(string rootPath, string relativePath)
        {
            var root = string.IsNullOrWhiteSpace(rootPath) ? "/" : rootPath.Trim();
            root = "/" + root.Trim('/');
            var relative = string.IsNullOrWhiteSpace(relativePath) ? "" : relativePath.Trim('/');
            return string.IsNullOrEmpty(relative) ? root : $"{root}/{relative}";
        }

        public Task<IReadOnlyList<RemoteFileEntry>> ListAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteFileEntry>>(
                string.IsNullOrEmpty(UploadedPath)
                    ? []
                    : [new RemoteFileEntry(NormalizeRemotePath(profile.RootPath, UploadedPath), false, UploadedContent.Length, DateTimeOffset.UtcNow)]);

        public Task UploadTextAsync(WebDavProfile profile, string relativePath, string content, CancellationToken cancellationToken = default)
        {
            UploadedPath = relativePath;
            UploadedContent = content;
            return Task.CompletedTask;
        }

        public Task<string> DownloadTextAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.IsNullOrEmpty(DownloadContent) ? UploadedContent : DownloadContent);

        public Task DeleteAsync(WebDavProfile profile, string relativePath, CancellationToken cancellationToken = default)
        {
            DeletedPaths.Add(relativePath);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConfirmationDialogService(bool result = true) : IConfirmationDialogService
    {
        public List<(string Title, string Message, string PrimaryButtonText)> Requests { get; } = [];
        public List<(string Title, string RequiredPhrase)> TypedRequests { get; } = [];

        public bool Result { get; set; } = result;

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((title, message, primaryButtonText));
            return Task.FromResult(Result);
        }

        public Task<bool> ConfirmTypedAsync(
            string title,
            string message,
            string requiredPhrase,
            string instruction,
            string primaryButtonText,
            string? closeButtonText = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((title, $"{message}\n{instruction}", primaryButtonText));
            TypedRequests.Add((title, requiredPhrase));
            return Task.FromResult(Result);
        }
    }

    private sealed class FakePasswordEditorDialogService(
        ILocalizationService localization,
        IPasswordGeneratorService passwordGenerator) : IPasswordEditorDialogService
    {
        private Action<PasswordEditorViewModel>? _configureNext;
        private bool _cancelNext;

        public void ConfigureNext(Action<PasswordEditorViewModel> configure)
        {
            _cancelNext = false;
            _configureNext = configure;
        }

        public void CancelNext()
        {
            _cancelNext = true;
            _configureNext = null;
        }

        public Task<PasswordEditorViewModel?> ShowAsync(
            PasswordEntry? entry,
            IReadOnlyList<Category> categories,
            string plainPassword,
            IReadOnlyList<string>? siblingPasswords = null,
            IReadOnlyList<SecureItem>? notes = null,
            IReadOnlyList<CustomField>? customFields = null,
            CancellationToken cancellationToken = default)
        {
            if (_cancelNext)
            {
                _cancelNext = false;
                return Task.FromResult<PasswordEditorViewModel?>(null);
            }

            var editor = new PasswordEditorViewModel(localization, passwordGenerator, entry, categories, plainPassword, siblingPasswords, notes, customFields);
            _configureNext?.Invoke(editor);
            _configureNext = null;
            return Task.FromResult<PasswordEditorViewModel?>(editor.Validate() ? editor : null);
        }
    }

    private sealed class FakeTotpEditorDialogService(ILocalizationService localization) : ITotpEditorDialogService
    {
        private Action<TotpEditorViewModel>? _configureNext;
        private bool _cancelNext;

        public void ConfigureNext(Action<TotpEditorViewModel> configure)
        {
            _cancelNext = false;
            _configureNext = configure;
        }

        public void CancelNext()
        {
            _cancelNext = true;
            _configureNext = null;
        }

        public Task<TotpEditorViewModel?> ShowAsync(SecureItem? item, CancellationToken cancellationToken = default)
        {
            if (_cancelNext)
            {
                _cancelNext = false;
                return Task.FromResult<TotpEditorViewModel?>(null);
            }

            var editor = new TotpEditorViewModel(localization, item);
            _configureNext?.Invoke(editor);
            _configureNext = null;
            return Task.FromResult<TotpEditorViewModel?>(editor.Validate() ? editor : null);
        }
    }

    private sealed class FakeWalletItemEditorDialogService(ILocalizationService localization) : IWalletItemEditorDialogService
    {
        private Action<WalletItemEditorViewModel>? _configureNext;
        private bool _cancelNext;

        public void ConfigureNext(Action<WalletItemEditorViewModel> configure)
        {
            _cancelNext = false;
            _configureNext = configure;
        }

        public void CancelNext()
        {
            _cancelNext = true;
            _configureNext = null;
        }

        public Task<WalletItemEditorViewModel?> ShowAsync(SecureItem? item, VaultItemType? newItemType = null, CancellationToken cancellationToken = default)
        {
            if (_cancelNext)
            {
                _cancelNext = false;
                return Task.FromResult<WalletItemEditorViewModel?>(null);
            }

            var editor = new WalletItemEditorViewModel(localization, item, newItemType);
            _configureNext?.Invoke(editor);
            _configureNext = null;
            return Task.FromResult<WalletItemEditorViewModel?>(editor.Validate() ? editor : null);
        }
    }

    private sealed class FakePasswordDetailDialogService(
        ILocalizationService localization,
        IClipboardService clipboardService,
        ICryptoService cryptoService,
        ITotpService totpService) : IPasswordDetailDialogService
    {
        public PasswordDetailViewModel? LastDetails { get; private set; }
        public IReadOnlyList<PasswordEntry> LastSiblings { get; private set; } = [];
        public Category? LastCategory { get; private set; }
        public SecureItem? LastBoundNote { get; private set; }
        public IReadOnlyList<Attachment> LastAttachments { get; private set; } = [];
        public IReadOnlyList<CustomField> LastCustomFields { get; private set; } = [];
        public IReadOnlyList<PasswordHistoryDisplayItem> LastPasswordHistory { get; private set; } = [];

        public Task ShowAsync(
            PasswordEntry entry,
            IReadOnlyList<PasswordEntry> siblings,
            Category? category,
            SecureItem? boundNote,
            IReadOnlyList<Attachment> attachments,
            IReadOnlyList<CustomField> customFields,
            IReadOnlyList<PasswordHistoryDisplayItem> passwordHistory,
            Func<PasswordEntry, Task>? addAttachment,
            Func<Attachment, Task<bool>>? deleteAttachment,
            Func<PasswordHistoryEntry, Task<bool>>? deletePasswordHistory,
            Func<long, Task<bool>>? clearPasswordHistory,
            CancellationToken cancellationToken = default)
        {
            LastSiblings = siblings;
            LastCategory = category;
            LastBoundNote = boundNote;
            LastAttachments = attachments;
            LastCustomFields = customFields;
            LastPasswordHistory = passwordHistory;
            LastDetails = new PasswordDetailViewModel(
                localization,
                clipboardService,
                cryptoService,
                totpService,
                entry,
                siblings,
                category,
                boundNote,
                attachments,
                customFields,
                passwordHistory,
                addAttachment,
                deleteAttachment,
                deletePasswordHistory,
                clearPasswordHistory);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCategoryPickerDialogService : ICategoryPickerDialogService
    {
        private PasswordCategoryChoice? _nextChoice;
        public IReadOnlyList<Category> LastCategories { get; private set; } = [];
        public long? LastSelectedCategoryId { get; private set; }

        public void SelectNext(long? id, string name)
        {
            _nextChoice = new PasswordCategoryChoice(id, name);
        }

        public void CancelNext()
        {
            _nextChoice = null;
        }

        public Task<PasswordCategoryChoice?> ShowAsync(
            IReadOnlyList<Category> categories,
            long? selectedCategoryId = null,
            CancellationToken cancellationToken = default)
        {
            LastCategories = categories;
            LastSelectedCategoryId = selectedCategoryId;
            return Task.FromResult(_nextChoice);
        }
    }
}
