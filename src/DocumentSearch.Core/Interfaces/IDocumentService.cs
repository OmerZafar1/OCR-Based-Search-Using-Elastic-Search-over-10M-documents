using DocumentSearch.Core.Dtos;

namespace DocumentSearch.Core.Interfaces;

public interface IDocumentService
{
    Task<DocumentDto> UploadAsync(Guid folderId, Stream content, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<UploadBatchResultDto> UploadBatchAsync(Guid folderId, IEnumerable<(Stream Content, string FileName, string ContentType)> files, CancellationToken cancellationToken = default);
    Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DocumentStatusDto?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Stream?> OpenDownloadStreamAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> BackfillFromDirectoryAsync(string sourceDirectory, Guid? targetFolderId, CancellationToken cancellationToken = default);
}
