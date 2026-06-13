namespace DocumentSearch.Core.Interfaces;

public interface IFileStorage
{
    string GetResolvedRootPath();
    Task<string> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default);
    Task<string> SaveExtractedTextAsync(string relativeSidecarPath, string text, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
    Task<string?> ReadExtractedTextAsync(string? extractedTextPath, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
    string GetAbsolutePath(string storagePath);
    string ToStorageRelativePath(string absoluteFilePath);
}
