namespace DocumentSearch.Core.Options;

public class UploadOptions
{
    public const string SectionName = "Upload";

    /// <summary>Max HTTP request body size for uploads (bytes). Default 500 MB.</summary>
    public long MaxRequestBodySizeBytes { get; set; } = 524_288_000;

    /// <summary>Max files accepted in one upload-batch request.</summary>
    public int MaxFilesPerRequest { get; set; } = 200;

    /// <summary>UI hint: above this count, recommend bulk folder index instead of browser upload.</summary>
    public int RecommendBulkIndexThreshold { get; set; } = 500;

    /// <summary>Suggested parallel upload connections from the browser.</summary>
    public int ClientParallelUploads { get; set; } = 6;
}
