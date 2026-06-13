using DocumentSearch.Core.Dtos;

namespace DocumentSearch.Core.Interfaces;

public interface IBulkIngestService
{
    Task<BulkIngestStatusDto> StartAsync(string sourceDirectory, Guid? targetFolderId, CancellationToken cancellationToken = default);
    BulkIngestStatusDto? GetCurrentJob();
    Task<DocumentStatsDto> GetDocumentStatsAsync(CancellationToken cancellationToken = default);
}
