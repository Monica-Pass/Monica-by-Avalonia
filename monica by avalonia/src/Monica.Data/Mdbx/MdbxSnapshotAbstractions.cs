namespace Monica.Data.Mdbx;

/// <summary>
/// Copies encrypted vault files without changing the source. Validation may open and change only
/// an operation-owned staging copy; callers must never pass an original selected file for validation.
/// </summary>
public interface IMdbxNativeSnapshotBridge
{
    Task<MdbxNativeBackupInfo> CreatePortableBackupAsync(
        string sourcePath,
        string destination,
        CancellationToken cancellationToken = default);

    Task<MdbxNativeSnapshotValidation> ValidateSnapshotAsync(
        string stagingPath,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed record MdbxNativeBackupInfo(
    string VaultId,
    string FormatVersion,
    uint SchemaVersion,
    ulong FileSizeBytes);

public sealed record MdbxNativeSnapshotValidation(
    string VaultId,
    string DeviceId,
    bool HasExternalBlobReferences);

/// <summary>
/// A stable failure code without the native exception message, payload, credential, or selected path.
/// </summary>
public sealed class MdbxSnapshotException(string reasonCode) :
    InvalidOperationException($"MDBX snapshot operation failed ({reasonCode}).")
{
    public string ReasonCode { get; } = reasonCode;
}
