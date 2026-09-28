using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using Monica.Data;

namespace Monica.App.Services;

/// The site icon a library row shows, taken from the same Google s2 endpoint the Android client uses
/// and kept entirely outside the vault: an icon belongs to a public host, not to an entry, so nothing
/// is written to the database and any machine can re-fetch what it never stored.
public static class WebsiteIconCache
{
    private const int MemoryCapacity = 50;
    private const int FetchAttempts = 3;
    private const int MaxIconBytes = 512 * 1024;
    private const int DiskTrimInterval = 64;
    private const int DiskFileBudget = 1000;
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(15);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Bitmap> Memory = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> MemoryOrder = new();
    private static readonly Dictionary<string, DateTimeOffset> CoolingDown = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Pending = new(StringComparer.Ordinal);
    private static int _writesSinceTrim;
    private static bool _userWantsIcons = true;
    private static bool _automatedRunAllowsNetwork = true;

    /// Raised when a switch flips, so rows already on screen re-decide instead of waiting for a rebuild.
    public static event Action? PreferencesChanged;

    /// Whether a row may show a picture at all. This gates the whole path, cache read included: the
    /// person's switch is a display choice, so a picture that is already on disk must not keep painting
    /// after they turn the feature off - that reads as a broken switch.
    public static bool IconsWanted
    {
        get
        {
            lock (Gate)
            {
                return _userWantsIcons;
            }
        }
    }

    /// Whether the path may leave this process. A measurement run closes it so a row never pays for a
    /// round trip, which is a different question from whether a picture already here may be shown.
    public static bool NetworkAllowed
    {
        get
        {
            lock (Gate)
            {
                return _userWantsIcons && _automatedRunAllowsNetwork;
            }
        }
    }

    /// The settings toggle. A gated run leaves this alone: the probe only closes the network.
    public static void SetUserWantsIcons(bool value)
    {
        if (SetIfChanged(ref _userWantsIcons, value))
        {
            PreferencesChanged?.Invoke();
        }
    }

    // The artifact gate and the UI suite measure this process, so they must not add a round trip to
    // every host they sample; with the path off they keep the type glyph they were written against.
    public static void SetAutomatedRunNetworkAllowed(bool value)
    {
        if (SetIfChanged(ref _automatedRunAllowsNetwork, value))
        {
            PreferencesChanged?.Invoke();
        }
    }

    /// Turns whatever a person typed in the website field into the host s2 is asked for, or refuses.
    /// Only the host a real URL parser found ever reaches the endpoint, so a value crafted to smuggle
    /// a path, a query or somebody else's authority into the request cannot become a request.
    public static bool TryExtractHost(string? website, out string host)
    {
        host = "";
        var raw = (website ?? "").Trim();
        if (raw.Length == 0
            || raw.Length > 300
            || raw.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || (!raw.Contains("://") && raw.Contains(':')))
        {
            return false;
        }

        // An '@' anywhere in the authority means the text is not one plain host, whatever a parser is
        // willing to make of it, so that part is looked at before anything is parsed.
        var authorityStart = raw.IndexOf("://", StringComparison.Ordinal) is var scheme and >= 0 ? scheme + 3 : 0;
        var authorityEnd = raw.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
        if (authorityEnd >= 0 ? raw[authorityStart..authorityEnd].Contains('@') : raw[authorityStart..].Contains('@'))
        {
            return false;
        }

        if (!Uri.TryCreate(raw.Contains("://") ? raw : "https://" + raw, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var candidate = uri.Host.ToLowerInvariant();
        if (candidate.StartsWith("www.", StringComparison.Ordinal))
        {
            candidate = candidate[4..];
        }

        if (!IsSingleLabelDomain(candidate))
        {
            return false;
        }

        host = candidate;
        return true;
    }

    public static bool TryGetCachedBitmap(string host, out Bitmap? bitmap)
    {
        lock (Gate)
        {
            if (Memory.TryGetValue(host, out var stored))
            {
                Promote(host);
                bitmap = stored;
                return true;
            }
        }

        var path = DiskPath(host);
        if (!File.Exists(path))
        {
            bitmap = null;
            return false;
        }

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            var loaded = new Bitmap(stream);
            lock (Gate)
            {
                Remember(host, loaded);
            }

            bitmap = loaded;
            return true;
        }
        catch (Exception)
        {
            TryDelete(path);
            bitmap = null;
            return false;
        }
    }

    /// Returns the icon for a host, or null when there is nothing to show. Callers on the UI thread
    /// should have tried TryGetCachedBitmap first: this only waits when the host must be fetched.
    public static async Task<Bitmap?> GetAsync(string host, CancellationToken cancellationToken)
    {
        if (TryGetCachedBitmap(host, out var cached))
        {
            return cached;
        }

        if (!NetworkAllowed || IsCoolingDown(host))
        {
            return null;
        }

        var pending = Pending.GetOrAdd(host, static key => CreateFetchTask(key));
        try
        {
            return await pending.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The row scrolled away; the fetch still runs to completion and fills the cache, so the
            // work of whoever next shows this host is already paid for.
            return null;
        }
    }

    private static Task<Bitmap?> CreateFetchTask(string key)
    {
        var task = Task.Run(() => FetchAsync(key));
        _ = task.ContinueWith(
            completed => Pending.TryRemove(new KeyValuePair<string, Task<Bitmap?>>(key, completed)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private static async Task<Bitmap?> FetchAsync(string host)
    {
        var requestUri = "https://www.google.com/s2/favicons?domain=" + Uri.EscapeDataString(host) + "&sz=64";
        for (var attempt = 1; attempt <= FetchAttempts; attempt++)
        {
            try
            {
                using var response = await Http.GetAsync(
                    requestUri, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
                if (!response.IsSuccessStatusCode)
                {
                    // A host s2 says no to will not change its answer if asked again right now.
                    if ((int)response.StatusCode < 500)
                    {
                        break;
                    }

                    throw new HttpRequestException($"s2 returned {(int)response.StatusCode}");
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (bytes.Length == 0
                    || bytes.Length > MaxIconBytes
                    || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                string? diskPath = null;
                lock (Gate)
                {
                    Remember(host, bitmap);
                    diskPath = WriteDisk(host, bitmap);
                }

                TrimDisk();
                return bitmap;
            }
            catch (Exception) when (attempt < FetchAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt));
            }
            catch (Exception)
            {
                break;
            }
        }

        lock (Gate)
        {
            CoolingDown[host] = DateTimeOffset.UtcNow + FailureCooldown;
        }

        return null;
    }

    private static bool IsSingleLabelDomain(string candidate)
    {
        if (candidate.Length is < 4 or > 253 || !candidate.Contains('.') || candidate.EndsWith('.'))
        {
            return false;
        }

        var labels = candidate.Split('.');
        if (labels[^1].Length < 2 || candidate.All(c => c == '.' || char.IsAsciiDigit(c)))
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length == 0
                || label.Length > 63
                || label.StartsWith('-')
                || label.EndsWith('-')
                || !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCoolingDown(string host)
    {
        lock (Gate)
        {
            if (!CoolingDown.TryGetValue(host, out var until))
            {
                return false;
            }

            if (until > DateTimeOffset.UtcNow)
            {
                return true;
            }

            CoolingDown.Remove(host);
            return false;
        }
    }

    private static void Remember(string host, Bitmap bitmap)
    {
        if (Memory.ContainsKey(host))
        {
            Promote(host);
            return;
        }

        Memory[host] = bitmap;
        MemoryOrder.AddFirst(host);
        while (MemoryOrder.Count > MemoryCapacity)
        {
            var oldest = MemoryOrder.Last!;
            MemoryOrder.RemoveLast();
            // A bound row still holds its bitmap, so an evicted icon is left for the finalizer rather
            // than disposed here; s2 pictures are 64px, so 50 of them are under a megabyte.
            Memory.Remove(oldest.Value);
        }
    }

    private static void Promote(string host)
    {
        var node = MemoryOrder.Find(host);
        if (node is not null && !ReferenceEquals(node, MemoryOrder.First))
        {
            MemoryOrder.Remove(node);
            MemoryOrder.AddFirst(node);
        }
    }

    // Internal so a test can place a picture where this cache itself would look for it, rather than
    // repeating the naming rule and drifting from it.
    internal static string DiskPath(string host)
    {
        // The version tag is inside the hashed name, so a change of what is stored invalidates every
        // file that the previous shape left behind.
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v2:" + host)))
            .ToLowerInvariant() + ".png";
        return Path.Combine(MonicaAppDataPaths.GetPath("favicons"), name);
    }

    private static string? WriteDisk(string host, Bitmap bitmap)
    {
        var target = DiskPath(host);
        if (File.Exists(target))
        {
            return null;
        }

        // Written beside a name nobody is reading, then moved in, so a row that reads the cache during
        // a save never sees a half-written picture.
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var stream = File.Create(temporary))
            {
                bitmap.Save(stream, new PngBitmapEncoderOptions());
            }

            File.Move(temporary, target, overwrite: true);
            return target;
        }
        catch (Exception)
        {
            TryDelete(temporary);
            return null;
        }
    }

    private static void TrimDisk()
    {
        if (Interlocked.Increment(ref _writesSinceTrim) % DiskTrimInterval != 0)
        {
            return;
        }

        try
        {
            var directory = MonicaAppDataPaths.GetPath("favicons");
            if (!Directory.Exists(directory))
            {
                return;
            }

            var excess = Directory.EnumerateFiles(directory, "*.png")
                .Select(path => (path, written: File.GetLastWriteTimeUtc(path)))
                .OrderByDescending(item => item.written)
                .Skip(DiskFileBudget);
            foreach (var (path, _) in excess)
            {
                TryDelete(path);
            }
        }
        catch (Exception)
        {
            // The cache is allowed to be this run's only failure.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }

    private static bool SetIfChanged(ref bool field, bool value)
    {
        lock (Gate)
        {
            if (field == value)
            {
                return false;
            }

            field = value;
            return true;
        }
    }
}
