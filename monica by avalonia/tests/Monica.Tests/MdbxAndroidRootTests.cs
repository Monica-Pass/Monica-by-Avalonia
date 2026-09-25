using Monica.Data.Mdbx;

namespace Monica.Tests;

/// <summary>
/// Android and the desktop each compute the root project id on their own, so one divergent nibble makes
/// Android write folder-less entries into a project the desktop never created and the other client's
/// tree silently splits. Every expected value below was printed by the Java runtime Android runs
/// (java.util.UUID.nameUUIDFromBytes over the UTF-8 bytes of "monica-root:" + vaultId), not by this
/// implementation, which is what makes the pair a cross-language contract rather than a tautology.
/// </summary>
public sealed class MdbxAndroidRootTests
{
    [Theory]
    [InlineData("test", "a422cc5f-e505-3b65-b298-fbf94de90dd8")]
    [InlineData("11111111-2222-3333-4444-555555555555", "79398328-0235-3a2c-bc17-95b995c87708")]
    [InlineData("fake-vault", "3e6c760b-147f-396b-af39-2f3704d48b81")]
    public void ProjectIdFor_matches_java_util_uuid_nameuuidfrombytes(string vaultId, string expected)
    {
        Assert.Equal(expected, MdbxAndroidRoot.ProjectIdFor(vaultId));
    }

    [Fact]
    public void ProjectIdFor_is_a_lowercase_uuid_with_the_v3_and_rfc4122_nibbles_stamped()
    {
        var projectId = MdbxAndroidRoot.ProjectIdFor("11111111-2222-3333-4444-555555555555");

        // Guid.Parse would accept a swapped-byte rendering of the same digits, so the text shape is the
        // assertion that matters: Android compares these ids as strings.
        Assert.Equal(projectId.ToLowerInvariant(), projectId);
        Assert.Equal(Guid.Parse(projectId).ToString("D").ToLowerInvariant(), projectId);
        Assert.Equal('3', projectId[14]);
        Assert.Contains(projectId[19], "89ab".ToCharArray());
    }

    [Theory]
    [InlineData(".monica-root")]
    [InlineData(" .monica-root ")]
    [InlineData(".MONICA-ROOT")]
    public void IsRootTitle_recognizes_the_title_android_writes(string title)
    {
        Assert.True(MdbxAndroidRoot.IsRootTitle(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Monica")]
    [InlineData("root")]
    public void IsRootTitle_leaves_user_folders_alone(string? title)
    {
        Assert.False(MdbxAndroidRoot.IsRootTitle(title));
    }
}
