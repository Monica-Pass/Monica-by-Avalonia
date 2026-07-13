using Monica.App.ViewModels;
using Monica.Core.Models;

namespace Monica.App.Services;

internal sealed class DisabledTotpEditorDialogService : ITotpEditorDialogService
{
    public Task<TotpEditorViewModel?> ShowAsync(SecureItem? item, CancellationToken cancellationToken = default) =>
        Task.FromResult<TotpEditorViewModel?>(null);
}

internal sealed class DisabledWalletItemEditorDialogService : IWalletItemEditorDialogService
{
    public Task<WalletItemEditorViewModel?> ShowAsync(SecureItem? item, VaultItemType? newItemType = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<WalletItemEditorViewModel?>(null);
}

internal sealed class DisabledConfirmationDialogService : IConfirmationDialogService
{
    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string primaryButtonText,
        string? closeButtonText = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> ConfirmTypedAsync(
        string title,
        string message,
        string requiredPhrase,
        string instruction,
        string primaryButtonText,
        string? closeButtonText = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
