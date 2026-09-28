using System.Security.Cryptography;
using System.Text;
using Monica.Core.Models;

namespace Monica.Data.Mdbx;

public sealed partial class MdbxVaultStore
{
    // Native MDBX open performs the vault KDF. Reuse one serialized handle only
    // for the unlocked session, then dispose it on lock after any active lease ends.
    private readonly SemaphoreSlim _vaultSessionGate = new(1, 1);
    private IMdbxNativeVault? _cachedVault;
    private string? _cachedVaultPath;
    private byte[]? _cachedVaultCredentialFingerprint;
    private CancellationTokenRegistration _vaultSessionCancellationRegistration;
    private int _vaultSessionReleaseRequested;
    private int _disposed;

    private async Task<IMdbxNativeVault> OpenAsync(LocalMdbxDatabase database, CancellationToken cancellationToken)
    {
        var path = database.WorkingCopyPath ?? database.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("MDBX vault path is missing.");
        }

        if (string.IsNullOrWhiteSpace(database.EncryptedPassword))
        {
            throw new InvalidOperationException("MDBX vault password is missing.");
        }

        if (_vaultSessionService is null)
        {
            var ownedDecision = await AssessFileAccessAsync(path, cancellationToken);
            return new MdbxVaultLease(await OpenGatedVaultAsync(path, database.EncryptedPassword, ownedDecision, cancellationToken));
        }

        var sessionToken = _vaultSessionService.SessionCancellationToken;
        using var linkedCancellation = CreateLinkedCancellation(cancellationToken, sessionToken);
        var effectiveCancellationToken = linkedCancellation?.Token ??
            (cancellationToken.CanBeCanceled ? cancellationToken : sessionToken);
        await _vaultSessionGate.WaitAsync(effectiveCancellationToken);
        byte[]? credentialFingerprint = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_vaultSessionService.IsUnlocked)
            {
                throw new OperationCanceledException("The MDBX vault session is locked.", effectiveCancellationToken);
            }

            if (Interlocked.Exchange(ref _vaultSessionReleaseRequested, 0) != 0)
            {
                CloseCachedVault();
            }

            credentialFingerprint = CreateCredentialFingerprint(database.EncryptedPassword);
            if (_cachedVault is not null && !MatchesCachedVault(path, credentialFingerprint))
            {
                CloseCachedVault();
            }

            if (_cachedVault is null)
            {
                var decision = await AssessFileAccessAsync(path, effectiveCancellationToken);
                _cachedVault = await OpenGatedVaultAsync(path, database.EncryptedPassword, decision, effectiveCancellationToken);
                _cachedVaultPath = path;
                _cachedVaultCredentialFingerprint = credentialFingerprint;
                credentialFingerprint = null;
                _vaultSessionCancellationRegistration.Dispose();
                _vaultSessionCancellationRegistration = sessionToken.UnsafeRegister(
                    static state => ((MdbxVaultStore)state!).ReleaseVaultSession(),
                    this);
            }

            return new MdbxVaultLease(_cachedVault, this);
        }
        catch
        {
            _vaultSessionGate.Release();
            throw;
        }
        finally
        {
            if (credentialFingerprint is not null)
            {
                CryptographicOperations.ZeroMemory(credentialFingerprint);
            }
        }
    }

    /// <summary>
    /// What the last opened file was allowed to do, kept so the shell can tell the user the session is
    /// read-only instead of letting them discover it when a save fails. Null until a vault is opened.
    /// </summary>
    public MdbxNativeAccessDecision? LastVaultAccess { get; private set; }

    private async Task<MdbxNativeAccessDecision> AssessFileAccessAsync(string path, CancellationToken cancellationToken)
    {
        var decision = MdbxNativeFormatGate.Assess(
            await _nativeBridge.InspectMigrationAsync(path, cancellationToken),
            _nativeBridge.WritableStorageFormat,
            _nativeBridge.ReadableStorageFormats);
        LastVaultAccess = decision;
        return decision;
    }

    /// <summary>
    /// Opens with the gate's verdict already in hand. A refusal has to land before the engine is asked to
    /// open: opening runs the vault KDF and materializes native state, and spending that on a file this
    /// build has already declared unreadable buys nothing.
    /// </summary>
    private async Task<IMdbxNativeVault> OpenGatedVaultAsync(
        string path,
        string password,
        MdbxNativeAccessDecision decision,
        CancellationToken cancellationToken)
    {
        if (decision.Access == MdbxNativeVaultAccess.Refused)
        {
            throw new MdbxVaultFormatException(decision.Detail);
        }

        var vault = await _nativeBridge.OpenVaultAsync(path, password, DeviceId, cancellationToken);
        return decision.Access == MdbxNativeVaultAccess.ReadOnly
            ? new MdbxReadOnlyNativeVault(vault, decision)
            : vault;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ReleaseVaultSession();
        _vaultSessionCancellationRegistration.Dispose();
    }

    private void ReleaseVaultSession()
    {
        Interlocked.Exchange(ref _vaultSessionReleaseRequested, 1);
        if (!_vaultSessionGate.Wait(0))
        {
            return;
        }

        try
        {
            CloseCachedVault();
            Interlocked.Exchange(ref _vaultSessionReleaseRequested, 0);
        }
        finally
        {
            _vaultSessionGate.Release();
        }
    }

    private void ReleaseVaultLease()
    {
        try
        {
            if (Interlocked.Exchange(ref _vaultSessionReleaseRequested, 0) != 0)
            {
                CloseCachedVault();
            }
        }
        finally
        {
            _vaultSessionGate.Release();
        }
    }

    private bool MatchesCachedVault(string path, ReadOnlySpan<byte> credentialFingerprint) =>
        string.Equals(_cachedVaultPath, path, StringComparison.Ordinal) &&
        _cachedVaultCredentialFingerprint is not null &&
        CryptographicOperations.FixedTimeEquals(_cachedVaultCredentialFingerprint, credentialFingerprint);

    private void CloseCachedVault()
    {
        var vault = _cachedVault;
        _cachedVault = null;
        _cachedVaultPath = null;
        if (_cachedVaultCredentialFingerprint is not null)
        {
            CryptographicOperations.ZeroMemory(_cachedVaultCredentialFingerprint);
            _cachedVaultCredentialFingerprint = null;
        }

        vault?.Dispose();
    }

    private static byte[] CreateCredentialFingerprint(string credential)
    {
        var credentialBytes = Encoding.UTF8.GetBytes(credential);
        try
        {
            return SHA256.HashData(credentialBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialBytes);
        }
    }

    private static CancellationTokenSource? CreateLinkedCancellation(
        CancellationToken cancellationToken,
        CancellationToken sessionToken)
    {
        if (!cancellationToken.CanBeCanceled || cancellationToken == sessionToken)
        {
            return null;
        }

        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken);
    }

    private sealed class MdbxVaultLease : IMdbxNativeVault
    {
        private readonly IMdbxNativeVault _vault;
        private MdbxVaultStore? _owner;
        private int _disposed;

        public MdbxVaultLease(IMdbxNativeVault vault, MdbxVaultStore? owner = null)
        {
            _vault = vault;
            _owner = owner;
        }

        // The lease wraps whatever the format gate handed back, so the restriction travels with it.
        public bool IsReadOnly => _vault.IsReadOnly;

        public Task<MdbxNativeVaultInfo> GetInfoAsync(CancellationToken cancellationToken = default) =>
            _vault.GetInfoAsync(cancellationToken);

        public Task<MdbxNativeProjectRecord> CreateProjectAsync(string title, CancellationToken cancellationToken = default) =>
            _vault.CreateProjectAsync(title, cancellationToken);

        public Task<MdbxNativeProjectRecord> CreateProjectWithIdentityAsync(
            string projectId,
            string title,
            string? parentProjectId,
            CancellationToken cancellationToken = default) =>
            _vault.CreateProjectWithIdentityAsync(projectId, title, parentProjectId, cancellationToken);

        public Task<IReadOnlyList<MdbxNativeProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default) =>
            _vault.ListProjectsAsync(cancellationToken);

        public Task<MdbxNativeEntryRecord> CreateEntryAsync(
            string projectId,
            string entryType,
            string title,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            _vault.CreateEntryAsync(projectId, entryType, title, payloadJson, cancellationToken);

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListEntriesAsync(
            string projectId,
            string? entryType = null,
            CancellationToken cancellationToken = default) =>
            _vault.ListEntriesAsync(projectId, entryType, cancellationToken);

        public Task<IReadOnlyList<MdbxNativeEntryRecord>> ListDeletedEntriesAsync(
            string projectId,
            string? entryType = null,
            CancellationToken cancellationToken = default) =>
            _vault.ListDeletedEntriesAsync(projectId, entryType, cancellationToken);

        public Task<MdbxNativeEntryRecord> UpdateEntryAsync(
            string projectId,
            string entryId,
            string entryType,
            string title,
            string payloadJson,
            CancellationToken cancellationToken = default) =>
            _vault.UpdateEntryAsync(projectId, entryId, entryType, title, payloadJson, cancellationToken);

        public Task<MdbxNativeEntryRecord> MoveEntryAsync(
            string projectId,
            string entryId,
            string targetProjectId,
            CancellationToken cancellationToken = default) =>
            _vault.MoveEntryAsync(projectId, entryId, targetProjectId, cancellationToken);

        public Task DeleteEntryAsync(string projectId, string entryId, CancellationToken cancellationToken = default) =>
            _vault.DeleteEntryAsync(projectId, entryId, cancellationToken);

        public Task<MdbxNativeEntryRecord> RestoreEntryAsync(
            string projectId,
            string entryId,
            CancellationToken cancellationToken = default) =>
            _vault.RestoreEntryAsync(projectId, entryId, cancellationToken);

        public Task<MdbxNativeAttachmentRecord> CreateAttachmentAsync(
            string projectId,
            string? entryId,
            string fileName,
            string? mediaType,
            byte[] content,
            CancellationToken cancellationToken = default) =>
            _vault.CreateAttachmentAsync(projectId, entryId, fileName, mediaType, content, cancellationToken);

        public Task<IReadOnlyList<MdbxNativeAttachmentRecord>> ListAttachmentsAsync(
            string projectId,
            string? entryId,
            CancellationToken cancellationToken = default) =>
            _vault.ListAttachmentsAsync(projectId, entryId, cancellationToken);

        public Task<byte[]> ReadAttachmentContentAsync(
            string attachmentId,
            CancellationToken cancellationToken = default) =>
            _vault.ReadAttachmentContentAsync(attachmentId, cancellationToken);

        public Task DeleteAttachmentAsync(string attachmentId, CancellationToken cancellationToken = default) =>
            _vault.DeleteAttachmentAsync(attachmentId, cancellationToken);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                _vault.Dispose();
                return;
            }

            owner.ReleaseVaultLease();
        }
    }
}
