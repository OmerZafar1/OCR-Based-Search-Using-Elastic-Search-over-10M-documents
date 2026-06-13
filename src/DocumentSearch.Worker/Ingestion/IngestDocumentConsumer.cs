using DocumentSearch.Contracts;
using DocumentSearch.Core.Enums;
using DocumentSearch.Core.Interfaces;
using DocumentSearch.Core.Options;
using DocumentSearch.Infrastructure.Data;
using DocumentSearch.Infrastructure.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace DocumentSearch.Worker.Ingestion;

public class IngestDocumentConsumer : IConsumer<IngestDocumentMessage>
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentTextExtractor _extractor;
    private readonly IElasticsearchService _searchService;
    private readonly IFolderService _folderService;
    private readonly IngestionOptions _ingestionOptions;
    private readonly ILogger<IngestDocumentConsumer> _logger;
    private readonly ResiliencePipeline _pipeline;

    public IngestDocumentConsumer(
        AppDbContext db,
        IFileStorage fileStorage,
        IDocumentTextExtractor extractor,
        IElasticsearchService searchService,
        IFolderService folderService,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<IngestDocumentConsumer> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _extractor = extractor;
        _searchService = searchService;
        _folderService = folderService;
        _ingestionOptions = ingestionOptions.Value;
        _logger = logger;

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = _ingestionOptions.MaxRetries,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    logger.LogWarning(args.Outcome.Exception, "Ingest retry {Attempt} for document ingestion", args.AttemptNumber);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task Consume(ConsumeContext<IngestDocumentMessage> context)
    {
        var message = context.Message;
        var document = await _db.Documents
            .Include(d => d.Folder)
            .FirstOrDefaultAsync(d => d.Id == message.DocumentId, context.CancellationToken);

        if (document is null)
        {
            _logger.LogWarning("Document {DocumentId} not found for ingestion", message.DocumentId);
            return;
        }

        document.IndexStatus = IndexStatus.Processing;
        document.IndexError = null;
        await _db.SaveChangesAsync(context.CancellationToken);

        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                var text = await _fileStorage.ReadExtractedTextAsync(document.ExtractedTextPath, token);
                Core.Interfaces.ExtractionResult extraction;

                if (!string.IsNullOrWhiteSpace(text))
                {
                    extraction = new Core.Interfaces.ExtractionResult
                    {
                        Text = text,
                        DocumentKind = document.DocumentKind,
                        PageCount = document.PageCount
                    };
                }
                else
                {
                    await using var stream = await _fileStorage.OpenReadAsync(document.StoragePath, token);
                    extraction = await _extractor.ExtractAsync(stream, document.FileName, document.ContentType, token);
                    document.ExtractedTextPath = await _fileStorage.SaveExtractedTextAsync(
                        BuildSidecarPath(document.StoragePath), extraction.Text, token);
                }

                document.DocumentKind = extraction.DocumentKind;
                document.PageCount = extraction.PageCount;

                var ancestors = await _folderService.GetAncestorIdsAsync(document.FolderId, token);
                await _searchService.EnsureIndexAsync(token);
                await _searchService.IndexDocumentAsync(new Core.Dtos.SearchDocumentIndex
                {
                    DocumentId = document.Id,
                    Title = document.Title,
                    Content = extraction.Text,
                    FolderId = document.FolderId,
                    FolderPath = document.Folder.MaterializedPath,
                    AncestorFolderIds = ancestors,
                    FileExtension = document.FileExtension,
                    DocumentKind = extraction.DocumentKind.ToString(),
                    ModifiedAt = document.ModifiedAt
                }, token);

                document.IndexStatus = IndexStatus.Indexed;
                document.IndexedAt = DateTime.UtcNow;
                document.IndexError = null;
                await _db.SaveChangesAsync(token);
            }, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ingest document {DocumentId}", document.Id);
            document.IndexStatus = IndexStatus.Failed;
            document.IndexError = ex.Message.Length > 4000 ? ex.Message[..4000] : ex.Message;
            await _db.SaveChangesAsync(context.CancellationToken);
            throw;
        }
    }

    private static string BuildSidecarPath(string storagePath)
    {
        var directory = Path.GetDirectoryName(storagePath.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(storagePath) + ".extracted.txt";
        return Path.Combine(directory, fileName).Replace('\\', '/');
    }
}
