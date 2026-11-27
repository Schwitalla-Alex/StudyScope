using System.Text.Json;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Beispiel-Sprechskript für den Testmodus (Fake-KI) für Aufträge mit Kennzeile <c>AudioScript</c>
/// (<see cref="FakeAiRouter"/>, gemeinsam für <see cref="FakeLlmService"/> und den Dispatcher).
/// Beim Format „Podcast-Gespräch“ sprechen Moderatorin und Experte abwechselnd.
/// </summary>
public static class FakeAudioScriptResponse
{
    public static string Create(string prompt, IReadOnlyList<string> chunks)
    {
        var isPodcast = prompt.Contains("Format: Podcast-Gespräch", StringComparison.OrdinalIgnoreCase);
        var isBriefing = prompt.Contains("Format: Kurz-Briefing", StringComparison.OrdinalIgnoreCase);
        // Texte englisch, wenn die Sprachzeile es verlangt – dann auch mit englischen Rollen („Host“, „Expert“, „Narrator“),
        // wie sie echte Modelle oft liefern; AudioScriptService.NormalizeSpeakers ordnet sie den gespeicherten Rollen zu
        var en = FakeLanguage.IsEnglish(prompt);
        var host = en ? "Host" : "Moderatorin";
        var expert = en ? "Expert" : "Experte";

        var sentences = chunks
            .SelectMany(c => c.Replace('\n', ' ').Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Where(s => s.Length >= 20)
            .Take(isBriefing ? 4 : 8)
            .Select(s => (s.Length > 220 ? s[..220].TrimEnd() : s) + ".")
            .ToList();
        if (sentences.Count == 0)
            sentences.Add(FakeLanguage.T(en, "Die Unterlagen enthalten nur sehr wenig Text.", "The documents contain very little text."));

        var segments = new List<object>();
        if (isPodcast)
        {
            segments.Add(new { speaker = host, text = FakeLanguage.T(en, "Herzlich willkommen! Heute schauen wir uns gemeinsam Ihre Unterlagen an. Worum geht es denn?", "Welcome! Today we are taking a look at your documents together. What are they about?") });
            for (var i = 0; i < sentences.Count; i++)
            {
                segments.Add(new { speaker = expert, text = FakeLanguage.T(en, $"Ein wichtiger Punkt ist dieser: {sentences[i]}", $"One important point is this: {sentences[i]}") });
                if (i < sentences.Count - 1)
                    segments.Add(new { speaker = host, text = FakeLanguage.T(en, "Spannend. Und was gehört noch dazu?", "Interesting. And what else is part of it?") });
            }
            segments.Add(new { speaker = host, text = FakeLanguage.T(en, "Vielen Dank! Das war unser Überblick im Testmodus.", "Thank you! That was our overview in test mode.") });
        }
        else
        {
            var speaker = en ? "Narrator" : "Sprecherin";
            segments.Add(new { speaker, text = isBriefing
                ? FakeLanguage.T(en, "Hier das Wichtigste in Kürze.", "Here are the key points in brief.")
                : FakeLanguage.T(en, "In dieser Zusammenfassung geht es um die wichtigsten Inhalte Ihrer Unterlagen.", "This summary covers the most important content of your documents.") });
            segments.AddRange(sentences.Select(s => (object)new { speaker, text = s }));
            segments.Add(new { speaker, text = FakeLanguage.T(en, "Das war die Zusammenfassung im Testmodus.", "That was the summary in test mode.") });
        }

        var script = new[]
        {
            new
            {
                title = isPodcast ? FakeLanguage.T(en, "Testmodus: Podcast zu Ihren Unterlagen", "Test mode: podcast about your documents")
                    : isBriefing ? FakeLanguage.T(en, "Testmodus: Kurz-Briefing", "Test mode: short briefing")
                    : FakeLanguage.T(en, "Testmodus: Audio-Zusammenfassung", "Test mode: audio summary"),
                segments
            }
        };

        return JsonSerializer.Serialize(script, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
