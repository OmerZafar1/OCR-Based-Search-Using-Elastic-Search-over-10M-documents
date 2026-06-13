namespace DocumentSearch.Core.Dtos;

public sealed class SearchDocumentIndex
{
    public Guid DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public Guid FolderId { get; init; }
    public string FolderPath { get; init; } = string.Empty;
    public IReadOnlyList<Guid> AncestorFolderIds { get; init; } = [];
    public string FileExtension { get; init; } = string.Empty;
    public string DocumentKind { get; init; } = string.Empty;
    public DateTime ModifiedAt { get; init; }
}
