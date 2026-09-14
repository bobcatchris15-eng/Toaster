using System.Text;
using TesseractOCR;
using TesseractOCR.Enums;
using UglyToad.PdfPig.Content;

namespace Toaster.Service;

internal static class OcrTextExtractor
{
    private const int MinimumImagePixels = 180_000;
    private const float MinimumConfidence = 0.20f;
    private static readonly object Gate = new();
    private static Engine? _engine;
    private static bool _unavailable;

    public static string? TryReadPage(UglyToad.PdfPig.Content.Page page)
    {
        if (_unavailable) return null;

        var images = page.GetImages()
            .Where(image => (long)image.WidthInSamples * image.HeightInSamples >= MinimumImagePixels)
            .OrderByDescending(image => (long)image.WidthInSamples * image.HeightInSamples)
            .ToArray();

        if (images.Length == 0) return null;

        lock (Gate)
        {
            try
            {
                var engine = GetEngine();
                if (engine is null) return null;

                var text = new StringBuilder();
                foreach (var image in images)
                {
                    try
                    {
                        if (!image.TryGetPng(out var pngBytes)) continue;
                        using var pix = TesseractOCR.Pix.Image.LoadFromMemory(pngBytes);
                        using var result = engine.Process(pix);
                        var recognized = result.Text?.Trim();
                        if (result.MeanConfidence < MinimumConfidence || string.IsNullOrWhiteSpace(recognized)) continue;
                        if (text.Length > 0) text.AppendLine().AppendLine();
                        text.Append(recognized);
                    }
                    catch
                    {
                        // A malformed or unsupported embedded image must not make manual ingestion fail.
                    }
                }

                return text.Length == 0 ? null : text.ToString();
            }
            catch (DllNotFoundException)
            {
                _unavailable = true;
                return null;
            }
            catch (TypeInitializationException)
            {
                _unavailable = true;
                return null;
            }
        }
    }

    private static Engine? GetEngine()
    {
        if (_engine is not null) return _engine;

        var tessData = Environment.GetEnvironmentVariable("TOASTER_TESSDATA");
        if (string.IsNullOrWhiteSpace(tessData))
            tessData = Path.Combine(AppContext.BaseDirectory, "tessdata");

        if (!File.Exists(Path.Combine(tessData, "eng.traineddata"))) return null;

        _engine = new Engine(tessData, Language.English, EngineMode.Default);
        return _engine;
    }
}
