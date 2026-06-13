namespace DocumentSearch.Core.Dtos;

public sealed class CreateFolderRequest
{
    public required string Name { get; init; }
    public Guid? ParentFolderId { get; init; }
}
