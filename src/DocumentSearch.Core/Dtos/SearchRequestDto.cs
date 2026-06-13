namespace DocumentSearch.Core.Dtos;

public sealed class SearchRequestDto
{
    public required string Query { get; init; }
    public Guid? FolderId { get; init; }
    public bool IncludeSubfolders { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
