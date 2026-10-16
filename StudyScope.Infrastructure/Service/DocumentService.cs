using System.Text;
using StudyScope.Core.Interfaces;
using UglyToad.PdfPig;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Dokumenten-Parser-Dienst für .pdf-, .txt- und .csv-Dateien zum Extrahieren von Textstellen (Chunks).
/// </summary>
public class DocumentService : IDocumentService
{
    public async Task<int> GetChunkCountAsync(string filePath)
    {
        var chunks = await ExtractChunksAsync(filePath);
        return chunks.Count;
    }

    public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return new List<string>();

            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();

            return extension switch
            {
                ".txt" => ExtractFromTxt(filePath, maxWordsPerChunk),
                ".csv" => ExtractFromCsv(filePath, maxWordsPerChunk),
                ".pdf" => ExtractFromPdf(filePath, maxWordsPerChunk),
                _ => ExtractFromPdf(filePath, maxWordsPerChunk)
            };
        });
    }

    private static List<string> ExtractFromPdf(string filePath, int maxWordsPerChunk)
    {
        var fullTextBuilder = new StringBuilder();

        try
        {
            using var document = PdfDocument.Open(filePath);
            foreach (var page in document.GetPages())
            {
                if (!string.IsNullOrWhiteSpace(page.Text))
                {
                    fullTextBuilder.AppendLine(page.Text);
                }
            }
        }
        catch
        {
            return new List<string>();
        }

        return SplitIntoChunks(fullTextBuilder.ToString(), maxWordsPerChunk);
    }

    private static List<string> ExtractFromTxt(string filePath, int maxWordsPerChunk)
    {
        try
        {
            var text = File.ReadAllText(filePath, Encoding.UTF8);
            return SplitIntoChunks(text, maxWordsPerChunk);
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<string> ExtractFromCsv(string filePath, int maxWordsPerChunk)
    {
        try
        {
            var lines = File.ReadAllLines(filePath, Encoding.UTF8)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList();

            if (!lines.Any())
                return new List<string>();

            var header = lines.First();
            var result = new List<string>();
            var currentChunk = new StringBuilder();
            var currentWordCount = 0;

            currentChunk.AppendLine($"[CSV-Spalten: {header}]");

            foreach (var line in lines.Skip(1))
            {
                var lineWordCount = line.Split(new[] { ' ', ';', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

                if (currentWordCount + lineWordCount > maxWordsPerChunk && currentWordCount > 0)
                {
                    result.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                    currentChunk.AppendLine($"[CSV-Spalten: {header}]");
                    currentWordCount = 0;
                }

                currentChunk.AppendLine(line);
                currentWordCount += lineWordCount;
            }

            if (currentChunk.Length > 0)
            {
                result.Add(currentChunk.ToString().Trim());
            }

            return result;
        }
        catch
        {
            return new List<string>();
        }
    }

    public static List<string> SplitIntoChunks(string text, int maxWordsPerChunk = 250)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var words = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return result;

        for (int i = 0; i < words.Length; i += maxWordsPerChunk)
        {
            var chunkWords = words.Skip(i).Take(maxWordsPerChunk);
            result.Add(string.Join(" ", chunkWords));
        }

        return result;
    }
}