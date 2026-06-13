using System.Collections.Concurrent;
using System.Text;
using DocumentSearch.Contracts;
using DocumentSearch.Core.Dtos;
using DocumentSearch.Core.Enums;
using DocumentSearch.Core.Interfaces;
using DocumentSearch.Core.Options;
using DocumentSearch.Infrastructure.Data;
using DocumentSearch.Infrastructure.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentSearch.Infrastructure.Services;

public class BulkIngestService(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionOptions> ingestionOptions,
    ILogger<BulkIngestService> logger) : IBulkIngestService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm", ".log",
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp"
    };

    private readonly IngestionOptions _options = ingestionOptions.Value;
    private readonly object _jobLock = new();
    private BulkIngestJobState? _currentJob;
    private CancellationTokenSource? _jobCts;

    public Task<BulkIngestStatusDto> StartAsync(string sourceDirectory, Guid? targetFolderId, CancellationToken cancellationToken = default)
    {
        lock (_jobLock)
        {
            if (_currentJob?.Status is "Running")
            {
                throw new InvalidOperationException("A bulk ingest job is already running.");
            }

            var sourceRoot = Path.GetFullPath(sourceDirectory);
            if (!Directory.Exists(sourceRoot))
            {
                throw new DirectoryNotFoundException($"Directory not found: {sourceRoot}");
            }

            _jobCts = new CancellationTokenSource();
            var job = new BulkIngestJobState
            {
                JobId = Guid.NewGuid(),
                SourceDirectory = sourceRoot,
                Status = "Running",
                StartedAt = DateTime.UtcNow
            };
            _currentJob = job;

            _ = Task.Run(() => RunJobAsync(job, targetFolderId, _jobCts.Token), CancellationToken.None);
            return Task.FromResult(ToDto(job));
        }
    }

    public BulkIngestStatusDto? GetCurrentJob()
    {
        lock (_jobLock)
        {
            return _currentJob is null ? null : ToDto(_currentJob);
        }
    }

    public async Task<DocumentStatsDto> GetDocumentStatsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var counts = await db.Documents
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(d => d.IndexStatus == IndexStatus.Pending),
                Processing = g.Count(d => d.IndexStatus == IndexStatus.Processing),
                Indexed = g.Count(d => d.IndexStatus == IndexStatus.Indexed),
                Failed = g.Count(d => d.IndexStatus == IndexStatus.Failed)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new DocumentStatsDto
        {
            Total = counts?.Total ?? 0,
            Pending = counts?.Pending ?? 0,
            Processing = counts?.Processing ?? 0,
            Indexed = counts?.Indexed ?? 0,
            Failed = counts?.Failed ?? 0
        };
    }

    private async Task RunJobAsync(BulkIngestJobState job, Guid? targetFolderId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
            var folderService = scope.ServiceProvider.GetRequiredService<IFolderService>();
            var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

            await PersistJobAsync(db, job, cancellationToken);

            var storageRoot = fileStorage.GetResolvedRootPath();
            var rootFolderId = targetFolderId ?? await GetRootFolderIdAsync(db, folderService, cancellationToken);
            var folderCache = await LoadFolderCacheAsync(db, cancellationToken);
            var batchSize = Math.Clamp(_options.BulkBatchSize, 100, 2000);

            var pendingDocs = new List<Document>(batchSize);
            var pendingMessages = new List<IngestDocumentMessage>(batchSize);
            var started = DateTime.UtcNow;

            foreach (var filePath in Directory.EnumerateFiles(job.SourceDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                job.FilesDiscovered++;

                var extension = Path.GetExtension(filePath);
                if (string.IsNullOrEmpty(extension) || !SupportedExtensions.Contains(extension))
                {
                    job.Skipped++;
                    continue;
                }

                if (!filePath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
                {
                    job.Skipped++;
                    continue;
                }

                var storagePath = fileStorage.ToStorageRelativePath(filePath);
                var fileName = Path.GetFileName(filePath);
                var relativeDir = Path.GetRelativePath(job.SourceDirectory, Path.GetDirectoryName(filePath) ?? job.SourceDirectory);
                var folderId = await ResolveFolderIdAsync(db, folderService, folderCache, relativeDir, rootFolderId, cancellationToken);
                var fileInfo = new FileInfo(filePath);

                pendingDocs.Add(new Document
                {
                    Id = Guid.NewGuid(),
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    FileName = fileName,
                    ContentType = GetContentType(fileName),
                    FileExtension = extension.TrimStart('.').ToLowerInvariant(),
                    FileSizeBytes = fileInfo.Length,
                    Sha256Hash = FastFingerprint(storagePath, fileInfo),
                    FolderId = folderId,
                    StoragePath = storagePath,
                    IndexStatus = IndexStatus.Pending,
                    DocumentKind = DetectKind(extension),
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                });

                if (pendingDocs.Count >= batchSize)
                {
                    await FlushBatchAsync(db, publishEndpoint, job, pendingDocs, pendingMessages, cancellationToken);
                }

                if (job.FilesDiscovered % 10_000 == 0)
                {
                    job.FilesPerSecond = job.FilesDiscovered / Math.Max((DateTime.UtcNow - started).TotalSeconds, 1);
                    await UpdateJobProgressAsync(db, job, cancellationToken);
                    logger.LogInformation(
                        "Bulk ingest {JobId}: discovered {Discovered}, registered {Registered}, skipped {Skipped}",
                        job.JobId, job.FilesDiscovered, job.Registered, job.Skipped);
                }
            }

            if (pendingDocs.Count > 0)
            {
                await FlushBatchAsync(db, publishEndpoint, job, pendingDocs, pendingMessages, cancellationToken);
            }

            job.Status = "Completed";
            job.CompletedAt = DateTime.UtcNow;
            job.FilesPerSecond = job.FilesDiscovered / Math.Max((job.CompletedAt.Value - started).TotalSeconds, 1);
            await UpdateJobProgressAsync(db, job, cancellationToken);

            logger.LogInformation(
                "Bulk ingest {JobId} completed: {Registered} registered, {Skipped} skipped",
                job.JobId, job.Registered, job.Skipped);
        }
        catch (OperationCanceledException)
        {
            job.Status = "Cancelled";
            job.CompletedAt = DateTime.UtcNow;
            job.Error = "Cancelled";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk ingest {JobId} failed", job.JobId);
            job.Status = "Failed";
            job.CompletedAt = DateTime.UtcNow;
            job.Error = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await UpdateJobProgressAsync(db, job, CancellationToken.None);
        }
    }

    private async Task FlushBatchAsync(
        AppDbContext db,
        IPublishEndpoint publishEndpoint,
        BulkIngestJobState job,
        List<Document> pendingDocs,
        List<IngestDocumentMessage> pendingMessages,
        CancellationToken cancellationToken)
    {
        var paths = pendingDocs.Select(d => d.StoragePath).ToList();
        var existingPaths = await db.Documents
            .AsNoTracking()
            .Where(d => paths.Contains(d.StoragePath))
            .Select(d => d.StoragePath)
            .ToHashSetAsync(cancellationToken);

        pendingMessages.Clear();
        var toInsert = new List<Document>();

        foreach (var doc in pendingDocs)
        {
            if (existingPaths.Contains(doc.StoragePath))
            {
                job.Skipped++;
                continue;
            }

            toInsert.Add(doc);
            pendingMessages.Add(new IngestDocumentMessage
            {
                DocumentId = doc.Id,
                StoragePath = doc.StoragePath,
                ContentType = doc.ContentType,
                FileName = doc.FileName,
                FolderId = doc.FolderId
            });
        }

        if (toInsert.Count > 0)
        {
            db.Documents.AddRange(toInsert);
            await db.SaveChangesAsync(cancellationToken);
            await publishEndpoint.PublishBatch(pendingMessages, cancellationToken);
            job.Registered += toInsert.Count;
            job.Enqueued += pendingMessages.Count;
        }

        pendingDocs.Clear();
    }

    private static async Task<Guid> GetRootFolderIdAsync(AppDbContext db, IFolderService folderService, CancellationToken cancellationToken)
    {
        var root = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.ParentFolderId == null, cancellationToken);
        if (root is not null)
        {
            return root.Id;
        }

        var created = await folderService.CreateAsync(new CreateFolderRequest { Name = "root" }, cancellationToken);
        return created.Id;
    }

    private static async Task<Dictionary<string, Guid>> LoadFolderCacheAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        return await db.Folders
            .AsNoTracking()
            .ToDictionaryAsync(f => f.MaterializedPath, f => f.Id, cancellationToken);
    }

    private static async Task<Guid> ResolveFolderIdAsync(
        AppDbContext db,
        IFolderService folderService,
        Dictionary<string, Guid> folderCache,
        string relativePath,
        Guid rootFolderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return rootFolderId;
        }

        var rootFolder = await db.Folders.AsNoTracking().FirstAsync(f => f.Id == rootFolderId, cancellationToken);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var currentPath = rootFolder.MaterializedPath;
        var currentId = rootFolderId;

        foreach (var segment in segments)
        {
            currentPath = $"{currentPath.TrimEnd('/')}/{segment}/";
            if (folderCache.TryGetValue(currentPath, out var cachedId))
            {
                currentId = cachedId;
                continue;
            }

            var created = await folderService.CreateAsync(new CreateFolderRequest
            {
                Name = segment,
                ParentFolderId = currentId
            }, cancellationToken);

            folderCache[currentPath] = created.Id;
            currentId = created.Id;
        }

        return currentId;
    }

    private static async Task PersistJobAsync(AppDbContext db, BulkIngestJobState job, CancellationToken cancellationToken)
    {
        db.BulkIngestJobs.Add(new BulkIngestJob
        {
            Id = job.JobId,
            SourceDirectory = job.SourceDirectory,
            Status = job.Status,
            StartedAt = job.StartedAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpdateJobProgressAsync(AppDbContext db, BulkIngestJobState job, CancellationToken cancellationToken)
    {
        var entity = await db.BulkIngestJobs.FirstOrDefaultAsync(j => j.Id == job.JobId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.FilesDiscovered = job.FilesDiscovered;
        entity.Registered = job.Registered;
        entity.Skipped = job.Skipped;
        entity.Enqueued = job.Enqueued;
        entity.Status = job.Status;
        entity.Error = job.Error;
        entity.CompletedAt = job.CompletedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string FastFingerprint(string storagePath, FileInfo fileInfo)
    {
        var key = $"{storagePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private static DocumentKind DetectKind(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => DocumentKind.Pdf,
        ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp" or ".gif" or ".webp" => DocumentKind.Image,
        ".txt" => DocumentKind.Text,
        _ => DocumentKind.Text
    };

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

    private static BulkIngestStatusDto ToDto(BulkIngestJobState job) => new()
    {
        JobId = job.JobId,
        SourceDirectory = job.SourceDirectory,
        Status = job.Status,
        FilesDiscovered = job.FilesDiscovered,
        Registered = job.Registered,
        Skipped = job.Skipped,
        Enqueued = job.Enqueued,
        Error = job.Error,
        StartedAt = job.StartedAt,
        CompletedAt = job.CompletedAt,
        FilesPerSecond = job.FilesPerSecond
    };

    private sealed class BulkIngestJobState
    {
        public Guid JobId { get; init; }
        public string SourceDirectory { get; init; } = string.Empty;
        public string Status { get; set; } = "Running";
        public long FilesDiscovered { get; set; }
        public long Registered { get; set; }
        public long Skipped { get; set; }
        public long Enqueued { get; set; }
        public string? Error { get; set; }
        public DateTime StartedAt { get; init; }
        public DateTime? CompletedAt { get; set; }
        public double? FilesPerSecond { get; set; }
    }
}
