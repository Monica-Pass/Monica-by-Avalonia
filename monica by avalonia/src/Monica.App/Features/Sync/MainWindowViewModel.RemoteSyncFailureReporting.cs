namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void ReportRemoteSyncFailure(
        string diagnosticMessage,
        string userMessageKey,
        Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return;
        }

        AppDiagnostics.Error(diagnosticMessage, exception);
        SetStatusFailure(exception switch
        {
            Monica.Data.Mdbx.MdbxVaultReadOnlyException { ReasonCode: "unsupported-vault-objects" } =>
                "MdbxUnsupportedObjectsProtected",
            Monica.Data.Mdbx.MdbxSnapshotException snapshot => GetMdbxSnapshotFailureKey(snapshot.ReasonCode),
            _ => userMessageKey
        });
    }

    private static string GetMdbxSnapshotFailureKey(string reasonCode) => reasonCode switch
    {
        "native-unavailable" => "MdbxSnapshotNativeUnavailable",
        "coordination-unavailable" => "MdbxSnapshotCoordinationUnavailable",
        "validation-failed" => "MdbxSnapshotValidationFailed",
        "credential-or-integrity" => "MdbxSnapshotCredentialOrIntegrity",
        "mismatched-vault" => "MdbxSnapshotMismatchedVault",
        "identity-unavailable" => "MdbxSnapshotIdentityUnavailable",
        "external-blobs" => "MdbxSnapshotExternalBlobs",
        "unsupported-format" => "MdbxSnapshotUnsupportedFormat",
        "destination-exists" => "MdbxSnapshotDestinationExists",
        "vault-busy" => "MdbxSnapshotVaultBusy",
        "unsaved-edits" => "MdbxSnapshotUnsavedEdits",
        "rollback-failed" => "MdbxSnapshotRollbackFailed",
        _ => "MdbxSnapshotOperationFailed"
    };
}
