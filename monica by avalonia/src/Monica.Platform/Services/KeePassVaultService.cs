using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace Monica.Platform.Services;

public sealed class KeePassVaultService : IKeePassVaultService
{
    public Task<KeePassVaultSession> OpenAsync(
        ReadOnlyMemory<byte> content,
        string fileName,
        string? password,
        string? localPath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        KeePassVaultLimits.EnsureFileSize(content.Length);
        var safeFileName = KeePassVaultText.NormalizeFileName(fileName);
        var ciphertext = content.ToArray();
        var sourcePath = localPath?.Trim();
        return Task.Run(
            () => OpenCore(ciphertext, safeFileName, sourcePath, password, cancellationToken),
            cancellationToken);
    }

    public Task<KeePassVaultSession> CreateAsync(
        string fileName,
        string password,
        string? targetPath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A master key made only of spaces is a vault that looks protected on screen and is not one on
        // disk, so it is refused rather than trimmed into something else.
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var safeFileName = KeePassVaultText.NormalizeFileName(fileName);
        var destination = string.IsNullOrWhiteSpace(targetPath)
            ? null
            : KeePassVaultWrite.NormalizePath(targetPath);
        return Task.Run(
            () => CreateCore(safeFileName, password, destination, cancellationToken),
            cancellationToken);
    }

    private static async Task<KeePassVaultSession> CreateCore(
        string fileName,
        string password,
        string? targetPath,
        CancellationToken cancellationToken)
    {
        var (database, payload) = KeePassVaultCreate.Build(
            fileName,
            password,
            databaseName: Path.GetFileNameWithoutExtension(fileName));
        var ownershipTransferred = false;
        try
        {
            if (targetPath is { } path)
            {
                await KeePassVaultWrite.WriteAtomicAsync(path, payload, cancellationToken)
                    .ConfigureAwait(false);
            }

            var session = new KeePassVaultSession(database, fileName, targetPath, payload, cancellationToken);
            ownershipTransferred = true;
            return session;
        }
        finally
        {
            if (!ownershipTransferred && database.IsOpen)
            {
                database.Close();
            }
        }
    }

    private static KeePassVaultSession OpenCore(
        byte[] ciphertext,
        string fileName,
        string? sourcePath,
        string? password,
        CancellationToken cancellationToken)
    {
        var database = new PwDatabase();
        var ownershipTransferred = false;
        try
        {
            using var stream = new MemoryStream(ciphertext, writable: false);
            // Building the key and parsing the file go through the gate as one unit. With only the
            // parse gated, a cold process still refused the correct master key for every unlock in a
            // burst of two and of twelve, while a lone cold load always passed - the shared state
            // KPCLib touches first is reached before KdbxFile.Load is.
            lock (KeePassVaultParseGate.Gate)
            {
                var key = new CompositeKey();
                if (password is not null)
                {
                    key.AddUserKey(new KcpPassword(password));
                }

                database.MasterKey = key;
                new KdbxFile(database).Load(stream, KdbxFormat.Default, null);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var session = new KeePassVaultSession(database, fileName, sourcePath, ciphertext, cancellationToken);
            ownershipTransferred = true;
            return session;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeePassVaultException)
        {
            throw;
        }
        catch (OldFormatException)
        {
            throw KeePassVaultFaults.UnsupportedFormat();
        }
        catch (InvalidCompositeKeyException)
        {
            throw KeePassVaultFaults.InvalidFile();
        }
        catch (Exception)
        {
            throw KeePassVaultFaults.InvalidFile();
        }
        finally
        {
            if (!ownershipTransferred && database.IsOpen)
            {
                database.Close();
            }
        }
    }
}
