using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Monica.App;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string ProtectPassword(string password)
    {
        return _cryptoService.IsUnlocked ? _cryptoService.EncryptString(password) : password;
    }

    private IReadOnlyList<string> ProtectPasswords(IReadOnlyList<string> passwords)
    {
        if (passwords.Count == 0)
        {
            return [ProtectPassword("")];
        }

        return passwords.Select(ProtectPassword).ToArray();
    }

    private string UnprotectPassword(string storedPassword)
    {
        if (!_cryptoService.IsUnlocked)
        {
            return storedPassword;
        }

        try
        {
            return _cryptoService.DecryptString(storedPassword);
        }
        catch
        {
            return storedPassword;
        }
    }

    private async Task SavePasswordHistorySnapshotIfChangedAsync(long entryId, string oldPlainPassword, string newPlainPassword)
    {
        if (entryId <= 0 ||
            string.IsNullOrWhiteSpace(oldPlainPassword) ||
            string.Equals(oldPlainPassword, newPlainPassword, StringComparison.Ordinal))
        {
            return;
        }

        var latestHistory = (await _repository.GetPasswordHistoryAsync(entryId)).FirstOrDefault();
        if (latestHistory is not null &&
            string.Equals(UnprotectPassword(latestHistory.Password), oldPlainPassword, StringComparison.Ordinal))
        {
            return;
        }

        await _repository.SavePasswordHistoryAsync(new PasswordHistoryEntry
        {
            EntryId = entryId,
            Password = ProtectPassword(oldPlainPassword),
            LastUsedAt = DateTimeOffset.UtcNow
        });
        await _repository.TrimPasswordHistoryAsync(entryId, PasswordHistoryLimit);
    }

    private async Task<IReadOnlyList<PasswordHistoryDisplayItem>> GetPasswordHistoryDisplayItemsAsync(long entryId)
    {
        var history = await _repository.GetPasswordHistoryAsync(entryId);
        return history
            .Select(item =>
            {
                var password = TryUnprotectHistoryPassword(item.Password);
                return new PasswordHistoryDisplayItem(item, password.DisplayValue, password.CanCopy);
            })
            .ToArray();
    }

    private (string DisplayValue, bool CanCopy) TryUnprotectHistoryPassword(string storedPassword)
    {
        if (string.IsNullOrWhiteSpace(storedPassword))
        {
            return (_localization.Get("PasswordHistoryUnavailable"), false);
        }

        if (!_cryptoService.IsUnlocked)
        {
            return ("********", false);
        }

        try
        {
            return (_cryptoService.DecryptString(storedPassword), true);
        }
        catch
        {
            return (storedPassword, true);
        }
    }

    private async Task<bool> DeletePasswordHistoryAsync(PasswordHistoryEntry entry)
    {
        if (!await ConfirmDeletePasswordHistoryAsync())
        {
            return false;
        }

        await _repository.DeletePasswordHistoryAsync(entry.Id);
        StatusMessage = _localization.Get("DeletedPasswordHistoryEntry");
        return true;
    }

    private async Task<bool> ClearPasswordHistoryAsync(long entryId)
    {
        if (!await ConfirmClearPasswordHistoryAsync())
        {
            return false;
        }

        await _repository.ClearPasswordHistoryAsync(entryId);
        StatusMessage = _localization.Get("ClearedPasswordHistory");
        return true;
    }

}
