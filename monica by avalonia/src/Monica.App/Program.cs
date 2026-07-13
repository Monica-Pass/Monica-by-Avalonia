using Avalonia;
using Monica.Core.Models;
using Monica.Core.Services;
using Monica.Data;
using Monica.Data.Diagnostics;
using Monica.Data.Repositories;
using System;
using System.IO;
using System.Text.Json;

namespace Monica.App;

class Program
{
    private static readonly string LogPath = MonicaAppDataPaths.GetPath("crash.log");

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            File.AppendAllText(LogPath, $"[UnhandledException] {e.ExceptionObject}\n");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            File.AppendAllText(LogPath, $"[UnobservedTaskException] {e.Exception}\n");
            e.SetObserved();
        };

        try
        {
            AppDiagnostics.Info($"Process started at {DateTimeOffset.Now:O}");

            if (args.Length > 0 && string.Equals(args[0], "--smoke-vault", StringComparison.Ordinal))
            {
                return RunVaultSmokeTestAsync(args).GetAwaiter().GetResult();
            }

            if (args.Length > 0 && string.Equals(args[0], "--seed-smoke-vault", StringComparison.Ordinal))
            {
                return SeedSmokeVaultAsync(args).GetAwaiter().GetResult();
            }

            if (args.Length > 0 && string.Equals(args[0], "--init-empty-smoke-vault", StringComparison.Ordinal))
            {
                return InitEmptySmokeVaultAsync(args).GetAwaiter().GetResult();
            }

            if (args.Length > 0 && string.Equals(args[0], "--benchmark-vault", StringComparison.Ordinal))
            {
                return RunVaultBenchmarkAsync(args).GetAwaiter().GetResult();
            }

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            File.AppendAllText(LogPath, $"[Fatal] {ex}\n");
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static async Task<int> RunVaultSmokeTestAsync(string[] args)
    {
        try
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine("Usage: Monica.App --smoke-vault <dbPath> <correctPassword> <wrongPassword>");
                return 2;
            }

            var factory = new SqliteConnectionFactory(args[1]);
            var migrator = new DatabaseMigrator(factory);
            var store = new VaultCredentialStore(factory, migrator);
            var crypto = new CryptoService();
            var credential = await store.GetAsync();
            if (credential is null)
            {
                credential = crypto.HashMasterPassword(args[2]);
                await store.SaveAsync(credential);
            }

            if (new CryptoService().VerifyMasterPassword(args[3], credential))
            {
                Console.Error.WriteLine("Wrong password was accepted.");
                return 3;
            }

            var unlockCrypto = new CryptoService();
            if (!unlockCrypto.VerifyMasterPassword(args[2], credential))
            {
                Console.Error.WriteLine("Correct password was rejected.");
                return 4;
            }

            var encrypted = unlockCrypto.EncryptString("monica-aot-smoke");
            if (!string.Equals("monica-aot-smoke", unlockCrypto.DecryptString(encrypted), StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Encryption roundtrip failed.");
                return 5;
            }

            Console.WriteLine("Vault smoke test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> RunVaultBenchmarkAsync(string[] args)
    {
        try
        {
            if (args.Length < 2 || !int.TryParse(args[1], out var entryCount) || entryCount < 1)
            {
                Console.Error.WriteLine(
                    "Usage: Monica.App --benchmark-vault <entryCount> [--benchmark-path <dbPath>] [--retain-benchmark-vault]");
                return 2;
            }

            string? databasePath = null;
            var retainDatabase = false;
            for (var index = 2; index < args.Length; index++)
            {
                if (string.Equals(args[index], "--retain-benchmark-vault", StringComparison.Ordinal))
                {
                    retainDatabase = true;
                    continue;
                }

                if (string.Equals(args[index], "--benchmark-path", StringComparison.Ordinal) && index + 1 < args.Length)
                {
                    databasePath = args[++index];
                    continue;
                }

                Console.Error.WriteLine($"Unknown benchmark argument: {args[index]}");
                return 2;
            }

            var result = await VaultBenchmarkRunner.RunAsync(new VaultBenchmarkOptions(
                entryCount,
                databasePath,
                retainDatabase));
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> SeedSmokeVaultAsync(string[] args)
    {
        try
        {
            if (args.Length is not 2 and not 3)
            {
                Console.Error.WriteLine("Usage: Monica.App --seed-smoke-vault <password> [dbPath]");
                return 2;
            }

            var password = args[1];
            var databasePath = args.Length == 3 ? args[2] : MonicaAppDataPaths.GetDatabasePath();
            var factory = new SqliteConnectionFactory(databasePath);
            var migrator = new DatabaseMigrator(factory);
            var store = new VaultCredentialStore(factory, migrator);
            var crypto = new CryptoService();
            var credential = await store.GetAsync();
            if (credential is null)
            {
                credential = crypto.HashMasterPassword(password);
                await store.SaveAsync(credential);
            }
            else if (!crypto.VerifyMasterPassword(password, credential))
            {
                Console.Error.WriteLine("Seed password does not unlock the existing smoke vault.");
                return 3;
            }

            if (!crypto.IsUnlocked && !crypto.VerifyMasterPassword(password, credential))
            {
                Console.Error.WriteLine("Seed password was rejected.");
                return 4;
            }

            var repository = new MonicaRepository(factory, migrator);
            if ((await repository.GetPasswordsAsync()).Any(item => item.Title.StartsWith("Smoke ", StringComparison.Ordinal)))
            {
                await EnsureSmokeH04DataAsync(repository, crypto);
                await EnsureSmokeEdgeCaseDataAsync(repository, crypto);
                Console.WriteLine($"Smoke vault already seeded: {databasePath}");
                return 0;
            }

            var work = new Category { Name = "Smoke/Work", SortOrder = 1 };
            var prod = new Category { Name = "Smoke/Work/Production", SortOrder = 2 };
            var personal = new Category { Name = "Smoke/Personal", SortOrder = 3 };
            await repository.SaveCategoryAsync(work);
            await repository.SaveCategoryAsync(prod);
            await repository.SaveCategoryAsync(personal);

            var passwords = new List<PasswordEntry>
            {
                new PasswordEntry
                {
                    Title = "Smoke GitHub",
                    Website = "https://github.com",
                    Username = "dev@smoke.local",
                    Password = crypto.EncryptString("github-smoke-secret"),
                    CategoryId = work.Id,
                    IsFavorite = true,
                    AuthenticatorKey = "otpauth://totp/GitHub:dev%40smoke.local?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&period=30&digits=6"
                },
                new PasswordEntry
                {
                    Title = "Smoke Production Console",
                    Website = "https://console.smoke.local",
                    Username = "ops",
                    Password = crypto.EncryptString("production-smoke-secret"),
                    CategoryId = prod.Id,
                    Notes = "Used for screenshot matrix validation."
                },
                new PasswordEntry
                {
                    Title = "Smoke Personal Mail",
                    Website = "https://mail.smoke.local",
                    Username = "me",
                    Password = crypto.EncryptString("mail-smoke-secret"),
                    CategoryId = personal.Id
                },
                new PasswordEntry
                {
                    Title = "Smoke No Folder",
                    Website = "https://nofolder.smoke.local",
                    Username = "loose",
                    Password = crypto.EncryptString("loose-smoke-secret")
                }
            };

            for (var index = 1; index <= 20; index++)
            {
                passwords.Add(new PasswordEntry
                {
                    Title = $"Smoke Account {index:00}",
                    Website = $"https://account-{index:00}.smoke.local",
                    Username = $"smoke-user-{index:00}",
                    Password = crypto.EncryptString($"smoke-secret-{index:00}"),
                    CategoryId = index % 3 == 0
                        ? prod.Id
                        : index % 2 == 0
                            ? work.Id
                            : personal.Id,
                    Notes = index % 4 == 0
                        ? $"Smoke note payload for account {index:00}."
                        : string.Empty,
                    IsFavorite = index % 7 == 0
                });
            }

            foreach (var entry in passwords)
            {
                await repository.SavePasswordAsync(entry);
                await repository.LogAsync(new OperationLog
                {
                    ItemType = "PASSWORD",
                    ItemId = entry.Id,
                    ItemTitle = entry.Title,
                    OperationType = "CREATE",
                    DeviceName = Environment.MachineName
                });
            }

            await repository.ReplaceCustomFieldsAsync(passwords[0].Id,
            [
                new CustomField { EntryId = passwords[0].Id, Title = "Smoke recovery", Value = "blue", SortOrder = 0 },
                new CustomField { EntryId = passwords[0].Id, Title = "Smoke protected", Value = "654321", IsProtected = true, SortOrder = 1 }
            ]);

            foreach (var entry in passwords.Skip(4).Where((_, index) => index % 4 == 0))
            {
                await repository.ReplaceCustomFieldsAsync(entry.Id,
                [
                    new CustomField { EntryId = entry.Id, Title = "Smoke field", Value = $"field-{entry.Id}", SortOrder = 0 },
                    new CustomField { EntryId = entry.Id, Title = "Smoke secret", Value = $"protected-{entry.Id}", IsProtected = true, SortOrder = 1 }
                ]);
            }

            await repository.SaveAttachmentAsync(new Attachment
            {
                OwnerType = "PASSWORD",
                OwnerId = passwords[1].Id,
                FileName = "smoke-runbook.txt",
                ContentType = "text/plain",
                StoragePath = "secure_attachments/smoke-runbook.txt",
                SizeBytes = 128
            });

            foreach (var entry in passwords.Skip(4).Where((_, index) => index % 5 == 0))
            {
                await repository.SaveAttachmentAsync(new Attachment
                {
                    OwnerType = "PASSWORD",
                    OwnerId = entry.Id,
                    FileName = $"smoke-attachment-{entry.Id}.txt",
                    ContentType = "text/plain",
                    StoragePath = $"secure_attachments/smoke-attachment-{entry.Id}.txt",
                    SizeBytes = 256 + entry.Id
                });
            }

            var notePayload = NoteContentCodec.BuildSavePayload(
                "Smoke Recovery Note",
                "# Smoke Recovery Note\n\n- Screenshot matrix data\n- Safe temporary vault",
                "smoke,validation",
                true);
            await repository.SaveSecureItemAsync(new SecureItem
            {
                ItemType = VaultItemType.Note,
                Title = notePayload.Title,
                Notes = notePayload.NotesCache,
                ItemData = notePayload.ItemData,
                ImagePaths = notePayload.ImagePaths,
                IsFavorite = true
            });

            for (var index = 1; index <= 12; index++)
            {
                var tags = index % 3 == 0
                    ? "ops,runbook"
                    : index % 2 == 0
                        ? "personal,checklist"
                        : "smoke,validation";
                var payload = NoteContentCodec.BuildSavePayload(
                    $"Smoke Note {index:00}",
                    $"# Smoke Note {index:00}\n\n- Multi-tab validation\n- Tag group: {tags}\n\n![inline](monica-image://smoke-note-{index:00})",
                    tags,
                    true,
                    [$"smoke-note-{index:00}"]);
                await repository.SaveSecureItemAsync(new SecureItem
                {
                    ItemType = VaultItemType.Note,
                    Title = payload.Title,
                    Notes = payload.NotesCache,
                    ItemData = payload.ItemData,
                    ImagePaths = payload.ImagePaths,
                    IsFavorite = index % 5 == 0
                });
            }

            await EnsureSmokeH04DataAsync(repository, crypto);
            await EnsureSmokeEdgeCaseDataAsync(repository, crypto);
            Console.WriteLine($"Smoke vault seeded: {databasePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<int> InitEmptySmokeVaultAsync(string[] args)
    {
        try
        {
            if (args.Length is not 2 and not 3)
            {
                Console.Error.WriteLine("Usage: Monica.App --init-empty-smoke-vault <password> [dbPath]");
                return 2;
            }

            var password = args[1];
            var databasePath = args.Length == 3 ? args[2] : MonicaAppDataPaths.GetDatabasePath();
            var factory = new SqliteConnectionFactory(databasePath);
            var migrator = new DatabaseMigrator(factory);
            var store = new VaultCredentialStore(factory, migrator);
            var crypto = new CryptoService();
            var credential = await store.GetAsync();
            if (credential is null)
            {
                credential = crypto.HashMasterPassword(password);
                await store.SaveAsync(credential);
            }
            else if (!crypto.VerifyMasterPassword(password, credential))
            {
                Console.Error.WriteLine("Password does not unlock the existing empty smoke vault.");
                return 3;
            }

            Console.WriteLine($"Empty smoke vault initialized: {databasePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task EnsureSmokeH04DataAsync(MonicaRepository repository, CryptoService crypto)
    {
        var allPasswords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        if (allPasswords.All(item => !string.Equals(item.Title, "Smoke Archived Account", StringComparison.Ordinal)))
        {
            var archived = new PasswordEntry
            {
                Title = "Smoke Archived Account",
                Website = "https://archived.smoke.local",
                Username = "archived",
                Password = crypto.EncryptString("archived-smoke-secret"),
                Notes = "Archived row for H04 list interaction smoke.",
                IsArchived = true,
                ArchivedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            };
            await repository.SavePasswordAsync(archived);
            await repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = archived.Id,
                ItemTitle = archived.Title,
                OperationType = "ARCHIVE",
                DeviceName = Environment.MachineName
            });
        }

        if (allPasswords.All(item => !string.Equals(item.Title, "Smoke Deleted Account", StringComparison.Ordinal)))
        {
            var deleted = new PasswordEntry
            {
                Title = "Smoke Deleted Account",
                Website = "https://deleted.smoke.local",
                Username = "deleted",
                Password = crypto.EncryptString("deleted-smoke-secret"),
                Notes = "Recycle-bin row for H04 list interaction smoke."
            };
            await repository.SavePasswordAsync(deleted);
            await repository.SoftDeletePasswordAsync(deleted.Id);
            await repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = deleted.Id,
                ItemTitle = deleted.Title,
                OperationType = "DELETE",
                DeviceName = Environment.MachineName
            });
        }

        var secureItems = await repository.GetSecureItemsAsync(includeDeleted: true);
        if (secureItems.All(item => !string.Equals(item.Title, "Smoke Wallet Card", StringComparison.Ordinal)))
        {
            var cardData = new BankCardWalletData
            {
                CardNumber = "4111111111111111",
                CardholderName = "Smoke User",
                ExpiryMonth = "12",
                ExpiryYear = "2030",
                Cvv = "123",
                BankName = "Smoke Bank",
                CardTypeString = "CREDIT",
                Brand = "Visa",
                BillingAddress = "1 Smoke Street"
            };
            await repository.SaveSecureItemAsync(new SecureItem
            {
                ItemType = VaultItemType.BankCard,
                Title = "Smoke Wallet Card",
                Notes = "Card row for H04 wallet detail smoke.",
                ItemData = WalletItemDataCodec.EncodeBankCard(cardData),
                ImagePaths = WalletItemDataCodec.EncodeImagePaths(cardData.ImagePaths),
                IsFavorite = true
            });
        }

        if (secureItems.All(item => !string.Equals(item.Title, "Smoke Wallet Document", StringComparison.Ordinal)))
        {
            var documentData = new DocumentWalletData
            {
                DocumentNumber = "SMOKE-123456",
                FullName = "Smoke User",
                IssuedDate = "2024-01-01",
                ExpiryDate = "2034-01-01",
                IssuedBy = "Smoke Authority",
                Nationality = "Smoke",
                DocumentTypeString = "PASSPORT",
                AdditionalInfo = "Document row for H04 wallet selection smoke."
            };
            await repository.SaveSecureItemAsync(new SecureItem
            {
                ItemType = VaultItemType.Document,
                Title = "Smoke Wallet Document",
                Notes = "Document row for H04 wallet detail smoke.",
                ItemData = WalletItemDataCodec.EncodeDocument(documentData),
                ImagePaths = WalletItemDataCodec.EncodeImagePaths(documentData.ImagePaths)
            });
        }
    }

    private static async Task EnsureSmokeEdgeCaseDataAsync(MonicaRepository repository, CryptoService crypto)
    {
        var categories = (await repository.GetCategoriesAsync()).ToList();
        await EnsureSmokeCategoryAsync(
            repository,
            categories,
            "Smoke/Long Folder Segment With A Very Descriptive Name",
            90);
        var child = await EnsureSmokeCategoryAsync(
            repository,
            categories,
            "Smoke/Long Folder Segment With A Very Descriptive Name/Deep Child Folder With Deployment Secrets",
            91);
        var leaf = await EnsureSmokeCategoryAsync(
            repository,
            categories,
            "Smoke/Long Folder Segment With A Very Descriptive Name/Deep Child Folder With Deployment Secrets/Extremely Long Leaf Folder Name For Layout Regression",
            92);

        var allPasswords = await repository.GetPasswordsAsync(includeDeleted: true, includeArchived: true);
        if (allPasswords.All(item => !item.Title.StartsWith("Smoke Edge Long Password", StringComparison.Ordinal)))
        {
            var entry = new PasswordEntry
            {
                Title = "Smoke Edge Long Password Title For Responsive Layout Regression With Many Words And No Shortcut",
                Website = "https://very-long-subdomain-for-layout-regression.accounts.smoke.local/teams/platform/security/passwords/production/credential-detail-with-a-long-path?environment=production-east&rotationWindow=quarterly&owner=identity-platform",
                Username = "very.long.identity.owner.with.multiple.parts@smoke-layout-regression.example.internal",
                Password = crypto.EncryptString("edge-long-password-smoke-secret"),
                CategoryId = leaf.Id,
                Notes = "Long note payload used to verify that password detail text wraps or truncates cleanly without pushing command buttons out of the workspace.",
                IsFavorite = true
            };
            await repository.SavePasswordAsync(entry);
            await repository.LogAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = entry.Id,
                ItemTitle = entry.Title,
                OperationType = "CREATE",
                DeviceName = Environment.MachineName
            });
            await repository.ReplaceCustomFieldsAsync(entry.Id,
            [
                new CustomField
                {
                    EntryId = entry.Id,
                    Title = "Very long custom field name for detail pane wrapping validation",
                    Value = "Very long custom field value that should remain readable without stretching the right inspector pane or resizing the password list.",
                    SortOrder = 0
                },
                new CustomField
                {
                    EntryId = entry.Id,
                    Title = "Protected edge field",
                    Value = "protected-edge-value",
                    IsProtected = true,
                    SortOrder = 1
                }
            ]);
            await repository.SaveAttachmentAsync(new Attachment
            {
                OwnerType = "PASSWORD",
                OwnerId = entry.Id,
                FileName = "smoke-edge-layout-regression-runbook-with-a-long-file-name.md",
                ContentType = "text/markdown",
                StoragePath = "secure_attachments/smoke-edge-layout-regression-runbook-with-a-long-file-name.md",
                SizeBytes = 4096
            });
        }

        var secureItems = await repository.GetSecureItemsAsync(includeDeleted: true);
        if (secureItems.All(item => !item.Title.StartsWith("Smoke Edge Long Markdown Note", StringComparison.Ordinal)))
        {
            var markdown = string.Join(
                "\n",
                [
                    "# Smoke Edge Long Markdown Note For Responsive Editor Validation",
                    "",
                    "This note intentionally contains long paragraphs, tables, task lists, code fences, links, image references and enough lines to exercise the editor, preview and inspector layout.",
                    "",
                    "## Deep section with a title that should wrap cleanly instead of moving toolbar commands out of reach",
                    "",
                    "- [x] Keep the file tree usable with long tag group names.",
                    "- [ ] Keep the active tab compact when many note tabs are open.",
                    "- [ ] Keep edit and preview content aligned to the same text origin.",
                    "",
                    "| Column | Value | Notes |",
                    "| --- | --- | --- |",
                    "| Long URL | https://docs.smoke.local/workspaces/security/notes/markdown/editor/preview?case=very-long-table-cell&mode=split | Must not force horizontal page overflow. |",
                    "| Long owner | very.long.identity.owner.with.multiple.parts@smoke-layout-regression.example.internal | Used by inspector text wrapping. |",
                    "",
                    "```powershell",
                    "dotnet test \"monica by avalonia/tests/Monica.Tests/Monica.Tests.csproj\" --no-build",
                    "```",
                    "",
                    "![edge image](monica-image://smoke-edge-long-note-image)",
                    "",
                    "A final deliberately long paragraph follows so the preview pane has to wrap natural prose rather than only bullets: the quick brown layout regression text walks through nested folders, compressed tabs, a fixed command area, a stable inspector pane, and a markdown body that should stay readable at 800 by 500 without covering neighboring controls."
                ]);
            var payload = NoteContentCodec.BuildSavePayload(
                "Smoke Edge Long Markdown Note For Responsive Editor Validation",
                markdown,
                "smoke-edge,very-long-tag-name-for-file-tree-layout-regression,markdown-validation",
                true,
                ["smoke-edge-long-note-image"]);
            await repository.SaveSecureItemAsync(new SecureItem
            {
                ItemType = VaultItemType.Note,
                Title = payload.Title,
                Notes = payload.NotesCache,
                ItemData = payload.ItemData,
                ImagePaths = payload.ImagePaths,
                CategoryId = child.Id,
                IsFavorite = true
            });
        }
    }

    private static async Task<Category> EnsureSmokeCategoryAsync(
        MonicaRepository repository,
        List<Category> categories,
        string name,
        int sortOrder)
    {
        var existing = categories.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        var category = new Category
        {
            Name = name,
            SortOrder = sortOrder
        };
        await repository.SaveCategoryAsync(category);
        categories.Add(category);
        return category;
    }
}
