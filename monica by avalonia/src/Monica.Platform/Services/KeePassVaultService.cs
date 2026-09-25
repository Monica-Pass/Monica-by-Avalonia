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
            var key = new CompositeKey();
            if (password is not null)
            {
                key.AddUserKey(new KcpPassword(password));
            }

            database.MasterKey = key;
            using var stream = new MemoryStream(ciphertext, writable: false);
            new KdbxFile(database).Load(stream, KdbxFormat.Default, null);
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
