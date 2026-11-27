using System.Text.Json;
using System.Text.RegularExpressions;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Beispielantworten des Testmodus (Fake-KI) für das Quiz: Themenvorschläge und Fragen aller drei Typen.
/// Genutzt von <see cref="FakeLlmService"/> und dem Fake-Zweig des LlmServiceDispatcher.
/// </summary>
public static class FakeQuizResponse
{
    private static readonly Regex CountPattern = new(@"Erstelle (\d+) Quizfragen", RegexOptions.Compiled);

    public static bool IsTopicPrompt(string prompt) =>
        prompt.Contains("Quiz-Themen", StringComparison.OrdinalIgnoreCase) && prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase);

    public static bool IsQuestionPrompt(string prompt) =>
        prompt.Contains("Quizfragen", StringComparison.OrdinalIgnoreCase) && prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase);

    public static string CreateTopics(IReadOnlyList<string> chunks, bool english = false)
    {
        var en = english;
        var topics = chunks
            .Take(4)
            .Select((chunk, i) => new { name = FakeLanguage.T(en, $"Testthema {i + 1}", $"Test topic {i + 1}"), description = FakeLanguage.T(en, $"Inhalte aus Textstelle {i + 1}: {Preview(chunk, 60)}", $"Content from excerpt {i + 1}: {Preview(chunk, 60)}") })
            .ToArray();

        return JsonSerializer.Serialize(topics);
    }

    public static string CreateQuestions(string prompt, IReadOnlyList<string> chunks)
    {
        var en = FakeLanguage.IsEnglish(prompt);
        var match = CountPattern.Match(prompt);
        var count = match.Success ? Math.Clamp(int.Parse(match.Groups[1].Value), 1, 50) : 5;
        var words = chunks.SelectMany(Words).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (words.Count < 4)
            words.AddRange(en ? new[] { "Example", "Placeholder", "Test term", "Sample word" } : new[] { "Beispiel", "Platzhalter", "Testbegriff", "Musterwort" });

        // Zufälliger Zusatz, damit sich Fragen verschiedener Pakete nicht als Wiederholung gegenseitig aufheben
        var random = new Random();
        var questions = new List<object>();
        for (var i = 0; i < count; i++)
        {
            var tag = random.Next(1000, 9999);
            var word = words[i % words.Count];
            switch (i % 3)
            {
                case 0:
                    var options = Enumerable.Range(0, 4).Select(k => words[(i + k) % words.Count]).ToArray();
                    questions.Add(new
                    {
                        type = "single",
                        question = FakeLanguage.T(en, $"[Testmodus #{tag}] Welcher Begriff kommt in den Unterlagen an Position {i + 1} vor?", $"[Test mode #{tag}] Which term appears in the documents at position {i + 1}?"),
                        options = options.Select((o, k) => k == 0 ? o : FakeLanguage.T(en, $"{o} (falsch)", $"{o} (wrong)")).ToArray(),
                        correct = new[] { 0 },
                        explanation = FakeLanguage.T(en, $"„{options[0]}“ stammt aus dem Text (Testmodus).", $"“{options[0]}” comes from the text (test mode).")
                    });
                    break;
                case 1:
                    questions.Add(new
                    {
                        type = "truefalse",
                        question = FakeLanguage.T(en, $"[Testmodus #{tag}] Das Wort „{word}“ kommt in den Unterlagen vor.", $"[Test mode #{tag}] The word “{word}” appears in the documents."),
                        options = en ? new[] { "True", "False" } : new[] { "Wahr", "Falsch" },
                        correct = new[] { 0 },
                        explanation = FakeLanguage.T(en, "Im Testmodus ist diese Aussage immer wahr.", "In test mode this statement is always true.")
                    });
                    break;
                default:
                    questions.Add(new
                    {
                        type = "multiple",
                        question = FakeLanguage.T(en, $"[Testmodus #{tag}] Welche Begriffe stammen aus den Unterlagen? (Nr. {i + 1})", $"[Test mode #{tag}] Which terms come from the documents? (No. {i + 1})"),
                        options = new[] { word, FakeLanguage.T(en, "Erfundenes Wort", "Made-up word"), words[(i + 1) % words.Count], FakeLanguage.T(en, "Nicht im Text", "Not in the text") },
                        correct = new[] { 0, 2 },
                        explanation = FakeLanguage.T(en, "Die beiden echten Begriffe stammen aus dem Text (Testmodus).", "The two real terms come from the text (test mode).")
                    });
                    break;
            }
        }

        return JsonSerializer.Serialize(questions);
    }

    private static string Preview(string text, int length)
    {
        var t = text.Trim().Replace('\n', ' ');
        return t.Length <= length ? t : t[..length] + "…";
    }

    private static IEnumerable<string> Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', ';', ':', '!', '?', '(', ')', '"', '„', '“'))
            .Where(w => w.Length >= 6);
}
