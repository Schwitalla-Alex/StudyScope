using System.Text.Json;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Feste Beispiel-Präsentation für den Testmodus (Fake-KI). Wird von <see cref="FakeLlmService"/>
/// und dem Fake-Zweig des LlmServiceDispatcher genutzt.
/// </summary>
public static class FakePresentationResponse
{
    public static string Create(IReadOnlyList<string> chunks, bool english = false)
    {
        var en = english;
        var slides = chunks
            .Take(4)
            .Select((chunk, i) => new
            {
                title = FakeLanguage.T(en, $"Textstelle {i + 1}", $"Excerpt {i + 1}"),
                bullets = Sentences(chunk).Take(3).DefaultIfEmpty(FakeLanguage.T(en, "Kein Text vorhanden", "No text available")).ToArray(),
                notes = FakeLanguage.T(en, $"Im Testmodus wird hier Textstelle {i + 1} zusammengefasst.", $"In test mode, excerpt {i + 1} is summarised here.")
            })
            .Append(new
            {
                title = FakeLanguage.T(en, "Fazit", "Conclusion"),
                bullets = new[] { FakeLanguage.Plural(en, "{count, plural, one {# Textstelle} other {# Textstellen}} ausgewertet", "{count, plural, one {# excerpt} other {# excerpts}} analysed", chunks.Count), FakeLanguage.T(en, "Für echte Inhalte in den Einstellungen eine KI wählen", "Choose an AI in the settings for real content") },
                notes = FakeLanguage.T(en, "Dies ist eine Beispiel-Präsentation der Fake-KI.", "This is a sample presentation from the fake AI.")
            })
            .ToArray();

        var presentation = new[]
        {
            new { title = FakeLanguage.T(en, "[Testmodus] Präsentation", "[Test mode] Presentation"), subtitle = FakeLanguage.T(en, "Beispiel aus den aktivierten Dokumenten", "Sample from the selected documents"), slides }
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
