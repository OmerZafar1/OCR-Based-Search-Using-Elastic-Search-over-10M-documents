using DocumentSearch.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocumentSearch.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class BackfillController(IBulkIngestService bulkIngestService) : ControllerBase
{
    [HttpPost("backfill")]
    public async Task<ActionResult<object>> Backfill(
        [FromQuery] string sourceDirectory,
        [FromQuery] Guid? folderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return BadRequest("sourceDirectory query parameter is required.");
        }

        var job = await bulkIngestService.StartAsync(sourceDirectory, folderId, cancellationToken);
        return Accepted(new { message = "Bulk ingest started in background.", job });
    }
}
