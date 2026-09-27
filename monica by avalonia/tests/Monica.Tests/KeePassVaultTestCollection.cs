namespace Monica.Tests;

/// <summary>
/// Every test that reads or writes a real KeePass database, run one at a time. Measured: the same set of
/// tests is fully green when scheduled sequentially and leaves exactly one database rejecting the key that
/// created it when the runner interleaves the classes. The library behind that handshake is not thread safe,
/// so the app serialises its own work on a process-wide lock; a test that calls the library directly has to
/// stand in the same queue or it reproduces the bug the app already fixed.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KeePassVaultTestCollection
{
    public const string Name = "KeePass vault";
}
