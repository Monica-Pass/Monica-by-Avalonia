using Avalonia.Threading;

namespace Monica.App.Features.Passwords;

internal static class PasswordEditorDialogWarmup
{
    private static int _state;

    internal static bool IsWarmed => Volatile.Read(ref _state) == 2;

    internal static void EnsureWarmed()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(EnsureWarmed, DispatcherPriority.Background);
            return;
        }

        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            return;
        }

        try
        {
            AppDiagnostics.Measure("Warm password editor view", static () => _ = new PasswordEditorDialog());
            Volatile.Write(ref _state, 2);
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _state, 0);
            AppDiagnostics.Error("Password editor view warmup failed", ex);
        }
    }
}
