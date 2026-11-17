using System.Text.Json;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Beispiel-Sprechskript für den Testmodus (Fake-KI). Wird von <see cref="FakeLlmService"/> und dem Fake-Zweig des
/// LlmServiceDispatcher nach Bericht und Datentabelle geprüft – vor Mindmap, Präsentation, Quiz und Karteikarten,
/// weil ein Schwerpunkt des Benutzers Wörter wie „Mindmap“ oder „Quiz“ enthalten kann.
/// Beim Format „Podcast-Gespräch“ sprechen Moderatorin und Experte abwechselnd.
/// </summary>
public static class FakeAudioScriptResponse
{
    public static bool IsAudioScriptPrompt(string prompt) =>
        prompt.StartsWith("Erstelle ein Audio-Skript", StringComparison.OrdinalIgnoreCase)
        && prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase);

    public static string Create(string prompt, IReadOnlyList<string> chunks)
    {
        var isPodcast = prompt.Contains("Format: Podcast-Gespräch", StringComparison.OrdinalIgnoreCase);
        var isBriefing = prompt.Contains("Format: Kurz-Briefing", StringComparison.OrdinalIgnoreCase);

        var sentences = chunks
            .SelectMany(c => c.Replace('\n', ' ').Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Where(s => s.Length >= 20)
            .Take(isBriefing ? 4 : 8)
            .Select(s => (s.Length > 220 ? s[..220].TrimEnd() : s) + ".")
            .ToList();
        if (sentences.Count == 0)
            sentences.Add("Die Unterlagen enthalten nur sehr wenig Text.");

        var segments = new List<object>();
        if (isPodcast)
        {
            segments.Add(new { speaker = "Moderatorin", text = "Herzlich willkommen! Heute schauen wir uns gemeinsam Ihre Unterlagen an. Worum geht es denn?" });
            for (var i = 0; i < sentences.Count; i++)
            {
                segments.Add(new { speaker = "Experte", text = $"Ein wichtiger Punkt ist dieser: {sentences[i]}" });
                if (i < sentences.Count - 1)
                    segments.Add(new { speaker = "Moderatorin", text = "Spannend. Und was gehört noch dazu?" });
            }
            segments.Add(new { speaker = "Moderatorin", text = "Vielen Dank! Das war unser Überblick im Testmodus." });
        }
        else
        {
            var speaker = "Sprecherin";
            segments.Add(new { speaker, text = isBriefing ? "Hier das Wichtigste in Kürze." : "In dieser Zusammenfassung geht es um die wichtigsten Inhalte Ihrer Unterlagen." });
            segments.AddRange(sentences.Select(s => (object)new { speaker, text = s }));
            segments.Add(new { speaker, text = "Das war die Zusammenfassung im Testmodus." });
        }

        var script = new[]
        {
            new
            {
                title = isPodcast ? "Testmodus: Podcast zu Ihren Unterlagen" : isBriefing ? "Testmodus: Kurz-Briefing" : "Testmodus: Audio-Zusammenfassung",
                segments
            }
        };

        return JsonSerializer.Serialize(script, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
