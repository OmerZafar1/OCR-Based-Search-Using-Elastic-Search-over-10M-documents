namespace DocumentSearch.Core.Dtos;

public sealed class DocumentStatsDto
{
    public long Total { get; init; }
    public long Pending { get; init; }
    public long Processing { get; init; }
    public long Indexed { get; init; }
    public long Failed { get; init; }
}
