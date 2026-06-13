namespace DocumentSearch.Core.Options;

public class OcrOptions
{
    public const string SectionName = "Ocr";

    public string TesseractDataPath { get; set; } = "./tessdata";
    public string Language { get; set; } = "eng";
    public int MinCharsPerPageForDigitalPdf { get; set; } = 50;
}
