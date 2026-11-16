using System.Text.Json;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Feste Beispiel-Präsentation für den Testmodus (Fake-KI). Wird von <see cref="FakeLlmService"/>
/// und dem Fake-Zweig des LlmServiceDispatcher genutzt.
/// </summary>
public static class FakePresentationResponse
{
    /// <summary>
    /// Erkennt eine Präsentations-Anfrage am Prompt. Bewusst eng gefasst, damit eine Chat-Frage,
    /// die nur das Wort „Präsentation“ enthält, im Testmodus nicht als Folien-JSON beantwortet wird.
    /// </summary>
    public static bool IsPresentationPrompt(string prompt) =>
        prompt.Contains("Erstelle eine Präsentation", StringComparison.OrdinalIgnoreCase)
        && prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase);

    public static string Create(IReadOnlyList<string> chunks)
    {
        var slides = chunks
            .Take(4)
            .Select((chunk, i) => new
            {
                title = $"Textstelle {i + 1}",
                bullets = Sentences(chunk).Take(3).DefaultIfEmpty("Kein Text vorhanden").ToArray(),
                notes = $"Im Testmodus wird hier Textstelle {i + 1} zusammengefasst."
            })
            .Append(new
            {
                title = "Fazit",
                bullets = new[] { $"{chunks.Count} Textstelle(n) ausgewertet", "Für echte Inhalte in den Einstellungen eine KI wählen" },
                notes = "Dies ist eine Beispiel-Präsentation der Fake-KI."
            })
            .ToArray();

        var presentation = new[]
        {
            new { title = "[Testmodus] Präsentation", subtitle = "Beispiel aus den aktivierten Dokumenten", slides }
        };

        return JsonSerializer.Serialize(presentation);
    }

    private static IEnumerable<string> Sentences(string text) =>
        text.Replace('\n', ' ')
            .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length >= 8)
            .Select(s => s.Length > 90 ? s[..90] + "…" : s);
}
