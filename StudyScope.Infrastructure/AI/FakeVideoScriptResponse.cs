using System.Text.Json;
using System.Text.RegularExpressions;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Beispiel-Video-Skript für den Testmodus (Fake-KI): je Folie aus den Auszügen („Folie N: Titel“ samt Stichpunkten)
/// ein kurzer Sprechtext. Wird von <see cref="FakeLlmService"/> und dem Fake-Zweig des LlmServiceDispatcher nach Bericht,
/// Datentabelle und Audio-Skript geprüft – vor Mindmap, Präsentation, Quiz und Karteikarten, weil Folientitel Wörter wie
/// „Mindmap“ oder „Quiz“ enthalten können und der Prompt „JSON“ enthält.
/// </summary>
public static partial class FakeVideoScriptResponse
{
    public static bool IsVideoScriptPrompt(string prompt) =>
        prompt.StartsWith("Erstelle ein Video-Skript", StringComparison.OrdinalIgnoreCase)
        && prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase);

    public static string Create(string prompt, IReadOnlyList<string> chunks)
    {
        var scenes = new List<object>();
        foreach (var chunk in chunks)
        {
            var match = SlideHeader().Match(chunk);
            if (!match.Success) continue;

            var index = int.Parse(match.Groups["n"].Value) - 1;
            var title = match.Groups["title"].Value.Replace("(Titelfolie)", string.Empty).Trim();
            var bullets = chunk.Split('\n')
                .Where(l => l.TrimStart().StartsWith("- "))
                .Select(l => l.Trim()[2..].Trim().TrimEnd('.'))
                .Take(3)
                .ToList();

            var narration = index == 0 || chunk.Contains("(Titelfolie)")
                ? $"Willkommen zu diesem Erklärvideo im Testmodus. Heute geht es um {title}."
                : bullets.Count == 0
                    ? $"Auf dieser Folie geht es um {title}."
                    : $"Auf dieser Folie geht es um {title}. Wichtig sind vor allem diese Punkte: {string.Join(". ", bullets)}.";
            scenes.Add(new { slideIndex = index, narration });
        }

        var script = new[] { new { title = "Testmodus: Videoübersicht", scenes } };
        return JsonSerializer.Serialize(script, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    [GeneratedRegex(@"^Folie (?<n>\d+):\s*(?<title>[^\n]*)")]
    private static partial Regex SlideHeader();
}
