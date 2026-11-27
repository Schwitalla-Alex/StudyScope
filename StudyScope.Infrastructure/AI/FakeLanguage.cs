using StudyScope.Core.Localization;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Sprache der Fake-KI-Antworten: Englisch, wenn die Sprachzeile des Prompts Englisch verlangt
/// (<see cref="OutputLanguage.PromptLine"/>), sonst Deutsch. So lassen sich Oberfläche und Ergebnissprache ohne echte
/// KI prüfen. Erkannt werden die Aufträge weiterhin an den deutschen Merkmalen des Prompts.
/// </summary>
public static class FakeLanguage
{
    public static bool IsEnglish(string? prompt) =>
        OutputLanguage.TryParse(prompt)?.TwoLetterISOLanguageName == "en";

    /// <summary>Text je nach Sprache.</summary>
    public static string T(bool english, string german, string englishText) => english ? englishText : german;

    /// <summary>Karteikarten-Antwort (Prompt mit „Lernkarteikarten“ bzw. „JSON“), gemeinsam für Dispatcher und <see cref="FakeLlmService"/>.</summary>
    public static string Flashcards(string prompt, IReadOnlyList<string> chunks)
    {
        var en = IsEnglish(prompt);
        var snippet = chunks.FirstOrDefault() ?? T(en, "Beispielinhalt", "Sample content");
        var preview = snippet.Substring(0, Math.Min(40, snippet.Length)).Replace("\"", "'");
        var q1 = T(en, "Was besagt der Textauszug?", "What does the excerpt say?");
        var a1 = T(en, $"Der Auszug lautet auszugsweise: '{preview}...'", $"The excerpt begins: '{preview}...'");
        var q2 = T(en, "Wie viele Textstellen wurden analysiert?", "How many excerpts were analysed?");
        var a2 = T(en, $"Es wurden genau {chunks.Count} Textstelle(n) analysiert.", $"Exactly {chunks.Count} excerpt(s) were analysed.");
        return System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { question = q1, answer = a1 },
            new { question = q2, answer = a2 }
        });
    }

    /// <summary>Chat-Antwort der Fake-KI (Frage ohne Sprachzeile wiedergegeben).</summary>
    public static string Chat(string prompt, IReadOnlyList<string> chunks)
    {
        var en = IsEnglish(prompt);
        var question = OutputLanguage.WithoutLine(prompt);
        if (chunks.Count == 0)
            return T(en, $"[Testmodus] Keine Dokumentinhalte aktiv. Ihre Frage war: '{question}'",
                $"[Test mode] No document content is active. Your question was: '{question}'");

        var firstPreview = chunks[0].Substring(0, Math.Min(50, chunks[0].Length));
        return T(en, $"[Testmodus] Antwort basierend auf {chunks.Count} Textstelle(n).\nFrage: {question}\nAuszug: {firstPreview}...",
            $"[Test mode] Answer based on {chunks.Count} excerpt(s).\nQuestion: {question}\nExcerpt: {firstPreview}...");
    }
}
