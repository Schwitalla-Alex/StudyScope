using System.Text.Json;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Feste Beispiel-Mindmap für den Testmodus (Fake-KI). Wird von <see cref="FakeLlmService"/>
/// und dem Fake-Zweig des LlmServiceDispatcher genutzt.
/// </summary>
public static class FakeMindmapResponse
{
    public static string Create(IReadOnlyList<string> chunks, bool english = false)
    {
        var en = english;
        var branches = chunks
            .Take(5)
            .Select((chunk, i) => new
            {
                title = FakeLanguage.T(en, $"Textstelle {i + 1}", $"Excerpt {i + 1}"),
                summary = Preview(chunk, 80),
                children = Words(chunk)
                    .Take(3)
                    .Select(w => new { title = w, summary = FakeLanguage.T(en, $"Begriff aus Textstelle {i + 1}.", $"Term from excerpt {i + 1}."), children = Array.Empty<object>() })
                    .ToArray()
            })
            .ToArray();

        var root = new[]
        {
            new
            {
                title = FakeLanguage.T(en, "[Testmodus] Mindmap", "[Test mode] Mind map"),
                summary = FakeLanguage.Plural(en, "Beispiel-Mindmap aus {count, plural, one {# Textstelle} other {# Textstellen}}. Für echte Inhalte in den Einstellungen eine KI wählen.",
                    "Sample mind map from {count, plural, one {# excerpt} other {# excerpts}}. Choose an AI in the settings for real content.", chunks.Count),
                children = branches
            }
        };

        return JsonSerializer.Serialize(root);
    }

    private static string Preview(string text, int length)
    {
        var t = text.Trim().Replace('\n', ' ');
        return t.Length <= length ? t : t[..length] + "…";
    }

    /// <summary>Die ersten längeren Wörter einer Textstelle als Unterpunkte.</summary>
    private static IEnumerable<string> Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', ';', ':', '!', '?', '(', ')', '"', '„', '“'))
            .Where(w => w.Length >= 6)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
