namespace Monica.Data.Mdbx;

/// <summary>What this client may do with the file it just inspected.</summary>
public enum MdbxNativeVaultAccess
{
    /// <summary>The file declares exactly what this build writes, so a mutation is safe.</summary>
    ReadWrite,

    /// <summary>The file is readable but was not authored by this build's writer. Writing it back would
    /// restamp or migrate someone else's file, so only reads are allowed.</summary>
    ReadOnly,

    /// <summary>The file declares something this build cannot interpret; opening it at all risks reading
    /// data wrong.</summary>
    Refused
}

/// <summary>
/// Decides what the desktop may do with an MDBX file from the header the <em>file</em> declares, rather
/// than from this build's own constants. Comparing the runtime's writable format against itself always
/// passes, which is what the previous check did; these fields come from the engine reading the file.
/// </summary>
public static class MdbxNativeFormatGate
{
    public static MdbxNativeAccessDecision Assess(
        MdbxNativeMigrationInfo? info,
        string writableStorageFormat,
        IReadOnlyList<string> readableStorageFormats)
    {
        // Nothing to gate on: the engine could not read a header. The vault handle itself is the
        // authority after a successful open, so this only declines to add a restriction.
        if (info is null)
        {
            return MdbxNativeAccessDecision.Writable();
        }

        var format = info.FormatVersion ?? "";
        var readable = readableStorageFormats.Any(candidate => string.Equals(candidate, format, StringComparison.Ordinal));
        if (!info.Initialized)
        {
            return MdbxNativeAccessDecision.Refused("not-initialized", Describe(info, writableStorageFormat));
        }

        if (!readable)
        {
            return MdbxNativeAccessDecision.Refused("format-unreadable", Describe(info, writableStorageFormat));
        }

        if (info.UnknownCriticalExtensions)
        {
            return MdbxNativeAccessDecision.ReadOnly("unknown-critical-extensions", Describe(info, writableStorageFormat));
        }

        if (info.RequiresUpgrade)
        {
            return MdbxNativeAccessDecision.ReadOnly("requires-upgrade", Describe(info, writableStorageFormat));
        }

        if (info.SchemaVersion is { } schema && info.TargetSchemaVersion is { } target && schema > target)
        {
            return MdbxNativeAccessDecision.ReadOnly("schema-newer", Describe(info, writableStorageFormat));
        }

        if (!string.Equals(format, writableStorageFormat, StringComparison.Ordinal))
        {
            return MdbxNativeAccessDecision.ReadOnly("format-not-writable", Describe(info, writableStorageFormat));
        }

        return MdbxNativeAccessDecision.Writable();
    }

    // Format names and version strings are engine constants, not user data, so they are safe to surface.
    private static string Describe(MdbxNativeMigrationInfo info, string writableStorageFormat) =>
        $"file={info.FormatVersion ?? "?"} schema={info.SchemaVersion?.ToString() ?? "?"} " +
        $"reader={info.MinReaderVersion ?? "?"} writer={info.MinWriterVersion ?? "?"} " +
        $"target={info.TargetFormatVersion}/{info.TargetSchemaVersion} this-build-writes={writableStorageFormat}";
}

/// <summary>The gate's verdict, with a stable reason code callers can map onto their own copy.</summary>
public sealed record MdbxNativeAccessDecision(MdbxNativeVaultAccess Access, string? ReasonCode, string Detail)
{
    public bool AllowsWrites => Access == MdbxNativeVaultAccess.ReadWrite;

    public static MdbxNativeAccessDecision Writable() =>
        new(MdbxNativeVaultAccess.ReadWrite, null, "");

    public static MdbxNativeAccessDecision ReadOnly(string reasonCode, string detail) =>
        new(MdbxNativeVaultAccess.ReadOnly, reasonCode, detail);

    public static MdbxNativeAccessDecision Refused(string reasonCode, string detail) =>
        new(MdbxNativeVaultAccess.Refused, reasonCode, detail);
}

/// <summary>Raised when a mutation is attempted against a session the gate limited to reads.</summary>
public sealed class MdbxVaultReadOnlyException(string reasonCode, string detail) :
    InvalidOperationException($"This vault is open read-only ({reasonCode}): {detail}")
{
    public string ReasonCode { get; } = reasonCode;
}

/// <summary>Raised when the file declares a format this build cannot interpret, so it is not opened.</summary>
public sealed class MdbxVaultFormatException(string detail) :
    InvalidOperationException($"This vault declares a format this build cannot read: {detail}");
