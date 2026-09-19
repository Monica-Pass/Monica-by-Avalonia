namespace Monica.Data.Bitwarden;

public sealed partial class BitwardenAccountStore
{
    private const string AccountSelect =
        """
        SELECT id AS Id,
               email AS Email,
               user_id AS UserId,
               account_key AS AccountKey,
               display_name AS DisplayName,
               server_url AS ServerUrl,
               identity_url AS IdentityUrl,
               api_url AS ApiUrl,
               events_url AS EventsUrl,
               access_token_expires_at AS AccessTokenExpiresAt,
               kdf_type AS KdfType,
               kdf_iterations AS KdfIterations,
               kdf_memory AS KdfMemory,
               kdf_parallelism AS KdfParallelism,
               last_sync_at AS LastSyncAt,
               last_full_sync_at AS LastFullSyncAt,
               revision_date AS RevisionDate,
               last_sync_status AS LastSyncStatus,
               last_sync_error AS LastSyncError,
               tls_mode AS TlsMode,
               custom_ca_certificate_path AS CustomCaCertificatePath,
               client_certificate_path AS ClientCertificatePath,
               is_default AS IsDefault,
               is_connected AS IsConnected,
               sync_enabled AS SyncEnabled,
               created_at AS CreatedAt,
               updated_at AS UpdatedAt
        FROM bitwarden_vaults
        """;

    private const string SecretSelect =
        """
        SELECT encrypted_access_token AS EncryptedAccessToken,
               encrypted_refresh_token AS EncryptedRefreshToken,
               encrypted_master_key AS EncryptedMasterKey,
               encrypted_enc_key AS EncryptedEncKey,
               encrypted_mac_key AS EncryptedMacKey,
               encrypted_client_certificate_password AS EncryptedClientCertificatePassword
        FROM bitwarden_vaults
        """;

    private const string SaveConnectedSql =
        """
        INSERT INTO bitwarden_vaults (
            email, canonical_email, user_id, account_key, display_name,
            server_url, identity_url, api_url, events_url,
            encrypted_access_token, encrypted_refresh_token, access_token_expires_at,
            encrypted_master_key, encrypted_enc_key, encrypted_mac_key,
            kdf_type, kdf_iterations, kdf_memory, kdf_parallelism,
            last_sync_at, last_full_sync_at, revision_date, last_sync_status, last_sync_error,
            tls_mode, custom_ca_certificate_path, client_certificate_path,
            encrypted_client_certificate_password,
            is_default, is_locked, is_connected, sync_enabled, created_at, updated_at)
        VALUES (
            @Email, @CanonicalEmail, @UserId, @AccountKey, @DisplayName,
            @ServerUrl, @IdentityUrl, @ApiUrl, @EventsUrl,
            @EncryptedAccessToken, @EncryptedRefreshToken, @AccessTokenExpiresAt,
            @EncryptedMasterKey, @EncryptedEncKey, @EncryptedMacKey,
            @KdfType, @KdfIterations, @KdfMemory, @KdfParallelism,
            @LastSyncAt, @LastFullSyncAt, @RevisionDate, @LastSyncStatus, @LastSyncError,
            @TlsMode, @CustomCaCertificatePath, @ClientCertificatePath,
            @EncryptedClientCertificatePassword,
            @IsDefault, 1, 1, @SyncEnabled, @Now, @Now)
        ON CONFLICT(account_key) DO UPDATE SET
            email = excluded.email,
            canonical_email = excluded.canonical_email,
            user_id = excluded.user_id,
            display_name = excluded.display_name,
            server_url = excluded.server_url,
            identity_url = excluded.identity_url,
            api_url = excluded.api_url,
            events_url = excluded.events_url,
            encrypted_access_token = excluded.encrypted_access_token,
            encrypted_refresh_token = excluded.encrypted_refresh_token,
            access_token_expires_at = excluded.access_token_expires_at,
            encrypted_master_key = excluded.encrypted_master_key,
            encrypted_enc_key = excluded.encrypted_enc_key,
            encrypted_mac_key = excluded.encrypted_mac_key,
            kdf_type = excluded.kdf_type,
            kdf_iterations = excluded.kdf_iterations,
            kdf_memory = excluded.kdf_memory,
            kdf_parallelism = excluded.kdf_parallelism,
            last_sync_at = excluded.last_sync_at,
            last_full_sync_at = excluded.last_full_sync_at,
            revision_date = excluded.revision_date,
            last_sync_status = excluded.last_sync_status,
            last_sync_error = excluded.last_sync_error,
            tls_mode = excluded.tls_mode,
            custom_ca_certificate_path = excluded.custom_ca_certificate_path,
            client_certificate_path = excluded.client_certificate_path,
            encrypted_client_certificate_password = excluded.encrypted_client_certificate_password,
            is_default = excluded.is_default,
            is_locked = 1,
            is_connected = 1,
            sync_enabled = excluded.sync_enabled,
            updated_at = excluded.updated_at
        RETURNING id AS Id, created_at AS CreatedAt
        """;

    private sealed record ProtectedAccountValues(
        string Email,
        string? UserId,
        string? DisplayName,
        string AccessToken,
        string RefreshToken,
        string MasterKey,
        string EncryptionKey,
        string MacKey,
        string? ClientCertificatePassword,
        string? LastSyncError,
        string? CustomCaCertificatePath,
        string? ClientCertificatePath);

    private sealed class SavedAccountRow
    {
        public long Id { get; init; }
        public long CreatedAt { get; init; }
    }

    private sealed class BitwardenSecretRow
    {
        public string? EncryptedAccessToken { get; init; }
        public string? EncryptedRefreshToken { get; init; }
        public string? EncryptedMasterKey { get; init; }
        public string? EncryptedEncKey { get; init; }
        public string? EncryptedMacKey { get; init; }
        public string? EncryptedClientCertificatePassword { get; init; }
    }

    private sealed class BitwardenAccountRow
    {
        public long Id { get; init; }
        public string Email { get; init; } = "";
        public string? UserId { get; init; }
        public string AccountKey { get; init; } = "";
        public string? DisplayName { get; init; }
        public string ServerUrl { get; init; } = "";
        public string IdentityUrl { get; init; } = "";
        public string ApiUrl { get; init; } = "";
        public string? EventsUrl { get; init; }
        public long? AccessTokenExpiresAt { get; init; }
        public int KdfType { get; init; }
        public int KdfIterations { get; init; }
        public int? KdfMemory { get; init; }
        public int? KdfParallelism { get; init; }
        public long? LastSyncAt { get; init; }
        public long? LastFullSyncAt { get; init; }
        public string? RevisionDate { get; init; }
        public string LastSyncStatus { get; init; } = "never";
        public string? LastSyncError { get; init; }
        public string TlsMode { get; init; } = "system";
        public string? CustomCaCertificatePath { get; init; }
        public string? ClientCertificatePath { get; init; }
        public bool IsDefault { get; init; }
        public bool IsConnected { get; init; }
        public bool SyncEnabled { get; init; }
        public long CreatedAt { get; init; }
        public long UpdatedAt { get; init; }
    }
}
