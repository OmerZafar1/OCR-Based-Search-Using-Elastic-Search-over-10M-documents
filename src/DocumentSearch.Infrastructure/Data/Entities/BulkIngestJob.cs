namespace DocumentSearch.Infrastructure.Data.Entities;

public class BulkIngestJob
{
    public Guid Id { get; set; }
    public string SourceDirectory { get; set; } = string.Empty;
    public string Status { get; set; } = "Running";
    public long FilesDiscovered { get; set; }
    public long Registered { get; set; }
    public long Skipped { get; set; }
    public long Enqueued { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
