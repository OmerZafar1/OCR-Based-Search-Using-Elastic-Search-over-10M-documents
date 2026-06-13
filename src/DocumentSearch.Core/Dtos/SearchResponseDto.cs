namespace DocumentSearch.Core.Dtos;

public sealed class SearchResponseDto
{
    public long Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public IReadOnlyList<SearchHitDto> Hits { get; init; } = [];
}

public sealed class SearchHitDto
{
    public Guid DocumentId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FolderPath { get; init; } = string.Empty;
    public double Score { get; init; }
    public string? Highlight { get; init; }
}
