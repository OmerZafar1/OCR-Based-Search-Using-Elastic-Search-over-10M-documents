using DocumentSearch.Core.Dtos;

namespace DocumentSearch.Core.Interfaces;

public interface IElasticsearchService
{
    Task EnsureIndexAsync(CancellationToken cancellationToken = default);
    Task IndexDocumentAsync(SearchDocumentIndex doc, CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<SearchResponseDto> SearchAsync(SearchRequestDto request, CancellationToken cancellationToken = default);
}
