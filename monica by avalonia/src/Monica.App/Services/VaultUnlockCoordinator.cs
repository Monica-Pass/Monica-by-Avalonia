using Monica.Core.Services;
using Monica.Data;

namespace Monica.App.Services;

public interface IVaultUnlockCoordinator
{
    Task<VaultInitializationState> InitializeAsync(CancellationToken cancellationToken = default);
    Task<VaultUnlockResult> UnlockOrCreateAsync(
        string masterPassword,
        string confirmMasterPassword,
        LegacyVaultDetection legacyVaultDetection,
        CancellationToken cancellationToken = default);
}

public sealed record VaultInitializationState(LegacyVaultDetection LegacyVaultDetection, bool IsVaultInitialized);

public sealed record VaultUnlockResult(
    VaultUnlockStatus Status,
    bool IsVaultInitialized,
    string MessageKey,
    Exception? Error = null,
    bool LegacyBusinessDataPending = false);

public enum VaultUnlockStatus
{
    CreatedAndUnlocked,
    Unlocked,
    MissingPassword,
    LegacyImportRequired,
    PasswordTooShort,
    ConfirmationMismatch,
    WrongPassword,
    Failed
}

public sealed class VaultUnlockCoordinator(
    IVaultCredentialStore credentialStore,
    ICryptoService cryptoService,
    ILegacyVaultDetector legacyVaultDetector,
    ICanonicalVaultBootstrapService? canonicalVaultBootstrapService = null) : IVaultUnlockCoordinator
{
    public async Task<VaultInitializationState> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var legacyVaultDetection = await legacyVaultDetector.DetectAsync(cancellationToken);
        if (legacyVaultDetection.RequiresImport)
        {
            return new VaultInitializationState(legacyVaultDetection, false);
        }

        var isVaultInitialized = await credentialStore.GetAsync(cancellationToken) is not null;
        return new VaultInitializationState(legacyVaultDetection, isVaultInitialized);
    }

    public async Task<VaultUnlockResult> UnlockOrCreateAsync(
        string masterPassword,
        string confirmMasterPassword,
        LegacyVaultDetection legacyVaultDetection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(masterPassword))
            {
                return new VaultUnlockResult(VaultUnlockStatus.MissingPassword, false, "EnterMasterPassword");
            }

            if (legacyVaultDetection.RequiresImport)
            {
                return new VaultUnlockResult(VaultUnlockStatus.LegacyImportRequired, false, "LegacyVaultImportRequired");
            }

            var storedHash = await credentialStore.GetAsync(cancellationToken);
            var created = false;
            if (storedHash is null)
            {
                if (!VaultMasterPasswordPolicy.MeetsMinimumLength(masterPassword))
                {
                    return new VaultUnlockResult(VaultUnlockStatus.PasswordTooShort, false, "MasterPasswordMinLength");
                }

                if (!string.Equals(masterPassword, confirmMasterPassword, StringComparison.Ordinal))
                {
                    return new VaultUnlockResult(VaultUnlockStatus.ConfirmationMismatch, false, "ConfirmationMismatch");
                }

                storedHash = cryptoService.HashMasterPassword(masterPassword);
                await credentialStore.SaveAsync(storedHash, cancellationToken);
                created = true;
            }

            if (!cryptoService.VerifyMasterPassword(masterPassword, storedHash))
            {
                return new VaultUnlockResult(VaultUnlockStatus.WrongPassword, true, "WrongMasterPassword");
            }

            var bootstrap = canonicalVaultBootstrapService is null
                ? null
                : await canonicalVaultBootstrapService.EnsureReadyAsync(cancellationToken);
            var legacyBusinessDataPending = bootstrap?.LegacyBusinessData.HasData == true;

            return new VaultUnlockResult(
                created ? VaultUnlockStatus.CreatedAndUnlocked : VaultUnlockStatus.Unlocked,
                true,
                legacyBusinessDataPending ? "VaultUnlockedLegacyBusinessDataPending" : "VaultUnlocked",
                LegacyBusinessDataPending: legacyBusinessDataPending);
        }
        catch (Exception ex)
        {
            return new VaultUnlockResult(VaultUnlockStatus.Failed, false, "VaultAccessUnlockFailed", ex);
        }
    }
}
