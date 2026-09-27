using System.Globalization;

namespace Monica.App.Services;

/// <summary>
/// The one place the byte limit on an entry's history is turned into the number a person reads and
/// types. Every other KeePass client asks for this limit in megabytes - Android's field is literally
/// <c>historyMaxSizeMb</c>, and its display is integer division - so the desktop says it the same way
/// instead of putting <c>6291456</c> on the screen and leaving the unit to guesswork.
/// </summary>
public static class KeePassHistorySizeUnits
{
    public const long BytesPerMegabyte = 1024 * 1024;

    /// <summary>
    /// 2047. The client that shares this file holds the limit in a 32-bit int, so a larger value would
    /// be a number the other end cannot carry. Refusing it here is what keeps a desktop-written
    /// database readable to it rather than one it has to clamp or reject.
    /// </summary>
    public const long MaximumMegabytes = int.MaxValue / BytesPerMegabyte;

    /// <summary>
    /// What the box shows. Truncating, exactly like the other client: a limit of 1500 KB is a bit over
    /// one megabyte, and the box says 1 rather than inventing a decimal no client spells.
    /// </summary>
    public static string ToDisplayMegabytes(long bytes) =>
        bytes < 0
            ? "-1"
            : (bytes / BytesPerMegabyte).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// What the box means. <paramref name="fileBytes"/> is the limit the database holds right now, or
    /// null when none is open, and it survives untouched when the box still shows its own truncated
    /// reading: the display is a lossy view, so "6" coming back as 6 MiB rather than the file's
    /// 6291456 bytes would be the screen rewriting a number nobody edited. Anything else is converted,
    /// which is the point of typing a different number in the first place.
    /// </summary>
    public static bool TryParseMegabytes(string text, long? fileBytes, out long bytes)
    {
        bytes = 0;
        // Integers only, and invariant: a decimal or a thousands separator would have to become a byte
        // count no other client writes, and a comma read as a separator is a unit error waiting to land.
        if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var megabytes)
            || megabytes < -1
            || megabytes > MaximumMegabytes)
        {
            return false;
        }

        if (megabytes < 0)
        {
            bytes = -1;
            return true;
        }

        if (fileBytes is { } exact && exact >= 0 && exact / BytesPerMegabyte == megabytes)
        {
            bytes = exact;
            return true;
        }

        bytes = megabytes * BytesPerMegabyte;
        return true;
    }
}
