using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Tells the vault which cipher its server still owes an erase for, at the one moment that is still
    /// knowable: the purge overwrites the stored row with a tombstone that keeps nothing but its ids, so
    /// after it the drift scan - which reads every synced row off its vault binding - can no longer tell
    /// that this entry was ever the server's. The identity travels from the row already in memory, and
    /// only once the local erase has actually landed: booking it for an entry this device kept would be a
    /// promise the next pull has to break.
    /// </summary>
    private async Task QueueBitwardenPurgeForRemovalAsync(PasswordEntry entry)
    {
        if (_bitwardenPurgeQueue is { } queue)
        {
            await TryQueueBitwardenPurgeAsync(() => queue.EnqueuePasswordAsync(entry));
        }
    }

    private async Task QueueBitwardenPurgeForRemovalAsync(SecureItem item)
    {
        if (_bitwardenPurgeQueue is { } queue)
        {
            await TryQueueBitwardenPurgeAsync(() => queue.EnqueueSecureItemAsync(item));
        }
    }

    private static async Task TryQueueBitwardenPurgeAsync(Func<Task<bool>> enqueue)
    {
        try
        {
            await enqueue();
        }
        catch (Exception exception)
        {
            // The user's decision stands either way: a queue that cannot be written costs the upload,
            // not the entry, and the next pull brings the remote copy back rather than destroying it.
            AppDiagnostics.Error("Bitwarden remote erase was not queued", exception);
        }
    }
}
