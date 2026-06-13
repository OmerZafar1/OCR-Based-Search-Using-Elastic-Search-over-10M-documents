namespace DocumentSearch.Core.Options;

public class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public int MaxParallelJobs { get; set; } = 4;
    public int BulkBatchSize { get; set; } = 500;
    public int MaxRetries { get; set; } = 3;
}
