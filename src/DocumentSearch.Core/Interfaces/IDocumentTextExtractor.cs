using DocumentSearch.Core.Enums;

namespace DocumentSearch.Core.Interfaces;

public interface IDocumentTextExtractor
{
    Task<ExtractionResult> ExtractAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default);
}

public sealed class ExtractionResult
{
    public required string Text { get; init; }
    public DocumentKind DocumentKind { get; init; }
    public int PageCount { get; init; }
}
