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

    private PasswordSecretReadResult ReadPasswordSecret(string storedPassword) =>
        PasswordSecretResolver.Read(storedPassword, _cryptoService);

    private string ReadPasswordSecretOrThrow(string storedPassword)
    {
        var result = ReadPasswordSecret(storedPassword);
        if (result.IsReadable)
        {
            return result.Value;
        }

        var messageKey = result.State == PasswordSecretState.Locked
            ? "VaultLocked"
            : "PasswordSecretUnavailable";
        throw new PasswordSecretUnavailableException(_localization.Get(messageKey));
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
        var latestPassword = latestHistory is null
            ? default
            : ReadPasswordSecret(latestHistory.Password);
        if (latestPassword.IsReadable &&
            string.Equals(latestPassword.Value, oldPlainPassword, StringComparison.Ordinal))
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
        var result = ReadPasswordSecret(storedPassword);
        if (result.State == PasswordSecretState.Empty)
        {
            return (_localization.Get("PasswordHistoryUnavailable"), false);
        }

        if (result.State == PasswordSecretState.Locked)
        {
            return ("********", false);
        }

        if (result.IsReadable)
        {
            return (result.Value, true);
        }

        return (_localization.Get("PasswordHistoryUnavailable"), false);
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
