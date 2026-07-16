namespace Monica.Tests;

public sealed class TestTempPathsTests
{
    [Fact]
    public void Owned_directory_cleanup_removes_sqlite_sidecar_files()
    {
        var directory = TestTempPaths.CreateDirectoryPath();
        File.WriteAllText(Path.Combine(directory, "vault.db"), "database");
        File.WriteAllText(Path.Combine(directory, "vault.db-wal"), "wal");
        File.WriteAllText(Path.Combine(directory, "vault.db-shm"), "shm");

        TestTempPaths.DeleteOwnedDirectory(directory);

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Owned_directory_cleanup_rejects_paths_outside_test_root()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TestTempPaths.DeleteOwnedDirectory(Path.GetTempPath()));
    }
}
