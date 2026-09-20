using System.Security.Cryptography;
using Monica.Core.Native;

namespace Monica.Tests;

public sealed class NativeCryptoTests
{
    [Fact]
    public void Argon2id_matches_known_test_vector()
    {
        var password = "password"u8.ToArray();
        var salt = "somesalt"u8.ToArray();
        var output = new byte[32];

        MonicaCryptoNative.DeriveArgon2id(password, salt, 2, 32, 1, output);

        Assert.Equal(
            "31111cc053ba0a799c0884148fd7ec9dc3631f3e8cf476cca9521d4ccc5136e8",
            Convert.ToHexString(output).ToLowerInvariant());
    }

    [Fact]
    public void Argon2id_matches_bitwarden_protocol_vector()
    {
        var password = "correct horse battery staple"u8.ToArray();
        var salt = SHA256.HashData("alice@example.com"u8);
        var output = new byte[32];

        MonicaCryptoNative.DeriveArgon2id(password, salt, 2, 32 * 1024, 2, output);

        Assert.Equal(
            "de42814dd2661793c529bd83e324eac04f9ede84dcc8fdce4e20adba8ce4db2f",
            Convert.ToHexString(output).ToLowerInvariant());
    }

    [Fact]
    public void Argon2id_rejects_zero_iterations()
    {
        var output = new byte[32];
        Assert.Throws<InvalidOperationException>(() =>
            MonicaCryptoNative.DeriveArgon2id("pw"u8, "salt"u8, 0, 32, 1, output));
    }

    [Fact]
    public void Argon2id_rejects_zero_parallelism()
    {
        var output = new byte[32];
        Assert.Throws<InvalidOperationException>(() =>
            MonicaCryptoNative.DeriveArgon2id("pw"u8, "salt"u8, 2, 32, 0, output));
    }

    [Fact]
    public void Argon2id_rejects_wrong_output_length()
    {
        var output = new byte[16];
        Assert.Throws<ArgumentException>(() =>
            MonicaCryptoNative.DeriveArgon2id("pw"u8, "salt"u8, 2, 32, 1, output));
    }

    [Fact]
    public void Native_library_is_available()
    {
        Assert.True(MonicaCryptoNative.IsAvailable,
            $"Native crypto library should be available but got: {MonicaCryptoNative.AvailabilityError}");
    }
}
