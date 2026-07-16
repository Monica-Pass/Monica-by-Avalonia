using Monica.Core.Models;
using Monica.Core.Services;

namespace Monica.App.ViewModels;

internal static class PasswordPresentationState
{
    public static void RefreshTotp(PasswordEntry entry, ITotpService totpService)
    {
        var data = TotpDataResolver.FromAuthenticatorKey(entry.AuthenticatorKey, entry.Title, entry.Username);
        if (data is null || string.IsNullOrWhiteSpace(data.Secret))
        {
            entry.TotpCode = "------";
            entry.TotpTimeRemaining = "";
            entry.TotpProgress = 0;
            return;
        }

        entry.TotpCode = totpService.GenerateCode(data.Secret, data.Period, data.Digits, data.OtpType, data.Counter);
        entry.TotpTimeRemaining = $"{totpService.GetRemainingSeconds(data.Period)}s";
        entry.TotpProgress = totpService.GetProgress(data.Period);
    }

    public static void RefreshAttachment(
        PasswordEntry entry,
        IReadOnlyCollection<long> attachmentOwnerIds)
    {
        entry.HasAttachments = attachmentOwnerIds.Contains(entry.Id);
    }
}
