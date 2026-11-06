using Monica.Data;

namespace Monica.Tests;

public sealed class MonicaAppDataPathsTests
{
    [Fact]
    public void A_new_data_directory_is_private_to_its_owner()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(TestTempPaths.CreateDirectoryPath(), "fresh", "appdata");

        MonicaAppDataPaths.EnsurePrivateDirectory(directory);

        Assert.True(Directory.Exists(directory));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(directory));
    }

    /// <summary>
    /// Installs from before this fix created the directory 0755, which let any local account copy
    /// monica.db. The next start has to close that, not only new installs.
    /// </summary>
    [Fact]
    public void An_existing_world_readable_data_directory_is_tightened()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(TestTempPaths.CreateDirectoryPath(), "legacy");
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(
            directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        MonicaAppDataPaths.EnsurePrivateDirectory(directory);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(directory));
    }

    [Fact]
    public void A_leading_tilde_in_the_override_means_the_home_directory_on_unix()
    {
        var expanded = MonicaAppDataPaths.ExpandOverride("~/monica-vault");

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("~/monica-vault", expanded);
            return;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home + "/monica-vault", expanded);
        Assert.Equal("relative/~/kept", MonicaAppDataPaths.ExpandOverride("relative/~/kept"));
    }
}
