using System.Security.Cryptography;
using DocumentSearch.Contracts;
using DocumentSearch.Core.Dtos;
using DocumentSearch.Core.Enums;
using DocumentSearch.Core.Interfaces;
using DocumentSearch.Infrastructure.Data;
using DocumentSearch.Infrastructure.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DocumentSearch.Infrastructure.Services;

public class DocumentService(
    AppDbContext db,
    IFileStorage fileStorage,
    IFolderService folderService,
    IPublishEndpoint publishEndpoint,
    ILogger<DocumentService> logger) : IDocumentService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm", ".log",
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp"
    };

    public async Task<DocumentDto> UploadAsync(Guid folderId, Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == folderId, cancellationToken)
            ?? throw new InvalidOperationException("Folder not found.");

        var (hash, fileSize, fileStream) = await PrepareUploadStreamAsync(content, cancellationToken);

        await using (fileStream)
        {
            var existing = await db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Sha256Hash == hash && d.FolderId == folderId, cancellationToken);
            if (existing is not null)
            {
                logger.LogInformation("Duplicate file detected in folder {FolderId}, returning existing document {DocumentId}", folderId, existing.Id);
                return MapToDto(existing, folder.MaterializedPath);
            }

            var storagePath = BuildStoragePath(folder.MaterializedPath, fileName);
            storagePath = await fileStorage.SaveAsync(storagePath, fileStream, cancellationToken);

            return await RegisterAndEnqueueAsync(
                folder,
                fileName,
                contentType,
                fileSize,
                hash,
                storagePath,
                cancellationToken);
        }
    }

    public async Task<UploadBatchResultDto> UploadBatchAsync(
        Guid folderId,
        IEnumerable<(Stream Content, string FileName, string ContentType)> files,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var accepted = 0;
        var failed = 0;

        foreach (var (content, fileName, contentType) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await UploadAsync(folderId, content, fileName, contentType, cancellationToken);
                accepted++;
            }
            catch (Exception ex)
            {
                failed++;
                if (errors.Count < 20)
                {
                    errors.Add($"{fileName}: {ex.Message}");
                }
            }
            finally
            {
                if (content is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    content.Dispose();
                }
            }
        }

        return new UploadBatchResultDto
        {
            Accepted = accepted,
            Failed = failed,
            Errors = errors
        };
    }

    public async Task<DocumentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents
            .AsNoTracking()
            .Include(d => d.Folder)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        return document is null ? null : MapToDto(document, document.Folder.MaterializedPath);
    }

    public async Task<DocumentStatusDto?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        return new DocumentStatusDto
        {
            Id = document.Id,
            IndexStatus = document.IndexStatus,
            IndexError = document.IndexError,
            IndexedAt = document.IndexedAt
        };
    }

    public async Task<Stream?> OpenDownloadStreamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        return await fileStorage.OpenReadAsync(document.StoragePath, cancellationToken);
    }

    public async Task<int> BackfillFromDirectoryAsync(string sourceDirectory, Guid? targetFolderId, CancellationToken cancellationToken = default)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceRoot}");
        }

        var storageRoot = fileStorage.GetResolvedRootPath();
        var rootFolderId = targetFolderId ?? await EnsureRootFolderAsync(cancellationToken);
        var enqueued = 0;

        foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension) || !SupportedExtensions.Contains(extension))
            {
                continue;
            }

            var fileName = Path.GetFileName(filePath);
            var contentType = GetContentType(fileName);
            var relativeDir = Path.GetRelativePath(sourceRoot, Path.GetDirectoryName(filePath) ?? sourceRoot);
            var folderId = await EnsureFolderPathAsync(relativeDir, rootFolderId, cancellationToken);
            var folder = await db.Folders.AsNoTracking().FirstAsync(f => f.Id == folderId, cancellationToken);

            var storagePath = filePath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase)
                ? fileStorage.ToStorageRelativePath(filePath)
                : BuildStoragePath(folder.MaterializedPath, fileName);

            if (!filePath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = File.OpenRead(filePath);
                storagePath = await fileStorage.SaveAsync(storagePath, stream, cancellationToken);
            }

            var fileInfo = new FileInfo(filePath);
            var hash = await ComputeFileSha256Async(filePath, cancellationToken);

            var duplicate = await db.Documents.AsNoTracking()
                .FirstOrDefaultAsync(d => d.StoragePath == storagePath, cancellationToken);
            if (duplicate is not null)
            {
                continue;
            }

            await RegisterAndEnqueueAsync(
                folder,
                fileName,
                contentType,
                fileInfo.Length,
                hash,
                storagePath,
                cancellationToken);

            enqueued++;
        }

        return enqueued;
    }

    private async Task<DocumentDto> RegisterAndEnqueueAsync(
        Folder folder,
        string fileName,
        string contentType,
        long fileSize,
        string hash,
        string storagePath,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var now = DateTime.UtcNow;
        var documentId = Guid.NewGuid();

        var document = new Document
        {
            Id = documentId,
            Title = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            ContentType = contentType,
            FileExtension = extension,
            FileSizeBytes = fileSize,
            Sha256Hash = hash,
            FolderId = folder.Id,
            StoragePath = storagePath,
            IndexStatus = IndexStatus.Pending,
            DocumentKind = DetectKind(extension),
            CreatedAt = now,
            ModifiedAt = now
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        await publishEndpoint.Publish(new IngestDocumentMessage
        {
            DocumentId = document.Id,
            StoragePath = document.StoragePath,
            ContentType = document.ContentType,
            FileName = document.FileName,
            FolderId = document.FolderId
        }, cancellationToken);

        return MapToDto(document, folder.MaterializedPath);
    }

    private async Task<Guid> EnsureRootFolderAsync(CancellationToken cancellationToken)
    {
        var root = await db.Folders.FirstOrDefaultAsync(f => f.ParentFolderId == null, cancellationToken);
        if (root is not null)
        {
            return root.Id;
        }

        var created = await folderService.CreateAsync(new CreateFolderRequest { Name = "root" }, cancellationToken);
        return created.Id;
    }

    private async Task<Guid> EnsureFolderPathAsync(string relativePath, Guid rootFolderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return rootFolderId;
        }

        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var currentParentId = (Guid?)rootFolderId;

        foreach (var segment in segments)
        {
            var parentId = currentParentId!.Value;
            var existing = await db.Folders.FirstOrDefaultAsync(
                f => f.ParentFolderId == parentId && f.Name == segment, cancellationToken);

            if (existing is null)
            {
                var created = await folderService.CreateAsync(new CreateFolderRequest
                {
                    Name = segment,
                    ParentFolderId = parentId
                }, cancellationToken);
                currentParentId = created.Id;
            }
            else
            {
                currentParentId = existing.Id;
            }
        }

        return currentParentId!.Value;
    }

    private static string BuildStoragePath(string materializedFolderPath, string fileName)
    {
        var folderSegment = materializedFolderPath.Trim('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(folderSegment, fileName).Replace('\\', '/');
    }

    private static DocumentKind DetectKind(string extension) => extension switch
    {
        "pdf" => DocumentKind.Pdf,
        "jpg" or "jpeg" or "png" or "tif" or "tiff" or "bmp" or "gif" or "webp" => DocumentKind.Image,
        "txt" => DocumentKind.Text,
        _ => DocumentKind.Text
    };

    private static async Task<(string Hash, long Size, MemoryStream Stream)> PrepareUploadStreamAsync(Stream content, CancellationToken cancellationToken)
    {
        var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(memory, cancellationToken);
        memory.Position = 0;

        return (Convert.ToHexString(hash).ToLowerInvariant(), memory.Length, memory);
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream"
        };
    }

    private static DocumentDto MapToDto(Document document, string folderPath) => new()
    {
        Id = document.Id,
        Title = document.Title,
        FileName = document.FileName,
        ContentType = document.ContentType,
        FileExtension = document.FileExtension,
        FileSizeBytes = document.FileSizeBytes,
        FolderId = document.FolderId,
        FolderPath = folderPath,
        IndexStatus = document.IndexStatus,
        DocumentKind = document.DocumentKind,
        CreatedAt = document.CreatedAt,
        IndexedAt = document.IndexedAt
    };
}
