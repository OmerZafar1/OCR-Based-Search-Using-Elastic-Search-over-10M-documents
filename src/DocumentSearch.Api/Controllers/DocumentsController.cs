using DocumentSearch.Core.Dtos;
using DocumentSearch.Core.Interfaces;
using DocumentSearch.Core.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocumentSearch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController(IDocumentService documentService, IOptions<UploadOptions> uploadOptions) : ControllerBase
{
    private readonly UploadOptions _upload = uploadOptions.Value;

    [HttpGet("upload/config")]
    public ActionResult<UploadConfigDto> GetUploadConfig() => Ok(new UploadConfigDto
    {
        MaxFilesPerRequest = _upload.MaxFilesPerRequest,
        RecommendBulkIndexThreshold = _upload.RecommendBulkIndexThreshold,
        ClientParallelUploads = _upload.ClientParallelUploads,
        MaxRequestBodySizeBytes = _upload.MaxRequestBodySizeBytes
    });

    [HttpPost("upload")]
    [RequestSizeLimit(524_288_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
    public async Task<ActionResult<DocumentDto>> Upload(
        [FromForm] Guid folderId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("File is required.");
        }

        await using var stream = file.OpenReadStream();
        var document = await documentService.UploadAsync(folderId, stream, file.FileName, file.ContentType, cancellationToken);
        return Ok(document);
    }

    [HttpPost("upload-batch")]
    [RequestSizeLimit(524_288_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
    public async Task<ActionResult<UploadBatchResultDto>> UploadBatch(
        [FromForm] Guid folderId,
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        if (files is null || files.Count == 0)
        {
            return BadRequest("At least one file is required.");
        }

        if (files.Count > _upload.MaxFilesPerRequest)
        {
            return BadRequest($"Maximum {_upload.MaxFilesPerRequest} files per request. Use bulk index for larger volumes.");
        }

        var batch = files.Select(f => ((Stream)f.OpenReadStream(), f.FileName, f.ContentType)).ToList();
        var result = await documentService.UploadBatchAsync(folderId, batch, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DocumentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var document = await documentService.GetByIdAsync(id, cancellationToken);
        return document is null ? NotFound() : Ok(document);
    }

    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<DocumentStatusDto>> GetStatus(Guid id, CancellationToken cancellationToken)
    {
        var status = await documentService.GetStatusAsync(id, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var document = await documentService.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        var stream = await documentService.OpenDownloadStreamAsync(id, cancellationToken);
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, document.ContentType, document.FileName);
    }
}
