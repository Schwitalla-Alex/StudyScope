using StudyScope.Core.Common;
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
        var a2 = Plural(en, "Es {count, plural, one {wurde genau # Textstelle} other {wurden genau # Textstellen}} analysiert.",
            "Exactly {count, plural, one {# excerpt was} other {# excerpts were}} analysed.", chunks.Count);
        return System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { question = q1, answer = a1 },
            new { question = q2, answer = a2 }
        });
    }

    /// <summary>
    /// Chat-Antwort der Fake-KI mit Websuche (Frage mit <see cref="WebContextPrompt.MarkerLine"/>): nennt Unterlagen [D…]
    /// und Web-Treffer [W…] getrennt mit Quellmarken, in der Ergebnissprache. Die Auszüge werden nur gezählt, nie als
    /// Auftrag gelesen.
    /// </summary>
    public static string WebChat(string prompt, IReadOnlyList<string> chunks)
    {
        var en = IsEnglish(prompt);
        var question = WebContextPrompt.WithoutMarker(OutputLanguage.WithoutLine(prompt));
        var documents = chunks.Count(c => c.StartsWith("[D", StringComparison.Ordinal));
        var web = chunks.Count(c => c.StartsWith("[W", StringComparison.Ordinal));
        // Einzahl/Mehrzahl über ICU in der Sprache der Antwort (nicht der Oberfläche) – wie alle Fake-Texte ohne .resx
        var documentPart = documents == 0
            ? T(en, "Aus den Unterlagen: keine passenden Auszüge.", "From your materials: no matching excerpts.")
            : Plural(en, "Aus den Unterlagen: Antwort basierend auf {count, plural, one {# Auszug} other {# Auszügen}} [D1].",
                "From your materials: answer based on {count, plural, one {# excerpt} other {# excerpts}} [D1].", documents);
        var webPart = web == 0
            ? T(en, "Aus dem Internet: keine Treffer.", "From the web: no results.")
            : Plural(en, "Aus dem Internet: ergänzt um {count, plural, one {# Web-Treffer} other {# Web-Treffer}} [W1].",
                "From the web: supplemented by {count, plural, one {# web result} other {# web results}} [W1].", web);
        return T(en, $"[Testmodus] {documentPart}\n{webPart}\nFrage: {question}",
            $"[Test mode] {documentPart}\n{webPart}\nQuestion: {question}");
    }

    /// <summary>ICU-Mehrzahl mit den Regeln der Antwortsprache (Deutsch bzw. Englisch), unabhängig von der Oberfläche.</summary>
    public static string Plural(bool english, string german, string englishText, int count)
    {
        using var scope = CultureScope.Use(System.Globalization.CultureInfo.GetCultureInfo(english ? "en" : "de"));
        return Loc.Format(T(english, german, englishText), ("count", count));
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
        var basis = Plural(en, "[Testmodus] Antwort basierend auf {count, plural, one {# Textstelle} other {# Textstellen}}.",
            "[Test mode] Answer based on {count, plural, one {# excerpt} other {# excerpts}}.", chunks.Count);
        return T(en, $"{basis}\nFrage: {question}\nAuszug: {firstPreview}...",
            $"{basis}\nQuestion: {question}\nExcerpt: {firstPreview}...");
    }
}
