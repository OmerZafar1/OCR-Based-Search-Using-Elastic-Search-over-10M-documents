using DocumentSearch.Core.Dtos;
using DocumentSearch.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DocumentSearch.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FoldersController(IFolderService folderService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FolderDto>>> GetTree(CancellationToken cancellationToken)
    {
        var folders = await folderService.GetTreeAsync(cancellationToken);
        return Ok(folders);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FolderDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var folder = await folderService.GetByIdAsync(id, cancellationToken);
        return folder is null ? NotFound() : Ok(folder);
    }

    [HttpPost]
    public async Task<ActionResult<FolderDto>> Create([FromBody] CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var folder = await folderService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = folder.Id }, folder);
    }
}
