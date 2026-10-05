using Monica.Data.Mdbx;

namespace Monica.Tests;

public sealed class MdbxObjectReadPolicyTests
{
    [Theory]
    [InlineData("ssh-key")]
    [InlineData("identity")]
    public void Known_legacy_aliases_require_the_supported_payload_version(string entryType)
    {
        Assert.True(MdbxObjectReadPolicy.Supports(entryType, 1));
        Assert.False(MdbxObjectReadPolicy.Supports(entryType, 0));
        Assert.False(MdbxObjectReadPolicy.Supports(entryType, 2));
        Assert.False(MdbxObjectReadPolicy.Supports(entryType.ToUpperInvariant(), 1));
    }

    [Theory]
    [InlineData("passkey")]
    [InlineData("api-token")]
    [InlineData("steam-mafile")]
    [InlineData("custom-identity")]
    public void Unknown_native_types_stay_outside_the_business_write_path(string entryType) =>
        Assert.False(MdbxObjectReadPolicy.Supports(entryType, 1));
}
