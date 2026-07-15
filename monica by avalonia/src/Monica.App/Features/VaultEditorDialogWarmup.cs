using Avalonia.Threading;
using Monica.App.Features.Wallet;

namespace Monica.App.Features;

internal static class VaultEditorDialogWarmup
{
    private static int _isSuspended;
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

    internal static void EnsurePasswordWarmed() => EnsureWarmed(PasswordEditor);
    internal static void EnsureTotpWarmed() => EnsureWarmed(TotpEditor);
    internal static void EnsureWalletWarmed() => EnsureWarmed(WalletEditor);
    internal static PasswordEditorDialog TakePasswordEditorView() => PasswordEditor.TakePreparedView();
    internal static TotpEditorDialog TakeTotpEditorView() => TotpEditor.TakePreparedView();
    internal static WalletItemEditorDialog TakeWalletEditorView() => WalletEditor.TakePreparedView();

    internal static void SuspendPreparedViews()
    {
        Dispatcher.UIThread.VerifyAccess();
        Volatile.Write(ref _isSuspended, 1);
        PasswordEditor.ReleasePreparedView();
        TotpEditor.ReleasePreparedView();
        WalletEditor.ReleasePreparedView();
    }

    internal static void ResumePreparedViews() => Volatile.Write(ref _isSuspended, 0);

    private static void EnsureWarmed<TView>(EditorWarmup<TView> warmup)
        where TView : class
    {
        if (Volatile.Read(ref _isSuspended) == 0)
        {
            warmup.EnsureWarmed();
        }
    }

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

            if (Volatile.Read(ref _isSuspended) != 0)
            {
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

        internal void ReleasePreparedView()
        {
            Dispatcher.UIThread.VerifyAccess();
            _preparedView = null;
            Volatile.Write(ref _state, 0);
        }
    }
}
