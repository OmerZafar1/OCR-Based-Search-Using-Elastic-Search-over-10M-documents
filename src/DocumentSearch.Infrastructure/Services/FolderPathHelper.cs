using DocumentSearch.Infrastructure.Data.Entities;
using DocumentSearch.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DocumentSearch.Infrastructure.Services;

public static class FolderPathHelper
{
    public static async Task RebuildAncestorsAsync(AppDbContext db, Folder folder, CancellationToken cancellationToken = default)
    {
        var ancestors = new List<FolderAncestor>
        {
            new() { FolderId = folder.Id, AncestorFolderId = folder.Id, Depth = 0 }
        };

        if (folder.ParentFolderId.HasValue)
        {
            var parentAncestors = await db.FolderAncestors
                .AsNoTracking()
                .Where(a => a.FolderId == folder.ParentFolderId.Value)
                .OrderBy(a => a.Depth)
                .ToListAsync(cancellationToken);

            foreach (var parentAncestor in parentAncestors)
            {
                ancestors.Add(new FolderAncestor
                {
                    FolderId = folder.Id,
                    AncestorFolderId = parentAncestor.AncestorFolderId,
                    Depth = parentAncestor.Depth + 1
                });
            }
        }

        var existing = await db.FolderAncestors
            .Where(a => a.FolderId == folder.Id)
            .ToListAsync(cancellationToken);

        db.FolderAncestors.RemoveRange(existing);
        db.FolderAncestors.AddRange(ancestors);
    }

    public static string BuildMaterializedPath(string name, string? parentPath)
    {
        var segment = SanitizeSegment(name);
        return string.IsNullOrEmpty(parentPath) ? $"/{segment}/" : $"{parentPath.TrimEnd('/')}/{segment}/";
    }

    private static string SanitizeSegment(string name)
    {
        var sanitized = name.Trim().Replace('/', '-').Replace('\\', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? "untitled" : sanitized;
    }
}
