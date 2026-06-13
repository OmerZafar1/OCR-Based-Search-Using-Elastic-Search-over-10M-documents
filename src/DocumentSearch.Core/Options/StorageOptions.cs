namespace DocumentSearch.Core.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Root folder where all documents are stored on disk.
    /// Uploads and backfill register files under this path.
    /// </summary>
    public string RootPath { get; set; } = "./Documents";
}
