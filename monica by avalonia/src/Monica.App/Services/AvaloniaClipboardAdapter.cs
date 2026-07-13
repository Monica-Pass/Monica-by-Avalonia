using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Monica.Platform.Services;

namespace Monica.App.Services;

public sealed class AvaloniaClipboardAdapter(Func<TopLevel?> topLevelProvider) : IClipboardAdapter
{
    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
        InvokeOnUiThreadAsync(
            async () => await GetClipboard().TryGetTextAsync(),
            cancellationToken);

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
        InvokeOnUiThreadAsync(
            async () => await GetClipboard().SetTextAsync(text),
            cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        InvokeOnUiThreadAsync(
            async () => await GetClipboard().ClearAsync(),
            cancellationToken);

    private Avalonia.Input.Platform.IClipboard GetClipboard() =>
        topLevelProvider()?.Clipboard
        ?? throw new InvalidOperationException("The application clipboard is unavailable.");

    private static Task InvokeOnUiThreadAsync(
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () => await CompleteAsync(action, completion, cancellationToken));
        return completion.Task;
    }

    private static Task<T> InvokeOnUiThreadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () => await CompleteAsync(action, completion, cancellationToken));
        return completion.Task;
    }

    private static async Task CompleteAsync(
        Func<Task> action,
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await action();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private static async Task CompleteAsync<T>(
        Func<Task<T>> action,
        TaskCompletionSource<T> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            completion.TrySetResult(await action());
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }
}
