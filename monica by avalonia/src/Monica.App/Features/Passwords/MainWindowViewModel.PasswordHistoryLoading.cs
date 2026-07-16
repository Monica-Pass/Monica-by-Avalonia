using Avalonia.Threading;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task LoadSelectedPasswordHistoryDeferredAsync(
        long entryId,
        int version,
        PasswordDetailViewModel details,
        Dispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        try
        {
            var history = await AppDiagnostics.MeasureAsync(
                $"Load selected password history id={entryId}",
                () => Task.Run(
                    () => GetPasswordHistoryDisplayItemsAsync(entryId, cancellationToken),
                    cancellationToken));
            await dispatcher.InvokeAsync(() =>
            {
                if (CanReadVault(cancellationToken) &&
                    IsCurrentSelectedPasswordDetailsRequest(version) &&
                    SelectedPassword?.Id == entryId &&
                    ReferenceEquals(SelectedPasswordDetails, details))
                {
                    details.SetPasswordHistory(history);
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            if (CanReadVault(cancellationToken))
            {
                AppDiagnostics.Error($"Load selected password history failed. id={entryId}, version={version}", ex);
            }
        }
    }
}
