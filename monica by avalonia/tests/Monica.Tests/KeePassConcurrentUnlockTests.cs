using Monica.Platform.Services;

namespace Monica.Tests;

/// <summary>
/// KPCLib corrupts a master key when two unlocks are the first thing a process does: measured cold, a
/// burst of two and a burst of twelve each rejected the right password on every unlock, and the same
/// bursts passed once any single unlock had finished on its own. The platform now builds the key and
/// parses the file as one gated unit, so a right password stops being refused for arriving at the
/// same time as another.
/// </summary>
[Collection(KeePassVaultTestCollection.Name)]
public sealed class KeePassConcurrentUnlockTests
{
    private const string FixturePassword = "kdbx-parity-fixture-not-a-secret";
    private const string OtherPassword = "concurrent-unlock-fixture-not-a-secret";
    private const string FixtureName = "android-kotpass-v1.kdbx";
    private const int FixtureEntryCount = 2;
    private const int Rejected = -1;
    private const int FanOut = 8;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"monica-keepass-concurrent-{Guid.NewGuid():N}");

    [Fact]
    public async Task Concurrent_unlocks_take_the_right_password_and_still_refuse_the_wrong_one()
    {
        var content = ReadFixture();
        var service = new KeePassVaultService();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, FanOut).Select(index =>
            OpenOnceAsync(service, content, index % 2 == 0 ? FixturePassword : "not-the-password")));

        // Half of these share one key material and one moment: if the gate let them corrupt each
        // other, the right-password half is where the red shows. The wrong-password half keeps the
        // pass from going green by quietly returning the same session to everyone.
        Assert.Equal(FanOut / 2, outcomes.Count(outcome => outcome == FixtureEntryCount));
        Assert.Equal(FanOut / 2, outcomes.Count(outcome => outcome == Rejected));
    }

    [Fact]
    public async Task A_vault_created_while_another_is_unlocking_unlocks_afterwards()
    {
        Directory.CreateDirectory(_directory);
        try
        {
            var content = ReadFixture();
            var service = new KeePassVaultService();
            var target = Path.Combine(_directory, "created-while-unlocking.kdbx");

            var unlocked = service.OpenAsync(content, FixtureName, FixturePassword);
            var created = service.CreateAsync(Path.GetFileName(target), OtherPassword, target);
            using var first = await unlocked;
            using var second = await created;
            Assert.Equal(FixtureEntryCount, first.EntryCount);
            Assert.Equal(0, second.EntryCount);

            // The new file is only worth having if the password that made it opens it again.
            using var reopened = await service.OpenAsync(
                await File.ReadAllBytesAsync(target),
                Path.GetFileName(target),
                OtherPassword);
            Assert.Equal(0, reopened.EntryCount);
        }
        finally
        {
            TryDeleteDirectory();
        }
    }

    private static async Task<int> OpenOnceAsync(
        KeePassVaultService service,
        byte[] content,
        string password)
    {
        try
        {
            using var session = await service.OpenAsync(content, FixtureName, password);
            return session.EntryCount;
        }
        catch (KeePassVaultException fault) when (fault.Error == KeePassVaultError.InvalidCredentialsOrFile)
        {
            return Rejected;
        }
    }

    private static byte[] ReadFixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Kdbx", FixtureName));

    private void TryDeleteDirectory()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
