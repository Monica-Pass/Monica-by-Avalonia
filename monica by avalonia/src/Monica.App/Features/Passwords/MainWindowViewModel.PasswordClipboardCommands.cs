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

        var text = entry.Password;
        if (_cryptoService.IsUnlocked)
        {
            try
            {
                text = _cryptoService.DecryptString(entry.Password);
            }
            catch
            {
                text = entry.Password;
            }
        }

        await _clipboardService.SetSensitiveTextAsync(text);
        StatusMessage = _localization.Format("CopiedPasswordFormat", entry.Title);
    }

    [RelayCommand]
    private async Task CopyUsernameAsync(PasswordEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Username))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(entry.Username);
        StatusMessage = _localization.Format("CopiedUsernameFormat", entry.Title);
    }

    [RelayCommand]
    private async Task CopyWebsiteAsync(PasswordEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Website))
        {
            return;
        }

        await _clipboardService.SetSensitiveTextAsync(entry.Website);
        StatusMessage = _localization.Format("CopiedWebsiteFormat", entry.Title);
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
        StatusMessage = _localization.Format("CopiedTotpFormat", entry.Title);
    }

}
