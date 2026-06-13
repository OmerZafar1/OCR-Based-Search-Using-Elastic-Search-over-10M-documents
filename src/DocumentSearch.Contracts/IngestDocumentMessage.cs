namespace DocumentSearch.Contracts;

public sealed class IngestDocumentMessage
{
    public Guid DocumentId { get; init; }
    public string StoragePath { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public Guid FolderId { get; init; }
    public int RetryCount { get; init; }
}
