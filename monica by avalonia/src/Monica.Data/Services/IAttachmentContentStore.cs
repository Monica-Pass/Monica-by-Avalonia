using Monica.Core.Models;

namespace Monica.Data.Services;

public interface IAttachmentContentStore
{
    Task<byte[]?> TryReadAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default);
    Task DeleteAttachmentContentAsync(Attachment attachment, CancellationToken cancellationToken = default);
}
