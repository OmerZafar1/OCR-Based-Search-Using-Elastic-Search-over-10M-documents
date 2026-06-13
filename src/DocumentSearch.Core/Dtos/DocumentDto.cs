using DocumentSearch.Core.Enums;

namespace DocumentSearch.Core.Dtos;

public sealed class DocumentDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public Guid FolderId { get; init; }
    public string FolderPath { get; init; } = string.Empty;
    public IndexStatus IndexStatus { get; init; }
    public DocumentKind DocumentKind { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? IndexedAt { get; init; }
}
