using System.Text.Json;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Beispiel-Bericht für den Testmodus (Fake-KI). Wird von <see cref="FakeLlmService"/> und dem
/// Fake-Zweig des LlmServiceDispatcher genutzt – und dort als Erstes geprüft, weil eine eigene
/// Vorgabe des Benutzers auch Wörter wie „Mindmap“ oder „Präsentation“ enthalten kann.
/// </summary>
public static class FakeReportResponse
{
    public static bool IsReportPrompt(string prompt) =>
        prompt.StartsWith("Erstelle einen Bericht", StringComparison.OrdinalIgnoreCase)
        && prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase);

    public static string Create(string prompt, IReadOnlyList<string> chunks)
    {
        var sentences = chunks
            .SelectMany(c => c.Replace('\n', ' ').Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Where(s => s.Length >= 15)
            .Take(12)
            .ToList();
        if (sentences.Count == 0)
            sentences.Add("Die aktivierten Dokumente enthalten kaum Text");

        var words = chunks
            .SelectMany(c => c.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.Trim('.', ',', ';', ':', '!', '?', '(', ')', '"', '„', '“'))
            .Where(w => w.Length >= 7)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        var kind = prompt.Contains("Glossar.") ? "Glossar"
            : prompt.Contains("FAQ.") ? "FAQ"
            : prompt.Contains("Zusammenfassung / Briefing") ? "Zusammenfassung"
            : prompt.Contains("Lernübersicht") ? "Lernübersicht"
            : "Bericht";

        var sections = sentences
            .Chunk(3)
            .Take(3)
            .Select((group, i) => new
            {
                heading = $"Abschnitt {i + 1}",
                paragraphs = new[] { string.Join(". ", group) + "." },
                bullets = group.Select(s => s.Length > 70 ? s[..70] + "…" : s).ToArray(),
                terms = words.Skip(i * 2).Take(2).Select(w => new { term = w, definition = $"Begriff aus den Unterlagen (Testmodus)." }).ToArray(),
                faq = new[] { new { question = $"Worum geht es in Abschnitt {i + 1}?", answer = group[0] + "." } },
                takeaway = $"Merksatz {i + 1}: {group[0]}."
            })
            .ToArray();

        var report = new[]
        {
            new
            {
                title = $"[Testmodus] {kind}",
                summary = $"Beispiel-{kind} der Fake-KI aus {chunks.Count} Textstelle(n). Für echte Inhalte in den Einstellungen eine KI wählen.",
                sections
            }
        };

        return JsonSerializer.Serialize(report);
    }
}
