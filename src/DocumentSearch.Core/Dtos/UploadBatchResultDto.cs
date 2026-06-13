namespace DocumentSearch.Core.Dtos;

public sealed class UploadBatchResultDto
{
    public int Accepted { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed class UploadConfigDto
{
    public int MaxFilesPerRequest { get; init; }
    public int RecommendBulkIndexThreshold { get; init; }
    public int ClientParallelUploads { get; init; }
    public long MaxRequestBodySizeBytes { get; init; }
}
