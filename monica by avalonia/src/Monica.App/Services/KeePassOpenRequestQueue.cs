using System.IO;

namespace Monica.App.Services;

/// <summary>
/// How a .kdbx reaches the one instance that owns this data directory from outside its own window:
/// the command line of the launch that got there first, and a drop-zone on disk for every launch
/// that did not. Each request is its own file, so two double-clicks that arrive before the first is
/// read cannot overwrite one another, and a request left while the owner was still starting up waits
/// on disk instead of being dropped. What travels is a path and nothing else - the master password is
/// typed into the window that shows the file, exactly as it is for every other way in.
/// </summary>
internal sealed class KeePassOpenRequestQueue(string dataRootDirectory)
{
    private const string RequestExtension = ".req";
    private const string PendingExtension = ".req.tmp";

    private readonly string _directory = Path.Combine(dataRootDirectory, "pending-kdbx-open");

    /// <summary>
    /// Where the requests sit, said once rather than guessed at by anything that has to look there.
    /// </summary>
    internal string RequestDirectory => _directory;

    /// <summary>
    /// Reads the argument a double-clicked file or a drag onto the shortcut arrives as. Every argument
    /// this application defines for itself starts with a dash and takes any path it needs after that
    /// flag, so a .kdbx is only claimed here when it is the whole command line - which is what the
    /// registered file association sends, and keeps the smoke runs' own vault arguments theirs.
    /// </summary>
    public static string? TryReadCommandLinePath(string[]? args)
    {
        if (args is not { Length: > 0 })
        {
            return null;
        }

        var candidate = args[0];
        if (candidate.StartsWith('-') ||
            !string.Equals(Path.GetExtension(candidate), ".kdbx", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(candidate);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns false when the request could not be left behind, which is the caller's cue to say the
    /// running copy never got the file rather than exit as if it had.
    /// </summary>
    public bool TryEnqueue(string path)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            // Written to a name the reader never looks at, then moved: a drain either sees the whole
            // path or does not see the request at all.
            var name = $"{DateTimeOffset.UtcNow.UtcTicks:x16}-{Guid.NewGuid():N}";
            var pending = Path.Combine(_directory, name + PendingExtension);
            var ready = Path.Combine(_directory, name + RequestExtension);
            File.WriteAllText(pending, path);
            File.Move(pending, ready);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Takes every request that is waiting, oldest first, and consumes it whether or not the caller
    /// can act on it: a path nobody is going to open again should not be waiting on the next launch.
    /// </summary>
    public IReadOnlyList<string> Drain()
    {
        var paths = new List<string>();
        try
        {
            if (!Directory.Exists(_directory))
            {
                return paths;
            }

            foreach (var file in Directory.EnumerateFiles(_directory, "*" + RequestExtension)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                string? path = null;
                try
                {
                    var content = File.ReadAllText(file).Trim();
                    path = content.Length > 0 ? content : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gone, or not ours to read any more. Either way the request is over.
                }

                TryDelete(file);
                if (path is not null)
                {
                    paths.Add(path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return paths;
    }

    private void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
