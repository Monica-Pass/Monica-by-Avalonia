using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Monica.App.Services;

namespace Monica.App.Controls;

/// The site icon slot of a row. The tree virtualizes, so the fetch starts when a row is realized and is
/// dropped when it scrolls away again: a vault of thousands asks for the icons on screen, the way Android
/// loads one row at a time, and a row that cannot show a picture keeps the type glyph underneath it.
public sealed class WebsiteIconImage : Image
{
    public static readonly StyledProperty<string?> HostProperty =
        AvaloniaProperty.Register<WebsiteIconImage, string?>(nameof(Host));

    private CancellationTokenSource? _pending;
    private bool _attachedToVisualTree;

    public string? Host
    {
        get => GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HostProperty && _attachedToVisualTree)
        {
            Reload();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attachedToVisualTree = true;
        WebsiteIconCache.PreferencesChanged += Reload;
        Reload();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attachedToVisualTree = false;
        WebsiteIconCache.PreferencesChanged -= Reload;
        CancelPending();
    }

    private void Reload()
    {
        CancelPending();
        Source = null;
        IsVisible = false;

        // Asked before the cache is read. The switch is about what a person sees, so a picture that is
        // already on disk must not keep painting after they turn the feature off.
        if (!WebsiteIconCache.IconsWanted)
        {
            return;
        }

        if (Host is not { Length: > 0 } host)
        {
            return;
        }

        if (WebsiteIconCache.TryGetCachedBitmap(host, out var cached))
        {
            Show(cached);
            return;
        }

        // A run that is being measured keeps its rows on the type glyph instead of paying a round trip
        // per host; the picture that is already here above needed no such permission.
        if (!WebsiteIconCache.NetworkAllowed)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        _ = FetchAsync(host, cancellation);
    }

    private async Task FetchAsync(string host, CancellationTokenSource cancellation)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await WebsiteIconCache.GetAsync(host, cancellation.Token);
        }
        catch (Exception)
        {
            // An icon is decoration: whatever went out, the row keeps its glyph.
        }

        // The continuation returns on this thread, but the row that asked may have been recycled, or
        // may already be showing a different host, by the time a slow answer lands.
        if (cancellation.IsCancellationRequested ||
            !_attachedToVisualTree ||
            Host != host ||
            !ReferenceEquals(_pending, cancellation))
        {
            return;
        }

        Show(bitmap);
    }

    private void Show(Bitmap? bitmap)
    {
        if (bitmap is null)
        {
            return;
        }

        Source = bitmap;
        IsVisible = true;
    }

    private void CancelPending()
    {
        var cancellation = _pending;
        _pending = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}
