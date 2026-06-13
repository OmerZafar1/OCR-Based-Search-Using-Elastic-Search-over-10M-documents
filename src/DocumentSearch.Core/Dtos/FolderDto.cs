namespace DocumentSearch.Core.Dtos;

public sealed class FolderDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? ParentFolderId { get; init; }
    public string MaterializedPath { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<FolderDto> Children { get; init; } = [];
}
