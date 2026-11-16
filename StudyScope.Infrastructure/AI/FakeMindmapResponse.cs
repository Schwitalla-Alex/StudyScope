using System.Text.Json;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Feste Beispiel-Mindmap für den Testmodus (Fake-KI). Wird von <see cref="FakeLlmService"/>
/// und dem Fake-Zweig des LlmServiceDispatcher genutzt.
/// </summary>
public static class FakeMindmapResponse
{
    /// <summary>Erkennt eine Mindmap-Anfrage am Prompt.</summary>
    public static bool IsMindmapPrompt(string prompt) =>
        prompt.Contains("Mindmap", StringComparison.OrdinalIgnoreCase);

    public static string Create(IReadOnlyList<string> chunks)
    {
        var branches = chunks
            .Take(5)
            .Select((chunk, i) => new
            {
                title = $"Textstelle {i + 1}",
                summary = Preview(chunk, 80),
                children = Words(chunk)
                    .Take(3)
                    .Select(w => new { title = w, summary = $"Begriff aus Textstelle {i + 1}.", children = Array.Empty<object>() })
                    .ToArray()
            })
            .ToArray();

        var root = new[]
        {
            new
            {
                title = "[Testmodus] Mindmap",
                summary = $"Beispiel-Mindmap aus {chunks.Count} Textstelle(n). Für echte Inhalte in den Einstellungen eine KI wählen.",
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
