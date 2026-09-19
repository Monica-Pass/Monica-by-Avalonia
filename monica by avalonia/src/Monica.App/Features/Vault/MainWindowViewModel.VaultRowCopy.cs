using CommunityToolkit.Mvvm.Input;
using Monica.App.Controls;
using Monica.App.Features.Vault;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // The row carries the entry it stands for, so a copy never waits for the list selection to
    // catch up with the right-click that opened the menu.
    [RelayCommand]
    private async Task CopyRowUsername(IVaultTreeRow? row)
    {
        if (row is VaultTreeEntryRow { Password: { } password })
        {
            await CopyUsernameCommand.ExecuteAsync(password);
        }
    }

    [RelayCommand]
    private async Task CopyRowSecret(IVaultTreeRow? row)
    {
        if (row is VaultTreeEntryRow { Password: { } password })
        {
            await CopyPasswordCommand.ExecuteAsync(password);
        }
    }

    // A code can live either on an authenticator item or inside a password's TOTP seed, and the two
    // are copied by different commands because only the password has to decrypt its own field.
    [RelayCommand]
    private async Task CopyRowCode(IVaultTreeRow? row)
    {
        if (row is not VaultTreeEntryRow entry)
        {
            return;
        }

        if (entry is { Kind: VaultEntryKind.Totp, Item: { } item })
        {
            await CopyTotpCommand.ExecuteAsync(item);
        }
        else if (entry.Password is { } password)
        {
            await CopyPasswordTotpCommand.ExecuteAsync(password);
        }
    }
}
