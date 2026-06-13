using DocumentSearch.Core.Dtos;
using DocumentSearch.Core.Interfaces;
using DocumentSearch.Core.Options;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentSearch.Infrastructure.Search;

public class ElasticsearchService : IElasticsearchService
{
    private readonly ElasticsearchClient _client;
    private readonly ElasticsearchOptions _options;
    private readonly ILogger<ElasticsearchService> _logger;

    public ElasticsearchService(ElasticsearchClient client, IOptions<ElasticsearchOptions> options, ILogger<ElasticsearchService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        var exists = await _client.Indices.ExistsAsync(_options.IndexName, cancellationToken);
        if (exists.Exists)
        {
            return;
        }

        var response = await _client.Indices.CreateAsync(_options.IndexName, c => c
            .Settings(s => s
                .NumberOfShards(5)
                .NumberOfReplicas(0))
            .Mappings(m => m
                .Properties<IndexedDocument>(p => p
                    .Keyword(k => k.DocumentId)
                    .Text(t => t.Title, tf => tf.Fields(ff => ff.Keyword("keyword")))
                    .Text(t => t.Content)
                    .Keyword(k => k.FolderId)
                    .Keyword(k => k.FolderPath)
                    .Keyword(k => k.AncestorFolderIds)
                    .Keyword(k => k.FileExtension)
                    .Keyword(k => k.DocumentKind)
                    .Date(d => d.ModifiedAt)
                    .IntegerNumber(n => n.PageNumber))),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to create Elasticsearch index: {response.ElasticsearchServerError?.Error?.Reason}");
        }

        _logger.LogInformation("Created Elasticsearch index {IndexName}", _options.IndexName);
    }

    public async Task IndexDocumentAsync(SearchDocumentIndex doc, CancellationToken cancellationToken = default)
    {
        var indexed = new IndexedDocument
        {
            DocumentId = doc.DocumentId.ToString(),
            Title = doc.Title,
            Content = doc.Content,
            FolderId = doc.FolderId.ToString(),
            FolderPath = doc.FolderPath,
            AncestorFolderIds = doc.AncestorFolderIds.Select(id => id.ToString()).ToList(),
            FileExtension = doc.FileExtension,
            DocumentKind = doc.DocumentKind,
            ModifiedAt = doc.ModifiedAt
        };

        var response = await _client.IndexAsync(indexed, _options.IndexName, doc.DocumentId.ToString(), cancellationToken);

        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Failed to index document {doc.DocumentId}: {response.ElasticsearchServerError?.Error?.Reason}");
        }
    }

    public async Task DeleteDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await _client.DeleteAsync<IndexedDocument>(documentId.ToString(), d => d.Index(_options.IndexName), cancellationToken);
    }

    public async Task<SearchResponseDto> SearchAsync(SearchRequestDto request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var from = (page - 1) * pageSize;

        var response = await _client.SearchAsync<IndexedDocument>(s => s
            .Indices(_options.IndexName)
            .From(from)
            .Size(pageSize)
            .Query(q => q.Bool(b =>
            {
                b.Must(m => m.MultiMatch(mm => mm
                    .Query(request.Query)
                    .Fields(new[] { "content^2", "title^3" })
                    .Type(TextQueryType.BestFields)
                    .Fuzziness(new Fuzziness("AUTO"))));

                if (request.FolderId.HasValue)
                {
                    var folderId = request.FolderId.Value.ToString();
                    if (request.IncludeSubfolders)
                    {
                        b.Filter(f => f.Term(t => t.Field("ancestorFolderIds").Value(folderId)));
                    }
                    else
                    {
                        b.Filter(f => f.Term(t => t.Field("folderId").Value(folderId)));
                    }
                }
            }))
            .Highlight(h => h
                .Fields(f => f
                    .Add("content", hf => hf
                        .FragmentSize(200)
                        .NumberOfFragments(1)
                        .PreTags(["<em>"])
                        .PostTags(["</em>"]))))
            .Sort(sort => sort.Score(sc => sc.Order(SortOrder.Desc))),
            cancellationToken);

        if (!response.IsValidResponse)
        {
            throw new InvalidOperationException($"Search failed: {response.ElasticsearchServerError?.Error?.Reason}");
        }

        var hits = response.Hits.Select(hit =>
        {
            var highlight = hit.Highlight?.Values.FirstOrDefault()?.FirstOrDefault();
            return new SearchHitDto
            {
                DocumentId = Guid.Parse(hit.Source!.DocumentId),
                Title = hit.Source.Title,
                FileName = hit.Source.Title,
                FolderPath = hit.Source.FolderPath,
                Score = hit.Score ?? 0,
                Highlight = highlight
            };
        }).ToList();

        return new SearchResponseDto
        {
            Total = response.Total,
            Page = page,
            PageSize = pageSize,
            Hits = hits
        };
    }

    private sealed class IndexedDocument
    {
        public string DocumentId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string FolderId { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public List<string> AncestorFolderIds { get; set; } = [];
        public string FileExtension { get; set; } = string.Empty;
        public string DocumentKind { get; set; } = string.Empty;
        public DateTime ModifiedAt { get; set; }
        public int? PageNumber { get; set; }
    }
}
