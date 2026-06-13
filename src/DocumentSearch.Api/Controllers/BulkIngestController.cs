using DocumentSearch.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocumentSearch.Api.Controllers;

[ApiController]
[Route("api/admin")]
public class BulkIngestController(IBulkIngestService bulkIngestService) : ControllerBase
{
    [HttpPost("bulk-ingest")]
    public async Task<ActionResult<object>> StartBulkIngest(
        [FromQuery] string sourceDirectory,
        [FromQuery] Guid? folderId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return BadRequest("sourceDirectory is required.");
        }

        var job = await bulkIngestService.StartAsync(sourceDirectory, folderId, cancellationToken);
        return Accepted(job);
    }

    [HttpGet("bulk-ingest/status")]
    public ActionResult<object> GetBulkIngestStatus()
    {
        var job = bulkIngestService.GetCurrentJob();
        return job is null ? NotFound(new { message = "No bulk ingest job running." }) : Ok(job);
    }

    [HttpGet("documents/stats")]
    public async Task<ActionResult<object>> GetDocumentStats(CancellationToken cancellationToken)
    {
        var stats = await bulkIngestService.GetDocumentStatsAsync(cancellationToken);
        return Ok(stats);
    }
}
