using DocumentSearch.Core.Dtos;
using DocumentSearch.Core.Interfaces;
using DocumentSearch.Infrastructure.Data;
using DocumentSearch.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentSearch.Infrastructure.Services;

public class FolderService(AppDbContext db) : IFolderService
{
    public async Task<IReadOnlyList<FolderDto>> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        var folders = await db.Folders
            .AsNoTracking()
            .OrderBy(f => f.MaterializedPath)
            .ToListAsync(cancellationToken);

        var lookup = folders.ToDictionary(f => f.Id, f => new FolderDto
        {
            Id = f.Id,
            Name = f.Name,
            ParentFolderId = f.ParentFolderId,
            MaterializedPath = f.MaterializedPath,
            CreatedAt = f.CreatedAt,
            Children = []
        });

        var roots = new List<FolderDto>();
        foreach (var folder in folders)
        {
            var dto = lookup[folder.Id];
            if (folder.ParentFolderId.HasValue && lookup.TryGetValue(folder.ParentFolderId.Value, out var parent))
            {
                ((List<FolderDto>)parent.Children).Add(dto);
            }
            else
            {
                roots.Add(dto);
            }
        }

        return roots;
    }

    public async Task<FolderDto> CreateAsync(CreateFolderRequest request, CancellationToken cancellationToken = default)
    {
        string parentPath = string.Empty;
        if (request.ParentFolderId.HasValue)
        {
            var parent = await db.Folders.FindAsync([request.ParentFolderId.Value], cancellationToken)
                ?? throw new InvalidOperationException("Parent folder not found.");
            parentPath = parent.MaterializedPath;
        }

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            ParentFolderId = request.ParentFolderId,
            MaterializedPath = FolderPathHelper.BuildMaterializedPath(request.Name, parentPath),
            CreatedAt = DateTime.UtcNow
        };

        db.Folders.Add(folder);
        await FolderPathHelper.RebuildAncestorsAsync(db, folder, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new FolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            ParentFolderId = folder.ParentFolderId,
            MaterializedPath = folder.MaterializedPath,
            CreatedAt = folder.CreatedAt
        };
    }

    public async Task<FolderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (folder is null)
        {
            return null;
        }

        return new FolderDto
        {
            Id = folder.Id,
            Name = folder.Name,
            ParentFolderId = folder.ParentFolderId,
            MaterializedPath = folder.MaterializedPath,
            CreatedAt = folder.CreatedAt
        };
    }

    public async Task<IReadOnlyList<Guid>> GetAncestorIdsAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        return await db.FolderAncestors
            .AsNoTracking()
            .Where(a => a.FolderId == folderId)
            .OrderBy(a => a.Depth)
            .Select(a => a.AncestorFolderId)
            .ToListAsync(cancellationToken);
    }
}
