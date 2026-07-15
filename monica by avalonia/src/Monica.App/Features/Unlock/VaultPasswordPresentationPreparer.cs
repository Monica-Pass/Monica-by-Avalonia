using Monica.Core.Services;

namespace Monica.App.ViewModels;

internal static class VaultPasswordPresentationPreparer
{
    public static void Prepare(
        VaultLoadSnapshot snapshot,
        ITotpService totpService,
        CancellationToken cancellationToken)
    {
        foreach (var entry in snapshot.AllPasswords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PasswordPresentationState.RefreshTotp(entry, totpService);
            PasswordPresentationState.RefreshAttachment(entry, snapshot.PasswordAttachments);
            entry.IsSelected = false;
        }
    }
}
