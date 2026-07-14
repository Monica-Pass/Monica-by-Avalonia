using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task AddPasswordAsync()
    {
        var initialPassword = string.IsNullOrWhiteSpace(GeneratedPassword) ? "" : GeneratedPassword;
        var editor = await _passwordEditorDialogService.ShowAsync(
            null,
            Categories.ToList(),
            initialPassword,
            notes: NoteItems.ToList());
        if (editor is null)
        {
            return;
        }

        using var editorLifetime = editor;

        var entries = editor
            .BuildEntries(ProtectPasswords(editor.GetPasswordRows()))
            .ToList();
        foreach (var entry in entries)
        {
            await _repository.SavePasswordAsync(entry);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = entry.Id,
                ItemTitle = entry.Title,
                OperationType = "CREATE",
                DeviceName = Environment.MachineName
            });
        }

        var customFields = BindCustomFields(entries[0].Id, editor.GetCustomFields());
        await _repository.ReplaceCustomFieldsAsync(entries[0].Id, customFields);
        SetPasswordCustomFields(entries[0].Id, customFields);
        foreach (var entry in entries)
        {
            RefreshPasswordTotpDisplay(entry);
        }

        await SynchronizeBoundTotpAsync(entries[0]);
        ReplacePasswordGroup([], entries);
        RefreshBoundTotpPresentation(entries);
        InvalidateSecurityAnalysis();
        RaiseCounts();
        RaiseFilteredPasswordsChanged();
        StatusMessage = _localization.Format("CreatedPasswordFormat", entries[0].Title);
    }

    [RelayCommand]
    private async Task EditPasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var siblings = GetPasswordSiblings(entry).ToList();
        var entryPassword = ReadPasswordSecret(entry.Password);
        var siblingPasswords = siblings.Select(item => ReadPasswordSecret(item.Password)).ToArray();
        if (!entryPassword.IsReadable || siblingPasswords.Any(item => !item.IsReadable))
        {
            var unavailable = !entryPassword.IsReadable
                ? entryPassword
                : siblingPasswords.First(item => !item.IsReadable);
            StatusMessage = _localization.Get(unavailable.State == PasswordSecretState.Locked
                ? "VaultLocked"
                : "PasswordSecretUnavailable");
            return;
        }

        var customFields = await GetGroupCustomFieldsAsync(entry, siblings);
        var editor = await _passwordEditorDialogService.ShowAsync(
            entry,
            Categories.ToList(),
            entryPassword.Value,
            siblingPasswords.Select(item => item.Value).ToArray(),
            NoteItems.ToList(),
            customFields);
        if (editor is null)
        {
            return;
        }

        using var editorLifetime = editor;

        var passwordRows = editor.GetPasswordRows();
        var storedPasswords = ProtectPasswords(passwordRows);
        var updatedEntries = new List<PasswordEntry>();
        for (var index = 0; index < storedPasswords.Count; index++)
        {
            var source = index < siblings.Count ? siblings[index] : null;
            var oldPlainPassword = source is null ? "" : siblingPasswords[index].Value;
            var updated = editor.BuildEntryFrom(source, storedPasswords[index]);
            await _repository.SavePasswordAsync(updated);
            await SavePasswordHistorySnapshotIfChangedAsync(updated.Id, oldPlainPassword, passwordRows[index]);
            await LogOperationAsync(new OperationLog
            {
                ItemType = "PASSWORD",
                ItemId = updated.Id,
                ItemTitle = updated.Title,
                OperationType = source is null ? "CREATE" : "UPDATE",
                DeviceName = Environment.MachineName
            });
            updatedEntries.Add(updated);
        }

        foreach (var removed in siblings.Skip(storedPasswords.Count))
        {
            await _repository.SoftDeletePasswordAsync(removed.Id);
        }

        var updatedCustomFields = BindCustomFields(updatedEntries[0].Id, editor.GetCustomFields());
        await _repository.ReplaceCustomFieldsAsync(updatedEntries[0].Id, updatedCustomFields);
        SetPasswordCustomFields(updatedEntries[0].Id, updatedCustomFields);
        foreach (var updated in updatedEntries)
        {
            RefreshPasswordTotpDisplay(updated);
        }

        await SynchronizeBoundTotpAsync(updatedEntries[0]);
        ReplacePasswordGroup(siblings, updatedEntries);
        RefreshBoundTotpPresentation(siblings.Concat(updatedEntries));
        InvalidateSecurityAnalysis();
        RaiseCounts();
        RaiseFilteredPasswordsChanged();
        StatusMessage = _localization.Format("UpdatedPasswordFormat", updatedEntries[0].Title);
    }

}
