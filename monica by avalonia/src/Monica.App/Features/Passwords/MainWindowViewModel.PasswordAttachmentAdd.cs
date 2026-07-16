using System.Security.Cryptography;
using CommunityToolkit.Mvvm.Input;
using Monica.App.Services;
using Monica.Core.Models;

namespace Monica.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task<long> AddPasswordAttachmentMetadataAsync(
        PasswordEntry entry,
        string fileName,
        string storagePath,
        long sizeBytes = 0,
        string contentType = "",
        byte[]? content = null,
        CancellationToken cancellationToken = default) =>
        (await AddPasswordAttachmentMetadataCoreAsync(
            entry,
            fileName,
            storagePath,
            sizeBytes,
            contentType,
            content,
            cancellationToken)).Id;

    private async Task<Attachment> AddPasswordAttachmentMetadataCoreAsync(
        PasswordEntry entry,
        string fileName,
        string storagePath,
        long sizeBytes,
        string contentType,
        byte[]? content,
        CancellationToken cancellationToken = default)
    {
        if (entry.Id == 0)
        {
            throw new ArgumentException("Password entry must be saved before adding attachments.", nameof(entry));
        }

        var attachment = new Attachment
        {
            OwnerType = "PASSWORD",
            OwnerId = entry.Id,
            FileName = string.IsNullOrWhiteSpace(fileName) ? _localization.Get("Attachment") : fileName.Trim(),
            ContentType = contentType.Trim(),
            StoragePath = storagePath.Trim(),
            SizeBytes = Math.Max(0, sizeBytes),
            CreatedAt = DateTimeOffset.UtcNow,
            BitwardenVaultId = entry.BitwardenVaultId
        };

        var originalStoragePath = attachment.StoragePath;
        var id = content is null
            ? await _repository.SaveAttachmentAsync(attachment, cancellationToken)
            : await _repository.SaveAttachmentAsync(attachment, content, cancellationToken);
        if (attachment.Id == 0)
        {
            attachment.Id = id;
        }

        if (content is not null &&
            !string.IsNullOrWhiteSpace(originalStoragePath) &&
            !string.Equals(originalStoragePath, attachment.StoragePath, StringComparison.Ordinal) &&
            !originalStoragePath.StartsWith("mdbx:", StringComparison.OrdinalIgnoreCase))
        {
            await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(originalStoragePath, cancellationToken);
        }

        SetPasswordAttachmentOwnerState(entry.Id, hasAttachments: true);
        AddPasswordAttachmentSearchMatch(entry.Id, attachment);
        RefreshPasswordAttachmentState(entry);
        RaiseFilteredPasswordsChanged();
        await LogOperationAsync(new OperationLog
        {
            ItemType = "PASSWORD",
            ItemId = entry.Id,
            ItemTitle = entry.Title,
            OperationType = "ATTACHMENT",
            DeviceName = Environment.MachineName
        }, cancellationToken);
        StatusMessage = _localization.Format("AddedAttachmentFormat", attachment.FileName, entry.Title);
        return attachment;
    }

    [RelayCommand]
    private async Task AddPasswordAttachmentAsync(PasswordEntry? entry) =>
        _ = await TryAddPasswordAttachmentAsync(entry);

    private async Task<PasswordAttachmentAddResult> TryAddPasswordAttachmentAsync(PasswordEntry? entry)
    {
        if (entry is null || entry.Id <= 0 || entry.IsDeleted)
        {
            return new PasswordAttachmentAddResult(PasswordAttachmentAddOutcome.Cancelled);
        }

        byte[]? pickedContent = null;
        byte[]? storedContent = null;
        var stagedStoragePath = "";
        try
        {
            var draft = await _passwordAttachmentFileService.PickAttachmentAsync();
            if (draft is null)
            {
                return new PasswordAttachmentAddResult(PasswordAttachmentAddOutcome.Cancelled);
            }

            pickedContent = draft.Content;
            if (_vaultSessionService.IsExplicitlyLocked)
            {
                StatusMessage = _localization.Get("VaultLocked");
                return new PasswordAttachmentAddResult(
                    PasswordAttachmentAddOutcome.VaultLocked,
                    StatusText: StatusMessage);
            }

            if (!_repository.PersistsAttachmentContent || draft.Content is not { Length: > 0 })
            {
                draft = await _passwordAttachmentFileService.StoreAttachmentAsync(
                    draft.FileName,
                    draft.Content ?? [],
                    draft.ContentType);
                storedContent = draft.Content;
                stagedStoragePath = draft.StoragePath;
            }

            if (_vaultSessionService.IsExplicitlyLocked)
            {
                await DeleteStagedAttachmentAfterFailureAsync(stagedStoragePath);
                stagedStoragePath = "";
                StatusMessage = _localization.Get("VaultLocked");
                return new PasswordAttachmentAddResult(
                    PasswordAttachmentAddOutcome.VaultLocked,
                    StatusText: StatusMessage);
            }

            var attachment = await AddPasswordAttachmentMetadataCoreAsync(
                entry,
                draft.FileName,
                draft.StoragePath,
                draft.SizeBytes,
                draft.ContentType,
                draft.Content);
            stagedStoragePath = "";
            return new PasswordAttachmentAddResult(
                PasswordAttachmentAddOutcome.Added,
                attachment,
                StatusMessage);
        }
        catch (AttachmentTooLargeException ex)
        {
            StatusMessage = _localization.Format(
                "AttachmentTooLargeFormat",
                FormatByteSize(ex.ActualBytes),
                FormatByteSize(ex.MaximumBytes));
            return new PasswordAttachmentAddResult(
                PasswordAttachmentAddOutcome.TooLarge,
                StatusText: StatusMessage);
        }
        catch (OperationCanceledException)
        {
            return new PasswordAttachmentAddResult(PasswordAttachmentAddOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            await DeleteStagedAttachmentAfterFailureAsync(stagedStoragePath);
            stagedStoragePath = "";
            AppDiagnostics.Error("Password attachment add failed", ex);
            StatusMessage = _localization.Get("AttachmentAddFailed");
            return new PasswordAttachmentAddResult(
                PasswordAttachmentAddOutcome.Failed,
                StatusText: StatusMessage);
        }
        finally
        {
            ZeroAttachmentBuffer(pickedContent);
            if (!ReferenceEquals(pickedContent, storedContent))
            {
                ZeroAttachmentBuffer(storedContent);
            }
        }
    }

    private async Task DeleteStagedAttachmentAfterFailureAsync(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return;
        }

        try
        {
            await _passwordAttachmentFileService.DeleteStoredAttachmentAsync(storagePath);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("Staged password attachment cleanup failed", ex);
        }
    }

    private static void ZeroAttachmentBuffer(byte[]? content)
    {
        if (content is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }
}
