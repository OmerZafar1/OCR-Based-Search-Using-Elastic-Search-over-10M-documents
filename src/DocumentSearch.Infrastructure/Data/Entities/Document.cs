using DocumentSearch.Core.Enums;

namespace DocumentSearch.Infrastructure.Data.Entities;

public class Document
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public Guid FolderId { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public IndexStatus IndexStatus { get; set; } = IndexStatus.Pending;
    public DocumentKind DocumentKind { get; set; }
    public int PageCount { get; set; }
    public string? ExtractedTextPath { get; set; }
    public DateTime? IndexedAt { get; set; }
    public string? IndexError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }

    public Folder Folder { get; set; } = null!;
}
