using Monica.App.ViewModels;
using Monica.Core.Services;

namespace Monica.Tests;

public sealed class PasswordSecretResolverTests
{
    [Fact]
    public void Read_returns_decrypted_secret_without_exposing_encrypted_payload()
    {
        var crypto = CreateUnlockedCrypto();
        var payload = crypto.EncryptString("current-secret");

        var result = PasswordSecretResolver.Read(payload, crypto);

        Assert.Equal(PasswordSecretState.Available, result.State);
        Assert.Equal(PasswordSecretOrigin.Encrypted, result.Origin);
        Assert.Equal("current-secret", result.Value);
    }

    [Fact]
    public void Read_keeps_compatible_plaintext_readable()
    {
        var crypto = CreateUnlockedCrypto();

        var result = PasswordSecretResolver.Read("legacy-secret", crypto);

        Assert.Equal(PasswordSecretState.Available, result.State);
        Assert.Equal(PasswordSecretOrigin.Plaintext, result.Origin);
        Assert.Equal("legacy-secret", result.Value);
    }

    [Fact]
    public void Read_marks_aes_gcm_shaped_decrypt_failure_unreadable()
    {
        var crypto = CreateUnlockedCrypto();
        var corruptPayload = Convert.ToBase64String(new byte[29]);

        var result = PasswordSecretResolver.Read(corruptPayload, crypto);

        Assert.Equal(PasswordSecretState.Unreadable, result.State);
        Assert.Equal(PasswordSecretOrigin.Encrypted, result.Origin);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Read_never_releases_nonempty_secret_while_vault_is_locked()
    {
        var result = PasswordSecretResolver.Read("legacy-secret", new CryptoService());

        Assert.Equal(PasswordSecretState.Locked, result.State);
        Assert.Equal(PasswordSecretOrigin.None, result.Origin);
        Assert.Empty(result.Value);
    }

    private static CryptoService CreateUnlockedCrypto()
    {
        var crypto = new CryptoService();
        crypto.InitializeSession("correct password", new byte[16]);
        return crypto;
    }
}
