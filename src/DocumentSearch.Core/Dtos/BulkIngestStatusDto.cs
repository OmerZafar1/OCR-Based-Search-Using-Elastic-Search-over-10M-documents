namespace DocumentSearch.Core.Dtos;

public sealed class BulkIngestStatusDto
{
    public Guid JobId { get; init; }
    public string SourceDirectory { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long FilesDiscovered { get; init; }
    public long Registered { get; init; }
    public long Skipped { get; init; }
    public long Enqueued { get; init; }
    public string? Error { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public double? FilesPerSecond { get; init; }
}
