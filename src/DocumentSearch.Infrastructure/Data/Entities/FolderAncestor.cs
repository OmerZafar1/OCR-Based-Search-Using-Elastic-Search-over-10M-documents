namespace DocumentSearch.Infrastructure.Data.Entities;

public class FolderAncestor
{
    public Guid FolderId { get; set; }
    public Guid AncestorFolderId { get; set; }
    public int Depth { get; set; }

    public Folder Folder { get; set; } = null!;
}
