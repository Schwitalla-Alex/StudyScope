using System.Text.Json;
using System.Text.RegularExpressions;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Beispiel-Datentabelle für den Testmodus (Fake-KI) für Aufträge mit Kennzeile <c>DataTable</c>
/// (<see cref="FakeAiRouter"/>, gemeinsam für <see cref="FakeLlmService"/> und den Dispatcher).
/// Übernimmt die Spalten aus dem Prompt und die Quellen aus den Markierungen „[Quelle: …]“ der Textstellen.
/// </summary>
public static partial class FakeDataTableResponse
{
    public static string Create(string prompt, IReadOnlyList<string> chunks)
    {
        var en = FakeLanguage.IsEnglish(prompt);
        var columns = ColumnLine().Match(prompt) is { Success: true } m && !m.Groups["cols"].Value.StartsWith("Wähle", StringComparison.Ordinal)
            ? m.Groups["cols"].Value.Split('|').Select(c => c.Trim()).Where(c => c.Length > 0).ToList()
            : new List<string>();
        if (columns.Count == 0)
            columns = en ? new List<string> { "Term", "Explanation" } : new List<string> { "Begriff", "Erklärung" };

        // Bereits vorhandene Zeilen („- Eintrag“ unter „…gibt es bereits“) nicht wiederholen
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingIndex = prompt.IndexOf("gibt es bereits", StringComparison.OrdinalIgnoreCase);
        if (existingIndex >= 0)
        {
            foreach (var line in prompt[existingIndex..].Split('\n').Where(l => l.StartsWith("- ")))
                existing.Add(line[2..].Split(" – ")[0].Trim());
        }

        var rows = new List<object>();
        var usedWords = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in chunks)
        {
            var source = SourceMarker().Match(chunk) is { Success: true } s ? s.Groups["src"].Value.Trim() : string.Empty;
            var text = SourceMarker().Replace(chunk, string.Empty).Replace('\n', ' ').Trim();

            var sentence = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.Length >= 15) ?? text;
            if (sentence.Length > 120) sentence = sentence[..120] + "…";

            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim('.', ',', ';', ':', '!', '?', '(', ')', '"', '„', '“'))
                .Where(w => w.Length >= 6 && char.IsLetter(w[0]))
                .Where(w => usedWords.Add(w))
                .Take(2)
                .ToList();

            foreach (var word in words)
            {
                // Erste Spalte: Stichwort, zweite: Satz aus dem Text, weitere bleiben leer (nichts erfinden)
                var cells = columns.Select((_, i) => i == 0 ? word : i == 1 ? sentence : string.Empty).ToArray();
                rows.Add(new { cells, source });
                if (rows.Count >= 8) break;
            }

            if (rows.Count >= 8) break;
        }

        var table = new[]
        {
            new
            {
                title = FakeLanguage.T(en, "[Testmodus] Datentabelle", "[Test mode] Data table"),
                columns,
                rows
            }
        };

        return JsonSerializer.Serialize(table, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    [GeneratedRegex(@"^Spalten[^:\n]*:\s*(?<cols>[^\n]+)$", RegexOptions.Multiline)]
    private static partial Regex ColumnLine();

    [GeneratedRegex(@"\[Quelle:\s*(?<src>[^\]]+)\]")]
    private static partial Regex SourceMarker();
}
