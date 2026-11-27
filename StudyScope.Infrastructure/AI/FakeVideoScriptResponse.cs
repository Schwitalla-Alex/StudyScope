using System.Text.Json;
using System.Text.RegularExpressions;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Beispiel-Video-Skript für den Testmodus (Fake-KI): je Folie aus den Auszügen („Folie N: Titel“ samt Stichpunkten)
/// ein kurzer Sprechtext; für Aufträge mit Kennzeile <c>VideoScript</c> (<see cref="FakeAiRouter"/>).
/// </summary>
public static partial class FakeVideoScriptResponse
{
    public static string Create(string prompt, IReadOnlyList<string> chunks)
    {
        var en = FakeLanguage.IsEnglish(prompt);
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
                ? FakeLanguage.T(en, $"Willkommen zu diesem Erklärvideo im Testmodus. Heute geht es um {title}.", $"Welcome to this explainer video in test mode. The topic is {title}.")
                : bullets.Count == 0
                    ? FakeLanguage.T(en, $"Auf dieser Folie geht es um {title}.", $"This slide is about {title}.")
                    : FakeLanguage.T(en, $"Auf dieser Folie geht es um {title}. Wichtig sind vor allem diese Punkte: {string.Join(". ", bullets)}.",
                        $"This slide is about {title}. These points matter most: {string.Join(". ", bullets)}.");
            scenes.Add(new { slideIndex = index, narration });
        }

        var script = new[] { new { title = FakeLanguage.T(en, "Testmodus: Videoübersicht", "Test mode: video overview"), scenes } };
        return JsonSerializer.Serialize(script, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    [GeneratedRegex(@"^Folie (?<n>\d+):\s*(?<title>[^\n]*)")]
    private static partial Regex SlideHeader();
}
