using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Monica.Core.Models;
using Monica.Data.Repositories;

namespace Monica.Data.Diagnostics;

public sealed record VaultBenchmarkOptions(
    int EntryCount,
    string? DatabasePath = null,
    bool RetainDatabase = false);

public sealed record VaultBenchmarkTimings(
    double InitializeMilliseconds,
    double SeedMilliseconds,
    double ColdLoadMilliseconds,
    double SearchMedianMilliseconds,
    double SearchP95Milliseconds,
    double CreateMilliseconds,
    double UpdateMilliseconds,
    double DeleteMilliseconds);

public sealed record VaultBenchmarkResult(
    string Schema,
    int RequestedEntryCount,
    int PasswordCount,
    int SecureItemCount,
    int LoadedPasswordCount,
    int LoadedSecureItemCount,
    int SearchIterations,
    int SearchMatches,
    long DatabaseBytes,
    string DatabasePath,
    bool DatabaseRetained,
    VaultBenchmarkTimings Timings);

/// <summary>
/// Creates a deterministic commercial-scale fixture, then measures repository
/// load, in-memory search, and CRUD without initializing Avalonia.
/// Fixture generation is timed separately from the operations under test.
/// </summary>
public static class VaultBenchmarkRunner
{
    private const int SearchIterations = 50;

    public static async Task<VaultBenchmarkResult> RunAsync(
        VaultBenchmarkOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.EntryCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Benchmark entry count must be positive.");
        }

        var databasePath = ResolveDatabasePath(options.DatabasePath);
        if (File.Exists(databasePath))
        {
            throw new IOException($"Benchmark database already exists: {databasePath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var passwordCount = options.EntryCount * 7 / 10;
        var secureItemCount = options.EntryCount - passwordCount;

        try
        {
            var factory = new SqliteConnectionFactory(databasePath);
            var migrator = new DatabaseMigrator(factory);
            var initialize = Stopwatch.StartNew();
            await migrator.MigrateAsync(cancellationToken);
            initialize.Stop();

            var seed = Stopwatch.StartNew();
            await SeedAsync(factory, passwordCount, secureItemCount, cancellationToken);
            seed.Stop();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var coldFactory = new SqliteConnectionFactory(databasePath);
            var repository = new MonicaRepository(coldFactory, new DatabaseMigrator(coldFactory));
            var coldLoad = Stopwatch.StartNew();
            var passwords = await repository.GetPasswordsAsync(cancellationToken: cancellationToken);
            var secureItems = await repository.GetSecureItemsAsync(cancellationToken: cancellationToken);
            _ = await repository.GetCategoriesAsync(cancellationToken);
            coldLoad.Stop();

            if (passwords.Count != passwordCount || secureItems.Count != secureItemCount)
            {
                throw new InvalidOperationException(
                    $"Benchmark fixture count mismatch. Expected {passwordCount}/{secureItemCount}, loaded {passwords.Count}/{secureItems.Count}.");
            }

            var searchDurations = new double[SearchIterations];
            var searchMatches = 0;
            for (var iteration = 0; iteration < SearchIterations; iteration++)
            {
                var target = iteration * Math.Max(1, options.EntryCount / SearchIterations);
                var query = $"benchmark-{target:D6}";
                var search = Stopwatch.StartNew();
                searchMatches += CountMatches(passwords, secureItems, query);
                search.Stop();
                searchDurations[iteration] = search.Elapsed.TotalMilliseconds;
            }

            Array.Sort(searchDurations);

            var created = new PasswordEntry
            {
                Title = "Benchmark CRUD entry",
                Website = "https://crud.benchmark.invalid",
                Username = "crud-user",
                Password = "synthetic-crud-secret",
                Notes = "Created by the headless benchmark."
            };
            var create = Stopwatch.StartNew();
            await repository.SavePasswordAsync(created, cancellationToken);
            create.Stop();

            created.Notes = "Updated by the headless benchmark.";
            var update = Stopwatch.StartNew();
            await repository.SavePasswordAsync(created, cancellationToken);
            update.Stop();

            var delete = Stopwatch.StartNew();
            await repository.SoftDeletePasswordAsync(created.Id, cancellationToken);
            delete.Stop();

            var databaseBytes = GetDatabaseBytes(databasePath);
            return new VaultBenchmarkResult(
                Schema: "monica-vault-benchmark-v1",
                RequestedEntryCount: options.EntryCount,
                PasswordCount: passwordCount,
                SecureItemCount: secureItemCount,
                LoadedPasswordCount: passwords.Count,
                LoadedSecureItemCount: secureItems.Count,
                SearchIterations,
                SearchMatches: searchMatches,
                DatabaseBytes: databaseBytes,
                DatabasePath: databasePath,
                DatabaseRetained: options.RetainDatabase,
                Timings: new VaultBenchmarkTimings(
                    Round(initialize.Elapsed.TotalMilliseconds),
                    Round(seed.Elapsed.TotalMilliseconds),
                    Round(coldLoad.Elapsed.TotalMilliseconds),
                    Round(Percentile(searchDurations, 0.50)),
                    Round(Percentile(searchDurations, 0.95)),
                    Round(create.Elapsed.TotalMilliseconds),
                    Round(update.Elapsed.TotalMilliseconds),
                    Round(delete.Elapsed.TotalMilliseconds)));
        }
        finally
        {
            if (!options.RetainDatabase)
            {
                SqliteConnection.ClearAllPools();
                DeleteDatabaseFiles(databasePath);
            }
        }
    }

    private static async Task SeedAsync(
        ISqliteConnectionFactory factory,
        int passwordCount,
        int secureItemCount,
        CancellationToken cancellationToken)
    {
        await using var connection = factory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await SeedCategoriesAsync(connection, transaction, cancellationToken);
        await SeedPasswordsAsync(connection, transaction, passwordCount, cancellationToken);
        await SeedSecureItemsAsync(connection, transaction, secureItemCount, passwordCount, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task SeedCategoriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO categories(name, sort_order) VALUES ($name, $sort_order);";
        var name = command.Parameters.Add("$name", SqliteType.Text);
        var sortOrder = command.Parameters.Add("$sort_order", SqliteType.Integer);
        await command.PrepareAsync(cancellationToken);
        for (var index = 0; index < 32; index++)
        {
            name.Value = $"Benchmark/Folder {index:D2}";
            sortOrder.Value = index;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task SeedPasswordsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO password_entries(
                title, website, username, password, notes, created_at, updated_at,
                is_favorite, sort_order, category_id, login_type)
            VALUES (
                $title, $website, $username, $password, $notes, $created_at, $updated_at,
                $is_favorite, $sort_order, $category_id, 'PASSWORD');
            """;
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var website = command.Parameters.Add("$website", SqliteType.Text);
        var username = command.Parameters.Add("$username", SqliteType.Text);
        var password = command.Parameters.Add("$password", SqliteType.Text);
        var notes = command.Parameters.Add("$notes", SqliteType.Text);
        var createdAt = command.Parameters.Add("$created_at", SqliteType.Integer);
        var updatedAt = command.Parameters.Add("$updated_at", SqliteType.Integer);
        var favorite = command.Parameters.Add("$is_favorite", SqliteType.Integer);
        var sortOrder = command.Parameters.Add("$sort_order", SqliteType.Integer);
        var categoryId = command.Parameters.Add("$category_id", SqliteType.Integer);
        await command.PrepareAsync(cancellationToken);

        await using var fieldCommand = connection.CreateCommand();
        fieldCommand.Transaction = transaction;
        fieldCommand.CommandText =
            "INSERT INTO custom_fields(entry_id, title, value, is_protected, sort_order) VALUES ($entry_id, $title, $value, $protected, 0);";
        var fieldEntryId = fieldCommand.Parameters.Add("$entry_id", SqliteType.Integer);
        var fieldTitle = fieldCommand.Parameters.Add("$title", SqliteType.Text);
        var fieldValue = fieldCommand.Parameters.Add("$value", SqliteType.Text);
        var fieldProtected = fieldCommand.Parameters.Add("$protected", SqliteType.Integer);
        await fieldCommand.PrepareAsync(cancellationToken);

        const long epoch = 1_767_225_600_000;
        for (var index = 0; index < count; index++)
        {
            var marker = $"benchmark-{index:D6}";
            title.Value = $"Benchmark Login {index:D6}";
            website.Value = $"https://account-{index % 1000:D3}.benchmark.invalid/{marker}";
            username.Value = $"user-{index:D6}@benchmark.invalid";
            password.Value = $"synthetic-secret-{index:D6}";
            notes.Value = index % 4 == 0 ? $"Search marker {marker}" : "";
            createdAt.Value = epoch + index;
            updatedAt.Value = epoch + index;
            favorite.Value = index % 17 == 0 ? 1 : 0;
            sortOrder.Value = index;
            categoryId.Value = index % 32 + 1;
            await command.ExecuteNonQueryAsync(cancellationToken);

            if (index % 10 == 0)
            {
                fieldEntryId.Value = index + 1;
                fieldTitle.Value = "Benchmark metadata";
                fieldValue.Value = marker;
                fieldProtected.Value = index % 20 == 0 ? 1 : 0;
                await fieldCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task SeedSecureItemsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int count,
        int passwordCount,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO secure_items(
                item_type, title, notes, is_favorite, sort_order, created_at, updated_at,
                item_data, image_paths, bound_password_id, category_id, sync_status)
            VALUES (
                $item_type, $title, $notes, $is_favorite, $sort_order, $created_at, $updated_at,
                $item_data, '[]', $bound_password_id, $category_id, 'NONE');
            """;
        var itemType = command.Parameters.Add("$item_type", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var notes = command.Parameters.Add("$notes", SqliteType.Text);
        var favorite = command.Parameters.Add("$is_favorite", SqliteType.Integer);
        var sortOrder = command.Parameters.Add("$sort_order", SqliteType.Integer);
        var createdAt = command.Parameters.Add("$created_at", SqliteType.Integer);
        var updatedAt = command.Parameters.Add("$updated_at", SqliteType.Integer);
        var itemData = command.Parameters.Add("$item_data", SqliteType.Text);
        var boundPasswordId = command.Parameters.Add("$bound_password_id", SqliteType.Integer);
        var categoryId = command.Parameters.Add("$category_id", SqliteType.Integer);
        await command.PrepareAsync(cancellationToken);

        const long epoch = 1_767_225_700_000;
        for (var index = 0; index < count; index++)
        {
            var globalIndex = passwordCount + index;
            var marker = $"benchmark-{globalIndex:D6}";
            var type = (index % 10) switch
            {
                < 5 => VaultItemType.Note,
                < 8 => VaultItemType.Totp,
                8 => VaultItemType.BankCard,
                _ => VaultItemType.Document
            };
            itemType.Value = type.ToString().ToUpperInvariant();
            title.Value = $"Benchmark {type} {globalIndex:D6}";
            notes.Value = $"Synthetic secure item {marker}";
            favorite.Value = index % 19 == 0 ? 1 : 0;
            sortOrder.Value = index;
            createdAt.Value = epoch + index;
            updatedAt.Value = epoch + index;
            itemData.Value = BuildSecureItemData(type, globalIndex);
            boundPasswordId.Value = type == VaultItemType.Totp && passwordCount > 0
                ? index % passwordCount + 1
                : DBNull.Value;
            categoryId.Value = index % 32 + 1;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string BuildSecureItemData(VaultItemType type, int index) => type switch
    {
        VaultItemType.Totp => $"{{\"secret\":\"JBSWY3DPEHPK3PXP\",\"accountName\":\"benchmark-{index:D6}\"}}",
        VaultItemType.BankCard => $"{{\"cardNumber\":\"4111111111{index % 1_000_000:D6}\"}}",
        VaultItemType.Document => $"{{\"documentNumber\":\"DOC-{index:D6}\"}}",
        _ => $"{{\"content\":\"Benchmark note benchmark-{index:D6}\",\"isMarkdown\":true}}"
    };

    private static int CountMatches(
        IReadOnlyList<PasswordEntry> passwords,
        IReadOnlyList<SecureItem> secureItems,
        string query)
    {
        var count = 0;
        foreach (var entry in passwords)
        {
            if (entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Website.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Username.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.Notes.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        foreach (var item in secureItems)
        {
            if (item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Notes.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static string ResolveDatabasePath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            return Path.GetFullPath(requestedPath);
        }

        return Path.Combine(
            Path.GetTempPath(),
            "Monica",
            "benchmarks",
            $"vault-{Guid.NewGuid():N}.db");
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        var index = Math.Clamp((int)Math.Ceiling(sortedValues.Count * percentile) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private static double Round(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static long GetDatabaseBytes(string databasePath) =>
        EnumerateDatabaseFiles(databasePath)
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in EnumerateDatabaseFiles(databasePath))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static IEnumerable<string> EnumerateDatabaseFiles(string databasePath)
    {
        yield return databasePath;
        yield return databasePath + "-wal";
        yield return databasePath + "-shm";
    }
}
