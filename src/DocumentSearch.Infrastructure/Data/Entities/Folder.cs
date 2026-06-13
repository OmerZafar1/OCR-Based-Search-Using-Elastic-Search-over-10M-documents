namespace DocumentSearch.Infrastructure.Data.Entities;

public class Folder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
    public string MaterializedPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Folder? ParentFolder { get; set; }
    public ICollection<Folder> Children { get; set; } = [];
    public ICollection<Document> Documents { get; set; } = [];
    public ICollection<FolderAncestor> Ancestors { get; set; } = [];
}
