using System.Text;
using Toaster.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Toaster.Service;

internal static class ManualParser
{
    private const int TargetChunkChars = 6500;

    public static IReadOnlyList<SourceSection> Parse(Guid sourceId, byte[] bytes, string contentType, string fileName)
    {
        if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return ParsePdf(sourceId, bytes);

        var text = Encoding.UTF8.GetString(bytes);
        return ParseText(sourceId, text);
    }

    public static IReadOnlyList<SourceSection> ParseText(Guid sourceId, string content)
    {
        var chunks = new List<SourceSection>();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        var heading = "Document";
        var ordinal = 0;

        void Flush()
        {
            if (current.Length == 0) return;
            chunks.Add(new SourceSection(Guid.NewGuid(), sourceId, heading, current.ToString().Trim(), ordinal++));
            current.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith('#'))
            {
                Flush();
                heading = line.TrimStart('#', ' ');
                continue;
            }

            current.AppendLine(line);
            if (current.Length >= TargetChunkChars) Flush();
        }

        Flush();
        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(content))
            chunks.Add(new SourceSection(Guid.NewGuid(), sourceId, "Document", content.Trim(), 0));
        return chunks;
    }

    private static IReadOnlyList<SourceSection> ParsePdf(Guid sourceId, byte[] bytes)
    {
        var sections = new List<SourceSection>();
        using var document = PdfDocument.Open(bytes);
        var ordinal = 0;
        var pageNumber = 0;

        foreach (var page in document.GetPages())
        {
            pageNumber++;
            var text = ContentOrderTextExtractor.GetText(page)?.Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;

            var parts = Chunk(text, TargetChunkChars);
            for (var i = 0; i < parts.Count; i++)
            {
                var heading = parts.Count == 1 ? $"Page {pageNumber}" : $"Page {pageNumber} — part {i + 1}";
                sections.Add(new SourceSection(Guid.NewGuid(), sourceId, heading, parts[i], ordinal++));
            }
        }

        return sections;
    }

    private static List<string> Chunk(string text, int target)
    {
        if (text.Length <= target) return [text];
        var result = new List<string>();
        var paragraphs = text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > target)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }

            if (paragraph.Length > target)
            {
                if (current.Length > 0) { result.Add(current.ToString().Trim()); current.Clear(); }
                for (var start = 0; start < paragraph.Length; start += target)
                    result.Add(paragraph.Substring(start, Math.Min(target, paragraph.Length - start)).Trim());
            }
            else
            {
                current.AppendLine(paragraph);
                current.AppendLine();
            }
        }

        if (current.Length > 0) result.Add(current.ToString().Trim());
        return result.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }
}
