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
    private async Task CopyPasswordAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var password = ReadPasswordSecret(entry.Password);
        if (!password.IsReadable)
        {
            SetStatusFailure(password.State == PasswordSecretState.Locked
                ? "VaultLocked"
                : "PasswordSecretUnavailable");
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(password.Value);
        SetStatusNotice("CopiedPasswordFormat", entry.Title);
    }

    [RelayCommand]
    private async Task CopyUsernameAsync(PasswordEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Username))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(entry.Username);
        SetStatusNotice("CopiedUsernameFormat", entry.Title);
    }

    [RelayCommand]
    private async Task CopyWebsiteAsync(PasswordEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Website))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(entry.Website);
        SetStatusNotice("CopiedWebsiteFormat", entry.Title);
    }


    [RelayCommand]
    private async Task CopyPasswordTotpAsync(PasswordEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        RefreshPasswordTotpDisplay(entry);
        await _clipboardService.SetSensitiveTextAsync(entry.TotpCode);
        SetStatusNotice("CopiedTotpFormat", entry.Title);
    }

}
