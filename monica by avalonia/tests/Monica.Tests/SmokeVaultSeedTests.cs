using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Monica.Data;
using Monica.Data.Repositories;
using Monica.Platform.Services;

namespace Monica.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SmokeVaultSeedTestCollection
{
    public const string Name = "Smoke vault seed";
}

[Collection(SmokeVaultSeedTestCollection.Name)]
public sealed class SmokeVaultSeedTests
{
    [Fact]
    public async Task Smoke_vault_seed_populates_canonical_mdbx_without_legacy_business_rows()
    {
        var root = Directory.CreateTempSubdirectory("monica-smoke-seed-");
        const string password = "Monica-Smoke-Seed-2026!";
        Process? process = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(typeof(Monica.App.App).Assembly.Location);
            startInfo.ArgumentList.Add("--seed-smoke-vault");
            startInfo.ArgumentList.Add(password);
            startInfo.Environment[MonicaAppDataPaths.OverrideEnvironmentVariable] = root.FullName;

            process = Process.Start(startInfo);
            Assert.NotNull(process);
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
            var output = await standardOutput;
            var error = await standardError;
            Assert.True(
                process.ExitCode == 0,
                $"Smoke seed failed with exit code {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");

            var databasePath = Path.Combine(root.FullName, "monica.db");
            var factory = new SqliteConnectionFactory(databasePath);
            var migrator = new DatabaseMigrator(factory);
            var sqliteRepository = new MonicaRepository(factory, migrator);
            var database = Assert.Single(await sqliteRepository.GetMdbxDatabasesAsync());
            Assert.True(database.IsDefault);
            Assert.True(File.Exists(database.WorkingCopyPath ?? database.FilePath));

            var legacyBusinessData = await new LegacyBusinessDataInspector(factory, migrator).InspectAsync();
            Assert.False(legacyBusinessData.HasData);

            var bridge = new MdbxUniffiNativeBridge();
            Assert.True(bridge.IsAvailable);
            using var vault = await bridge.OpenVaultAsync(
                database.WorkingCopyPath ?? database.FilePath,
                database.EncryptedPassword!,
                "monica-smoke-seed-test");
            var projects = await vault.ListProjectsAsync(includeDeleted: false);
            var entryCount = 0;
            foreach (var project in projects)
            {
                entryCount += (await vault.ListEntriesAsync(project.ProjectId)).Count;
            }

            Assert.True(entryCount >= 30, $"Expected canonical smoke entries, found {entryCount}.");
            Assert.Contains(projects, project => project.Title.Contains("Smoke/Work", StringComparison.Ordinal));
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            process?.Dispose();
            SqliteConnection.ClearAllPools();
            root.Delete(recursive: true);
        }
    }
}
