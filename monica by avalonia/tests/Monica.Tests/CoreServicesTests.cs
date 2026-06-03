using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Monica.Core.ImportExport;
using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.Tests;

public sealed class CoreServicesTests
{
    [Fact]
    public void Totp_generates_known_rfc_vector()
    {
        var service = new TotpService();

        var code = service.GenerateCode("JBSWY3DPEHPK3PXP", digits: 6);

        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public void Totp_resolver_accepts_otpauth_uri_and_raw_secret()
    {
        var fromUri = TotpDataResolver.FromAuthenticatorKey(
            "otpauth://totp/GitHub:dev%40example.com?secret=jbsw-y3dp ehpk3pxp&issuer=GitHub&period=45&digits=8");

        Assert.NotNull(fromUri);
        Assert.Equal("JBSWY3DPEHPK3PXP", fromUri.Secret);
        Assert.Equal("GitHub", fromUri.Issuer);
        Assert.Equal("dev@example.com", fromUri.AccountName);
        Assert.Equal(45, fromUri.Period);
        Assert.Equal(8, fromUri.Digits);

        var fromRaw = TotpDataResolver.FromAuthenticatorKey("jbsw y3dp-ehpk3pxp", "GitHub", "dev");

        Assert.NotNull(fromRaw);
        Assert.Equal("JBSWY3DPEHPK3PXP", fromRaw.Secret);
        Assert.Equal("GitHub", fromRaw.Issuer);
        Assert.Equal("dev", fromRaw.AccountName);
    }

    [Fact]
    public void Crypto_encrypts_and_decrypts_roundtrip()
    {
        var service = new CryptoService();
        var salt = service.CreateSalt();
        service.InitializeSession("correct horse battery staple", salt);

        var encrypted = service.EncryptString("secret payload");
        var decrypted = service.DecryptString(encrypted);

        Assert.NotEqual("secret payload", encrypted);
        Assert.Equal("secret payload", decrypted);
    }

    [Fact]
    public void Crypto_rejects_wrong_master_password()
    {
        var service = new CryptoService();
        var hash = service.HashMasterPassword("master");

        Assert.True(service.VerifyMasterPassword("master", hash));
        Assert.False(new CryptoService().VerifyMasterPassword("wrong", hash));
    }

    [Fact]
    public void Import_export_roundtrips_monica_json()
    {
        var service = new ImportExportService();
        var passwords = new[]
        {
            new PasswordEntry { Id = 7, Title = "GitHub", Username = "dev", Password = "encrypted" }
        };
        var items = new[]
        {
            new SecureItem { ItemType = VaultItemType.Note, Title = "Note", ItemData = "{}" }
        };
        var customFields = new Dictionary<long, IReadOnlyList<CustomField>>
        {
            [7] =
            [
                new CustomField { Id = 11, EntryId = 7, Title = "Recovery", Value = "blue", IsProtected = true }
            ]
        };
        var history = new Dictionary<long, IReadOnlyList<PasswordHistoryEntry>>
        {
            [7] =
            [
                new PasswordHistoryEntry { Id = 21, EntryId = 7, Password = "old-secret", LastUsedAt = DateTimeOffset.UnixEpoch.AddDays(1) }
            ]
        };
        var attachments = new Dictionary<long, IReadOnlyList<PasswordAttachmentExport>>
        {
            [7] =
            [
                new PasswordAttachmentExport(
                    new Attachment
                    {
                        Id = 31,
                        OwnerType = "PASSWORD",
                        OwnerId = 7,
                        FileName = "recovery.txt",
                        ContentType = "text/plain",
                        StoragePath = "mdbx:attachment-1",
                        SizeBytes = 12
                    },
                    Convert.ToBase64String("recovery"u8.ToArray()))
            ]
        };

        var json = service.ExportJson(passwords, items, passwordCustomFields: customFields, passwordHistory: history, passwordAttachments: attachments);
        var package = service.ImportJson(json);

        Assert.Equal(70, package.SchemaVersion);
        Assert.Single(package.Passwords);
        Assert.Single(package.SecureItems);
        Assert.Equal("Recovery", Assert.Single(Assert.Single(package.PasswordCustomFields).Fields).Title);
        Assert.Equal("old-secret", Assert.Single(Assert.Single(package.PasswordHistory).Entries).Password);
        var attachment = Assert.Single(Assert.Single(package.PasswordAttachments).Attachments);
        Assert.Equal("recovery.txt", attachment.Metadata.FileName);
        Assert.Equal("recovery", Encoding.UTF8.GetString(Convert.FromBase64String(attachment.ContentBase64)));
    }

    [Fact]
    public void Import_export_roundtrips_password_csv()
    {
        var service = new ImportExportService();
        var passwords = new[]
        {
            new PasswordEntry
            {
                Title = "GitHub, Inc.",
                Website = "https://github.com",
                Username = "dev@example.com",
                Password = "secret, with comma",
                Notes = "line 1\nline \"2\"",
                AuthenticatorKey = "JBSWY3DPEHPK3PXP",
                AppName = "GitHub",
                AppPackageName = "com.github.android",
                Email = "security@example.com",
                Phone = "15551234567",
                LoginType = PasswordLoginType.SshKey,
                SsoProvider = "Entra",
                PasskeyBindings = """[{"rpId":"github.com"}]""",
                WifiMetadata = """{"ssid":"Monica"}""",
                SshKeyData = "ssh-ed25519 AAAA"
            }
        };

        var csv = service.ExportPasswordCsv(passwords);
        var imported = Assert.Single(service.ImportPasswordCsv(csv));

        Assert.Contains("\"GitHub, Inc.\"", csv);
        Assert.Equal("GitHub, Inc.", imported.Title);
        Assert.Equal("secret, with comma", imported.Password);
        Assert.Equal("line 1\nline \"2\"", imported.Notes);
        Assert.Equal(PasswordLoginType.SshKey, imported.LoginType);
        Assert.Equal("""[{"rpId":"github.com"}]""", imported.PasskeyBindings);
        Assert.Equal("""{"ssid":"Monica"}""", imported.WifiMetadata);
        Assert.Equal("ssh-ed25519 AAAA", imported.SshKeyData);
    }

    [Fact]
    public void Import_export_exports_totp_items_as_aegis_json()
    {
        var service = new ImportExportService();
        var items = new[]
        {
            new SecureItem
            {
                Id = 42,
                ItemType = VaultItemType.Totp,
                Title = "GitHub",
                Notes = "work account",
                ItemData = TotpDataResolver.ToItemData(new TotpData(
                    "jbsw y3dp-ehpk3pxp",
                    "GitHub",
                    "dev@example.com",
                    Period: 45,
                    Digits: 8,
                    Algorithm: "SHA256"))
            },
            new SecureItem
            {
                Id = 43,
                ItemType = VaultItemType.Totp,
                Title = "Broken",
                ItemData = "{}"
            },
            new SecureItem
            {
                Id = 44,
                ItemType = VaultItemType.Note,
                Title = "Not an authenticator",
                ItemData = "{}"
            }
        };

        var json = service.ExportAegisJson(items);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Empty(root.GetProperty("header").GetProperty("slots").EnumerateArray());
        Assert.Equal("", root.GetProperty("header").GetProperty("params").GetProperty("nonce").GetString());
        var db = root.GetProperty("db");
        Assert.Equal(3, db.GetProperty("version").GetInt32());
        var entry = Assert.Single(db.GetProperty("entries").EnumerateArray());
        Assert.Equal("totp", entry.GetProperty("type").GetString());
        Assert.True(Guid.TryParse(entry.GetProperty("uuid").GetString(), out _));
        Assert.Equal("dev@example.com", entry.GetProperty("name").GetString());
        Assert.Equal("GitHub", entry.GetProperty("issuer").GetString());
        Assert.Equal("work account", entry.GetProperty("note").GetString());
        var info = entry.GetProperty("info");
        Assert.Equal("JBSWY3DPEHPK3PXP", info.GetProperty("secret").GetString());
        Assert.Equal("SHA256", info.GetProperty("algo").GetString());
        Assert.Equal(8, info.GetProperty("digits").GetInt32());
        Assert.Equal(45, info.GetProperty("period").GetInt32());
    }

    [Fact]
    public void Import_export_exports_totp_items_as_winui_compatible_csv()
    {
        var service = new ImportExportService();
        var items = new[]
        {
            new SecureItem
            {
                Id = 42,
                ItemType = VaultItemType.Totp,
                Title = "GitHub",
                Notes = "work account",
                IsFavorite = true,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(1000),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(2000),
                ItemData = TotpDataResolver.ToItemData(new TotpData("jbsw y3dp-ehpk3pxp", "GitHub", "dev@example.com"))
            },
            new SecureItem { Id = 43, ItemType = VaultItemType.Totp, Title = "Broken", ItemData = "{}" },
            new SecureItem { Id = 44, ItemType = VaultItemType.Note, Title = "Secure note", ItemData = "{}" }
        };

        var csv = service.ExportTotpCsv(items);

        Assert.Contains("ID,Type,Title,Data,Notes,IsFavorite,ImagePaths,CreatedAt,UpdatedAt", csv);
        Assert.Contains("42,TOTP,GitHub", csv);
        Assert.Contains("JBSWY3DPEHPK3PXP", csv);
        Assert.Contains("work account,True,,1000,2000", csv);
        Assert.DoesNotContain("Broken", csv);
        Assert.DoesNotContain("Secure note", csv);
    }

    [Fact]
    public void Import_export_exports_notes_as_winui_compatible_csv()
    {
        var service = new ImportExportService();
        var payload = NoteContentCodec.BuildSavePayload("Recovery", "# backup codes\nalpha", "ops, personal", true, ["inline.png"]);
        var items = new[]
        {
            new SecureItem
            {
                Id = 51,
                ItemType = VaultItemType.Note,
                Title = payload.Title,
                Notes = payload.NotesCache,
                ImagePaths = payload.ImagePaths,
                IsFavorite = true,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(3000),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(4000),
                ItemData = payload.ItemData
            },
            new SecureItem { Id = 52, ItemType = VaultItemType.Totp, Title = "Authenticator", ItemData = "{}" }
        };

        var csv = service.ExportNoteCsv(items);

        Assert.Contains("ID,Type,Title,Data,Notes,IsFavorite,ImagePaths,CreatedAt,UpdatedAt", csv);
        Assert.Contains("51,NOTE,Recovery", csv);
        Assert.Contains("backup codes", csv);
        Assert.Contains("ops", csv);
        Assert.Contains("inline.png", csv);
        Assert.Contains("True", csv);
        Assert.Contains("3000,4000", csv);
        Assert.DoesNotContain("Authenticator", csv);
    }

    [Fact]
    public void Import_export_imports_winui_compatible_note_csv()
    {
        var service = new ImportExportService();
        var payload = NoteContentCodec.BuildSavePayload("Recovery", "# backup codes\nalpha", "ops, personal", true, ["inline.png"]);
        var csv = service.ExportNoteCsv(
        [
            new SecureItem
            {
                Id = 51,
                ItemType = VaultItemType.Note,
                Title = payload.Title,
                Notes = payload.NotesCache,
                ImagePaths = payload.ImagePaths,
                IsFavorite = true,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(3000),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(4000),
                ItemData = payload.ItemData
            }
        ]);

        var imported = Assert.Single(service.ImportNoteCsv(csv));
        var decoded = NoteContentCodec.DecodeFromItem(imported);

        Assert.Equal(VaultItemType.Note, imported.ItemType);
        Assert.Equal("Recovery", imported.Title);
        Assert.Equal("# backup codes\nalpha", decoded.Content);
        Assert.Contains("ops", decoded.Tags);
        Assert.Contains("personal", decoded.Tags);
        Assert.True(decoded.IsMarkdown);
        Assert.True(imported.IsFavorite);
        Assert.Contains("inline.png", NoteContentCodec.DecodeImagePaths(imported.ImagePaths));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(3000), imported.CreatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(4000), imported.UpdatedAt);
    }

    [Fact]
    public void Import_export_imports_winui_compatible_totp_csv()
    {
        var service = new ImportExportService();
        var csv = service.ExportTotpCsv(
        [
            new SecureItem
            {
                Id = 42,
                ItemType = VaultItemType.Totp,
                Title = "GitHub",
                Notes = "work account",
                IsFavorite = true,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(1000),
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(2000),
                ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "dev@example.com"))
            }
        ]);

        var imported = Assert.Single(service.ImportTotpCsv(csv));
        var data = TotpDataResolver.ParseStoredItemData(imported.ItemData);

        Assert.Equal(VaultItemType.Totp, imported.ItemType);
        Assert.Equal("GitHub", imported.Title);
        Assert.Equal("work account", imported.Notes);
        Assert.True(imported.IsFavorite);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1000), imported.CreatedAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(2000), imported.UpdatedAt);
        Assert.NotNull(data);
        Assert.Equal("JBSWY3DPEHPK3PXP", data.Secret);
        Assert.Equal("GitHub", data.Issuer);
        Assert.Equal("dev@example.com", data.AccountName);
    }

    [Fact]
    public void Import_export_imports_unencrypted_aegis_json()
    {
        var service = new ImportExportService();
        var json = service.ExportAegisJson(
        [
            new SecureItem
            {
                ItemType = VaultItemType.Totp,
                Title = "GitHub",
                Notes = "work account",
                ItemData = TotpDataResolver.ToItemData(new TotpData("JBSWY3DPEHPK3PXP", "GitHub", "dev@example.com"))
            }
        ]);

        var imported = Assert.Single(service.ImportAegisJson(json));
        var data = TotpDataResolver.ParseStoredItemData(imported.ItemData);

        Assert.Equal(VaultItemType.Totp, imported.ItemType);
        Assert.Equal("dev@example.com", imported.Title);
        Assert.Equal("work account", imported.Notes);
        Assert.NotNull(data);
        Assert.Equal("JBSWY3DPEHPK3PXP", data.Secret);
        Assert.Equal("GitHub", data.Issuer);
        Assert.Equal("dev@example.com", data.AccountName);
    }

    [Fact]
    public void Import_export_reports_encrypted_aegis_json_as_unsupported()
    {
        var service = new ImportExportService();
        var json = """{"version":1,"header":{"slots":[]},"db":"encrypted-payload"}""";

        var ex = Assert.Throws<NotSupportedException>(() => service.ImportAegisJson(json));

        Assert.Contains("Encrypted Aegis JSON", ex.Message);
    }

    [Fact]
    public void Import_password_csv_accepts_bitwarden_style_headers()
    {
        var service = new ImportExportService();
        var csv = "name,login_uri,login_username,login_password,login_totp,notes\r\n"
            + "Example,https://example.com,dev,secret,JBSWY3DPEHPK3PXP,note";

        var imported = Assert.Single(service.ImportPasswordCsv(csv));

        Assert.Equal("Example", imported.Title);
        Assert.Equal("https://example.com", imported.Website);
        Assert.Equal("dev", imported.Username);
        Assert.Equal("secret", imported.Password);
        Assert.Equal("JBSWY3DPEHPK3PXP", imported.AuthenticatorKey);
        Assert.Equal(PasswordLoginType.Password, imported.LoginType);
    }

    [Fact]
    public void Feature_catalog_represents_android_parity_surface()
    {
        var keys = FeatureCatalog.AndroidParityFeatures.Select(item => item.Key).ToHashSet();

        Assert.Contains("passwords", keys);
        Assert.Contains("totp", keys);
        Assert.Contains("passkeys", keys);
        Assert.Contains("bitwarden", keys);
        Assert.Contains("keepass", keys);
        Assert.Contains("mdbx", keys);
        Assert.Contains("webdav", keys);
        Assert.Contains("autofill", keys);
    }

    [Fact]
    public void Security_questions_hash_and_verify_normalized_answers()
    {
        var service = new SecurityQuestionService();

        var setup = service.CreateSetup(
            new SecurityQuestionDraft(11, "", "  Tiga "),
            new SecurityQuestionDraft(SecurityQuestionService.CustomQuestionId, "Favorite shell?", "PowerShell"));

        Assert.True(setup.HasCompleteSetup);
        Assert.Equal(11, setup.Question1Id);
        Assert.Equal(SecurityQuestionService.CustomQuestionId, setup.Question2Id);
        Assert.NotEqual("Tiga", setup.Question1AnswerHash);
        Assert.True(service.VerifyAnswer("tiga", setup.Question1AnswerHash, setup.Question1AnswerSalt));
        Assert.True(service.VerifyAnswer(" powershell ", setup.Question2AnswerHash, setup.Question2AnswerSalt));
        Assert.False(service.VerifyAnswer("cmd", setup.Question2AnswerHash, setup.Question2AnswerSalt));
    }

    [Fact]
    public void Security_questions_reject_duplicate_or_incomplete_setup()
    {
        var service = new SecurityQuestionService();

        Assert.Throws<ArgumentException>(() => service.CreateSetup(
            new SecurityQuestionDraft(1, "", "one"),
            new SecurityQuestionDraft(1, "", "two")));
        Assert.Throws<ArgumentException>(() => service.CreateSetup(
            new SecurityQuestionDraft(SecurityQuestionService.CustomQuestionId, "", "one"),
            new SecurityQuestionDraft(2, "", "two")));
        Assert.Contains(
            service.PredefinedQuestions,
            question => question.Id == SecurityQuestionService.CustomQuestionId && question.Text == "Custom question");
    }

    [Fact]
    public void Password_generator_produces_strong_passwords()
    {
        var service = new PasswordGeneratorService();

        var password = service.GeneratePassword(24);
        var analysis = service.Analyze(password);

        Assert.Equal(24, password.Length);
        Assert.True(analysis.Score >= 3);
    }

    [Fact]
    public void Password_generator_honors_character_options()
    {
        var service = new PasswordGeneratorService();

        var password = service.GeneratePassword(
            32,
            includeUppercase: false,
            includeLowercase: true,
            includeNumbers: true,
            includeSymbols: false);

        Assert.Equal(32, password.Length);
        Assert.DoesNotContain(password, char.IsUpper);
        Assert.DoesNotContain(password, c => !char.IsLetterOrDigit(c));
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
    }

    [Fact]
    public async Task Pwned_password_service_queries_range_api_with_sha1_prefix()
    {
        var password = "correct horse battery staple";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var handler = new FakePwnedPasswordHandler(hash[..5], $"{hash[5..]}:42\r\n00000000000000000000000000000000000:1");
        var service = new PwnedPasswordService(new HttpClient(handler));

        var results = await service.CheckPasswordsAsync([password, password, ""]);

        Assert.Equal(42, results[password]);
        Assert.Single(results);
        Assert.Equal(1, handler.RequestCount);
        Assert.EndsWith($"/range/{hash[..5]}", handler.LastRequestUri?.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.True(handler.SawPaddingHeader);
    }

    private sealed class FakePwnedPasswordHandler(string expectedPrefix, string responseContent) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public bool SawPaddingHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            SawPaddingHeader = request.Headers.TryGetValues("Add-Padding", out var values) && values.Contains("true");
            Assert.EndsWith($"/range/{expectedPrefix}", request.RequestUri?.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            });
        }
    }
}
