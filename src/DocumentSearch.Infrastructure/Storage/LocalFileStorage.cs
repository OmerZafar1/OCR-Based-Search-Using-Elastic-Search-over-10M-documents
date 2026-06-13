using DocumentSearch.Core.Options;
using DocumentSearch.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace DocumentSearch.Infrastructure.Storage;

public class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly StorageOptions _options = options.Value;

    public string GetResolvedRootPath() => ResolveRootPath();

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(relativePath);
        var absolutePath = GetAbsolutePath(normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using var fileStream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream, cancellationToken);

        return normalized;
    }

    public async Task<string> SaveExtractedTextAsync(string relativeSidecarPath, string text, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizePath(relativeSidecarPath);
        var absolutePath = GetAbsolutePath(normalized);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await File.WriteAllTextAsync(absolutePath, text, cancellationToken);
        return normalized;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAbsolutePath(storagePath);
        Stream stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public async Task<string?> ReadExtractedTextAsync(string? extractedTextPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(extractedTextPath))
        {
            return null;
        }

        var absolutePath = GetAbsolutePath(extractedTextPath);
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(absolutePath, cancellationToken);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAbsolutePath(storagePath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    public string GetAbsolutePath(string storagePath)
    {
        if (Path.IsPathRooted(storagePath))
        {
            return Path.GetFullPath(storagePath);
        }

        return Path.GetFullPath(Path.Combine(ResolveRootPath(), storagePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    public string ToStorageRelativePath(string absoluteFilePath)
    {
        var fullFile = Path.GetFullPath(absoluteFilePath);
        var root = ResolveRootPath().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullFile.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"File '{fullFile}' is not under storage root '{root}'.");
        }

        return NormalizePath(fullFile[root.Length..]);
    }

    private string ResolveRootPath()
    {
        var configured = _options.RootPath;
        if (Path.IsPathRooted(configured))
        {
            return Path.GetFullPath(configured);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configured));
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
