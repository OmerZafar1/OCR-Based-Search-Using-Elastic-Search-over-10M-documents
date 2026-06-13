using DocumentSearch.Core.Enums;
using DocumentSearch.Core.Interfaces;
using DocumentSearch.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;
using UglyToad.PdfPig;
using Docnet.Core;
using Docnet.Core.Models;

namespace DocumentSearch.Infrastructure.Extraction;

public class DocumentTextExtractor : IDocumentTextExtractor
{
    private readonly OcrOptions _ocrOptions;
    private readonly ILogger<DocumentTextExtractor> _logger;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm", ".log"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp"
    };

    public DocumentTextExtractor(IOptions<OcrOptions> ocrOptions, ILogger<DocumentTextExtractor> logger)
    {
        _ocrOptions = ocrOptions.Value;
        _logger = logger;
    }

    public async Task<ExtractionResult> ExtractAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName);

        if (TextExtensions.Contains(extension))
        {
            using var reader = new StreamReader(content, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            var kind = extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) && fileName.Contains("ocr", StringComparison.OrdinalIgnoreCase)
                ? DocumentKind.OcrText
                : DocumentKind.Text;
            return new ExtractionResult { Text = text, DocumentKind = kind, PageCount = 1 };
        }

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await ExtractPdfAsync(content, cancellationToken);
        }

        if (ImageExtensions.Contains(extension))
        {
            var text = await OcrImageStreamAsync(content, cancellationToken);
            return new ExtractionResult { Text = text, DocumentKind = DocumentKind.Image, PageCount = 1 };
        }

        throw new NotSupportedException($"Unsupported file type: {extension}");
    }

    private async Task<ExtractionResult> ExtractPdfAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var memory = new MemoryStream();
        await content.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var document = PdfDocument.Open(memory);
        var pageTexts = new List<string>();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageTexts.Add(page.Text);
        }

        var combined = string.Join("\n\n", pageTexts);
        var pageCount = document.NumberOfPages;
        var avgChars = pageCount == 0 ? 0 : combined.Length / pageCount;

        if (avgChars >= _ocrOptions.MinCharsPerPageForDigitalPdf)
        {
            return new ExtractionResult
            {
                Text = combined,
                DocumentKind = DocumentKind.Pdf,
                PageCount = pageCount
            };
        }

        _logger.LogInformation("PDF appears scanned ({AvgChars} chars/page). Running OCR.", avgChars);
        memory.Position = 0;
        var ocrText = await OcrPdfAsync(memory.ToArray(), cancellationToken);
        return new ExtractionResult
        {
            Text = ocrText,
            DocumentKind = DocumentKind.ScannedPdf,
            PageCount = pageCount
        };
    }

    private async Task<string> OcrPdfAsync(byte[] pdfBytes, CancellationToken cancellationToken)
    {
        var texts = new List<string>();
        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(1080, 1920));

        for (var i = 0; i < docReader.GetPageCount(); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var pageReader = docReader.GetPageReader(i);
            var rawBytes = pageReader.GetImage();
            var width = pageReader.GetPageWidth();
            var height = pageReader.GetPageHeight();

            using var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height);
            await using var pngStream = new MemoryStream();
            await image.SaveAsPngAsync(pngStream, cancellationToken);
            pngStream.Position = 0;
            texts.Add(await OcrImageStreamAsync(pngStream, cancellationToken));
        }

        return string.Join("\n\n", texts);
    }

    private Task<string> OcrImageStreamAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var image = Image.Load<Rgba32>(imageStream);
            image.Mutate(x => x
                .Grayscale()
                .Contrast(1.2f));

            using var processed = new MemoryStream();
            image.SaveAsPng(processed);
            processed.Position = 0;

            using var engine = new TesseractEngine(ResolveTessDataPath(), _ocrOptions.Language, EngineMode.Default);
            using var pix = Pix.LoadFromMemory(processed.ToArray());
            using var page = engine.Process(pix);
            return page.GetText();
        }, cancellationToken);
    }

    private string ResolveTessDataPath()
    {
        if (Path.IsPathRooted(_ocrOptions.TesseractDataPath) && Directory.Exists(_ocrOptions.TesseractDataPath))
        {
            return _ocrOptions.TesseractDataPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tessdata"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _ocrOptions.TesseractDataPath)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), _ocrOptions.TesseractDataPath)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tessdata"))
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"Tesseract tessdata folder not found. Download eng.traineddata into tessdata/ at the solution root. Checked: {string.Join(", ", candidates)}");
    }
}
