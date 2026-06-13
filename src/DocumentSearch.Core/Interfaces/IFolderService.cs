using DocumentSearch.Core.Dtos;

namespace DocumentSearch.Core.Interfaces;

public interface IFolderService
{
    Task<IReadOnlyList<FolderDto>> GetTreeAsync(CancellationToken cancellationToken = default);
    Task<FolderDto> CreateAsync(CreateFolderRequest request, CancellationToken cancellationToken = default);
    Task<FolderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetAncestorIdsAsync(Guid folderId, CancellationToken cancellationToken = default);
}
