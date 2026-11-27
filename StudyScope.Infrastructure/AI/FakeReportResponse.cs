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
        var en = FakeLanguage.IsEnglish(prompt);
        var sentences = chunks
            .SelectMany(c => c.Replace('\n', ' ').Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Where(s => s.Length >= 15)
            .Take(12)
            .ToList();
        if (sentences.Count == 0)
            sentences.Add(FakeLanguage.T(en, "Die aktivierten Dokumente enthalten kaum Text", "The selected documents contain hardly any text"));

        var words = chunks
            .SelectMany(c => c.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.Trim('.', ',', ';', ':', '!', '?', '(', ')', '"', '„', '“'))
            .Where(w => w.Length >= 7)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        var kind = prompt.Contains("Glossar.") ? FakeLanguage.T(en, "Glossar", "Glossary")
            : prompt.Contains("FAQ.") ? "FAQ"
            : prompt.Contains("Zusammenfassung / Briefing") ? FakeLanguage.T(en, "Zusammenfassung", "Summary")
            : prompt.Contains("Lernübersicht") ? FakeLanguage.T(en, "Lernübersicht", "Study guide")
            : FakeLanguage.T(en, "Bericht", "Report");

        var sections = sentences
            .Chunk(3)
            .Take(3)
            .Select((group, i) => new
            {
                heading = FakeLanguage.T(en, $"Abschnitt {i + 1}", $"Section {i + 1}"),
                paragraphs = new[] { string.Join(". ", group) + "." },
                bullets = group.Select(s => s.Length > 70 ? s[..70] + "…" : s).ToArray(),
                terms = words.Skip(i * 2).Take(2).Select(w => new { term = w, definition = FakeLanguage.T(en, "Begriff aus den Unterlagen (Testmodus).", "Term from your documents (test mode).") }).ToArray(),
                faq = new[] { new { question = FakeLanguage.T(en, $"Worum geht es in Abschnitt {i + 1}?", $"What is section {i + 1} about?"), answer = group[0] + "." } },
                takeaway = FakeLanguage.T(en, $"Merksatz {i + 1}: {group[0]}.", $"Key point {i + 1}: {group[0]}.")
            })
            .ToArray();

        var report = new[]
        {
            new
            {
                title = FakeLanguage.T(en, $"[Testmodus] {kind}", $"[Test mode] {kind}"),
                summary = FakeLanguage.T(en, $"Beispiel-{kind} der Fake-KI aus {chunks.Count} Textstelle(n). Für echte Inhalte in den Einstellungen eine KI wählen.",
                    $"Sample {kind.ToLowerInvariant()} by the fake AI from {chunks.Count} excerpt(s). Choose an AI in the settings for real content."),
                sections
            }
        };

        return JsonSerializer.Serialize(report);
    }
}
