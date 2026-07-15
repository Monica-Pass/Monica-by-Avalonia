using Avalonia.Threading;
using Monica.App.Features.Wallet;

namespace Monica.App.Features;

internal static class VaultEditorDialogWarmup
{
    private static readonly EditorWarmup<PasswordEditorDialog> PasswordEditor = new(
        "password editor view",
        static () => new PasswordEditorDialog());
    private static readonly EditorWarmup<TotpEditorDialog> TotpEditor = new(
        "TOTP editor view",
        static () => new TotpEditorDialog());
    private static readonly EditorWarmup<WalletItemEditorDialog> WalletEditor = new(
        "wallet editor view",
        static () => new WalletItemEditorDialog());

    internal static bool IsPasswordWarmed => PasswordEditor.IsWarmed;
    internal static bool IsTotpWarmed => TotpEditor.IsWarmed;
    internal static bool IsWalletWarmed => WalletEditor.IsWarmed;

    internal static void EnsurePasswordWarmed() => PasswordEditor.EnsureWarmed();
    internal static void EnsureTotpWarmed() => TotpEditor.EnsureWarmed();
    internal static void EnsureWalletWarmed() => WalletEditor.EnsureWarmed();
    internal static PasswordEditorDialog TakePasswordEditorView() => PasswordEditor.TakePreparedView();
    internal static TotpEditorDialog TakeTotpEditorView() => TotpEditor.TakePreparedView();
    internal static WalletItemEditorDialog TakeWalletEditorView() => WalletEditor.TakePreparedView();

    private sealed class EditorWarmup<TView>(string diagnosticName, Func<TView> constructView)
        where TView : class
    {
        private int _state;
        private TView? _preparedView;

        internal bool IsWarmed => Volatile.Read(ref _state) == 2;

        internal void EnsureWarmed()
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
                TView? preparedView = null;
                AppDiagnostics.Measure($"Warm {diagnosticName}", () => preparedView = constructView());
                _preparedView = preparedView;
                Volatile.Write(ref _state, 2);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _state, 0);
                AppDiagnostics.Error($"{diagnosticName} warmup failed", ex);
            }
        }

        internal TView TakePreparedView()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                throw new InvalidOperationException($"The prepared {diagnosticName} must be consumed on the UI thread.");
            }

            var preparedView = _preparedView;
            _preparedView = null;
            Volatile.Write(ref _state, 0);
            return preparedView ?? constructView();
        }
    }
}
