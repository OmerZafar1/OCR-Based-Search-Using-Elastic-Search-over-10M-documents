using DocumentSearch.Core.Enums;

namespace DocumentSearch.Core.Dtos;

public sealed class DocumentStatusDto
{
    public Guid Id { get; init; }
    public IndexStatus IndexStatus { get; init; }
    public string? IndexError { get; init; }
    public DateTime? IndexedAt { get; init; }
}
