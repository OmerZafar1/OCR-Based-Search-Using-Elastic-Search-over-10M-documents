using DocumentSearch.Core.Dtos;
using DocumentSearch.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocumentSearch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController(IElasticsearchService searchService, IDocumentService documentService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SearchResponseDto>> Search(
        [FromQuery] string q,
        [FromQuery] Guid? folderId,
        [FromQuery] bool includeSubfolders = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Query parameter 'q' is required.");
        }

        var results = await searchService.SearchAsync(new SearchRequestDto
        {
            Query = q,
            FolderId = folderId,
            IncludeSubfolders = includeSubfolders,
            Page = page,
            PageSize = pageSize
        }, cancellationToken);

        var enrichedHits = new List<SearchHitDto>();
        foreach (var hit in results.Hits)
        {
            var doc = await documentService.GetByIdAsync(hit.DocumentId, cancellationToken);
            enrichedHits.Add(new SearchHitDto
            {
                DocumentId = hit.DocumentId,
                Title = hit.Title,
                FileName = doc?.FileName ?? hit.Title,
                FolderPath = doc?.FolderPath ?? hit.FolderPath,
                Score = hit.Score,
                Highlight = hit.Highlight
            });
        }

        return Ok(new SearchResponseDto
        {
            Total = results.Total,
            Page = results.Page,
            PageSize = results.PageSize,
            Hits = enrichedHits
        });
    }
}
