namespace Monica.Platform.Services;

/// <summary>
/// One process-wide gate around every KdbxFile parse and serialize. Neither direction is thread safe:
/// parallel saves have produced databases that reject the key that created them, and a cold process
/// asked to parse two vaults at once rejected the correct master key for <i>every</i> unlock in the
/// burst (measured 2/2 and 12/12 refused, twice each), while the same burst passed 12/12 once one
/// unlock had completed on its own. Opening and saving a vault happen on a user action, never in a
/// loop, so a queued unlock waits at most one derivation - and a correct password stops being called
/// wrong.
/// </summary>
internal static class KeePassVaultParseGate
{
    public static readonly object Gate = new();
}
