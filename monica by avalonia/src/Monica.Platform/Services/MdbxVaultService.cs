using Monica.Core.Models;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Monica.Data.Mdbx;

namespace Monica.Platform.Services;

public sealed record MdbxVaultInspection(string Path, bool Exists, string FormatVersion, string VaultId, string Status);

public interface IMdbxVaultEngine
{
    Task CreateVaultAsync(string path, string password, MdbxTigaMode mode, CancellationToken cancellationToken = default);
    Task OpenVaultAsync(string path, string password, CancellationToken cancellationToken = default);
    Task<MdbxVaultInspection> InspectAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class MdbxVaultService(IMdbxVaultEngine? engine = null, IMdbxNativeBridge? nativeBridge = null) : IMdbxVaultService
{
    private const string ExpectedFormatVersion = "MDBX-1";
    private const string DeviceId = "monica-avalonia";
    private readonly IMdbxVaultEngine _engine = engine ?? new MdbxCliVaultEngine();
    private readonly IMdbxNativeBridge _nativeBridge = nativeBridge ?? new UnavailableMdbxNativeBridge();

    public async Task<LocalMdbxDatabase> CreateLocalMetadataAsync(string name, string filePath, MdbxTigaMode mode = MdbxTigaMode.Multi, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory);
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        MdbxVaultInspection inspection;
        if (_nativeBridge.IsAvailable)
        {
            using var vault = File.Exists(fullPath)
                ? await _nativeBridge.OpenVaultAsync(fullPath, password, DeviceId, cancellationToken)
                : await _nativeBridge.CreateVaultAsync(fullPath, password, DeviceId, mode, cancellationToken);
            inspection = await InspectNativeVaultAsync(fullPath, vault, cancellationToken);
        }
        else
        {
            if (!File.Exists(fullPath))
            {
                await _engine.CreateVaultAsync(fullPath, password, mode, cancellationToken);
            }

            inspection = await _engine.InspectAsync(fullPath, cancellationToken);
        }

        EnsureExpectedFormat(inspection);
        var database = new LocalMdbxDatabase
        {
            Name = name,
            FilePath = fullPath,
            StorageLocation = MdbxStorageLocation.Internal,
            SourceType = "LOCAL_INTERNAL",
            TigaMode = mode,
            EncryptedPassword = password,
            LastSyncStatus = SyncStatus.LocalOnly,
            WorkingCopyPath = fullPath,
            IsOfflineAvailable = true,
            KdfProfile = "mdbx-1/argon2id",
            Description = $"{ExpectedFormatVersion} vault {inspection.VaultId}",
            CreatedAt = DateTimeOffset.UtcNow,
            LastAccessedAt = DateTimeOffset.UtcNow
        };

        return database;
    }

    public async Task<Stream> OpenLocalStreamAsync(LocalMdbxDatabase database, CancellationToken cancellationToken = default)
    {
        var path = database.WorkingCopyPath ?? database.FilePath;
        if (string.IsNullOrWhiteSpace(database.EncryptedPassword))
        {
            throw new InvalidOperationException("MDBX vault password is missing from metadata.");
        }

        MdbxVaultInspection inspection;
        if (_nativeBridge.IsAvailable)
        {
            using var vault = await _nativeBridge.OpenVaultAsync(path, database.EncryptedPassword, DeviceId, cancellationToken);
            inspection = await InspectNativeVaultAsync(path, vault, cancellationToken);
        }
        else
        {
            await _engine.OpenVaultAsync(path, database.EncryptedPassword, cancellationToken);
            inspection = await _engine.InspectAsync(path, cancellationToken);
        }

        EnsureExpectedFormat(inspection);

        Stream stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        return stream;
    }

    private static async Task<MdbxVaultInspection> InspectNativeVaultAsync(string path, IMdbxNativeVault vault, CancellationToken cancellationToken)
    {
        var info = await vault.GetInfoAsync(cancellationToken);
        return new MdbxVaultInspection(path, true, ExpectedFormatVersion, info.VaultId, "Available");
    }

    private static void EnsureExpectedFormat(MdbxVaultInspection inspection)
    {
        if (!string.Equals(inspection.FormatVersion, ExpectedFormatVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected {ExpectedFormatVersion} vault, found '{inspection.FormatVersion}'.");
        }
    }
}

public sealed class MdbxCliVaultEngine : IMdbxVaultEngine
{
    private const string PasswordEnvironmentVariable = "MONICA_MDBX_PASSWORD";

    public async Task CreateVaultAsync(string path, string password, MdbxTigaMode mode, CancellationToken cancellationToken = default)
    {
        var result = await RunCliAsync(
            password,
            ["--vault", path, "init", "--tiga", ToCliMode(mode)],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"MDBX init failed: {result.ErrorOrOutput}");
        }
    }

    public async Task OpenVaultAsync(string path, string password, CancellationToken cancellationToken = default)
    {
        var result = await RunCliAsync(
            password,
            ["--vault", path, "health"],
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"MDBX open failed: {result.ErrorOrOutput}");
        }
    }

    public async Task<MdbxVaultInspection> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return new MdbxVaultInspection(path, false, "", "", "File not found");
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT vault_id, format_version FROM vault_meta LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new MdbxVaultInspection(path, true, "", "", "vault_meta row not found");
        }

        var vaultId = reader.GetString(0);
        var formatVersion = reader.GetString(1);
        return new MdbxVaultInspection(path, true, formatVersion, vaultId, "Available");
    }

    private static async Task<MdbxCliResult> RunCliAsync(string password, IReadOnlyList<string> commandArgs, CancellationToken cancellationToken)
    {
        var command = MdbxCliCommand.Resolve();
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in command.PrefixArguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var arg in commandArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment[PasswordEnvironmentVariable] = password;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start MDBX CLI.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        return new MdbxCliResult(process.ExitCode, output.Trim(), error.Trim());
    }

    private static string ToCliMode(MdbxTigaMode mode) => mode switch
    {
        MdbxTigaMode.Power => "power",
        MdbxTigaMode.Sky => "sky",
        _ => "multi"
    };

    private sealed record MdbxCliResult(int ExitCode, string Output, string Error)
    {
        public string ErrorOrOutput => string.IsNullOrWhiteSpace(Error) ? Output : Error;
    }

    private sealed record MdbxCliCommand(string FileName, string WorkingDirectory, IReadOnlyList<string> PrefixArguments)
    {
        public static MdbxCliCommand Resolve()
        {
            var explicitCli = Environment.GetEnvironmentVariable("MONICA_MDBX_CLI");
            if (!string.IsNullOrWhiteSpace(explicitCli))
            {
                return new MdbxCliCommand(explicitCli, Path.GetDirectoryName(explicitCli) ?? Environment.CurrentDirectory, []);
            }

            var workspace = FindMdbxWorkspace();
            if (workspace is not null)
            {
                var debugExe = Path.Combine(workspace, "target", "debug", OperatingSystem.IsWindows() ? "mdbx.exe" : "mdbx");
                if (File.Exists(debugExe))
                {
                    return new MdbxCliCommand(debugExe, workspace, []);
                }
            }

            return new MdbxCliCommand("mdbx", Environment.CurrentDirectory, []);
        }

        private static string? FindMdbxWorkspace()
        {
            var explicitWorkspace = Environment.GetEnvironmentVariable("MONICA_MDBX_WORKSPACE");
            if (IsMdbxWorkspace(explicitWorkspace))
            {
                return explicitWorkspace;
            }

            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory is not null)
                {
                    var candidate = Path.Combine(directory.FullName, "mdbx");
                    if (IsMdbxWorkspace(candidate))
                    {
                        return candidate;
                    }

                    if (directory.Name.Equals("Monica-by-Avalonia", StringComparison.OrdinalIgnoreCase))
                    {
                        var sibling = Path.Combine(directory.Parent?.FullName ?? "", "mdbx");
                        if (IsMdbxWorkspace(sibling))
                        {
                            return sibling;
                        }
                    }

                    directory = directory.Parent;
                }
            }

            return null;
        }

        private static bool IsMdbxWorkspace(string? path) =>
            !string.IsNullOrWhiteSpace(path) &&
            File.Exists(Path.Combine(path, "Cargo.toml")) &&
            Directory.Exists(Path.Combine(path, "crates", "mdbx-cli"));
    }
}
