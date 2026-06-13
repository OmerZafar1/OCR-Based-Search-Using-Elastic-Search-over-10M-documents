using DocumentSearch.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DocumentSearch.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<FolderAncestor> FolderAncestors => Set<FolderAncestor>();
    public DbSet<BulkIngestJob> BulkIngestJobs => Set<BulkIngestJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Folder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.MaterializedPath).HasMaxLength(2048).IsRequired();
            entity.HasIndex(e => e.MaterializedPath);
            entity.HasOne(e => e.ParentFolder)
                .WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentFolderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(512).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(512).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(256).IsRequired();
            entity.Property(e => e.FileExtension).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Sha256Hash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.StoragePath).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.ExtractedTextPath).HasMaxLength(2048);
            entity.Property(e => e.IndexError).HasMaxLength(4000);
            entity.HasIndex(e => e.FolderId);
            entity.HasIndex(e => e.IndexStatus);
            entity.HasIndex(e => e.Sha256Hash);
            entity.HasIndex(e => e.StoragePath);
            entity.HasOne(e => e.Folder)
                .WithMany(e => e.Documents)
                .HasForeignKey(e => e.FolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FolderAncestor>(entity =>
        {
            entity.HasKey(e => new { e.FolderId, e.AncestorFolderId });
            entity.HasIndex(e => e.AncestorFolderId);
            entity.HasOne(e => e.Folder)
                .WithMany(e => e.Ancestors)
                .HasForeignKey(e => e.FolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BulkIngestJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SourceDirectory).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Error).HasMaxLength(4000);
            entity.HasIndex(e => e.StartedAt);
        });
    }
}
