using System.Globalization;
using System.Text;
using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Core.Localization;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Quiz-Logik ohne Oberfläche: Themenvorschläge und Fragen per KI (nachsichtige JSON-Auswertung über
/// <see cref="LenientJson"/>), Auswahl passender Textstellen je Thema und Speicherung je Profil
/// unter <c>Quizzes/&lt;Profil&gt;.json</c>.
/// </summary>
public class QuizService : IQuizService
{
    public const int DefaultBatchSize = 10;

    /// <summary>Textstellen je KI-Aufruf (Kontextgröße lokaler Modelle).</summary>
    public const int ChunksPerRequest = 8;

    /// <summary>So viele vorhandene Fragen werden der KI zur Vermeidung von Wiederholungen genannt.</summary>
    private const int MaxExistingQuestionsInPrompt = 40;

    private const int MaxQuestionLength = 400;
    private const int MaxOptionLength = 200;
    private const int MaxExplanationLength = 600;
    private const int MaxOptions = 6;

    private static readonly string[] QuestionListKeys = { "questions", "fragen", "quiz", "items" };
    private static readonly string[] QuestionKeys = { "question", "frage", "text", "title", "titel" };
    private static readonly string[] TypeKeys = { "type", "typ", "art" };
    private static readonly string[] OptionsKeys = { "options", "antworten", "answers", "choices", "auswahl", "optionen" };
    private static readonly string[] CorrectKeys = { "correct", "richtig", "correctindex", "correctindices", "correct_answer", "correctanswer", "answer", "loesung", "lösung", "korrekt" };
    private static readonly string[] ExplanationKeys = { "explanation", "erklaerung", "erklärung", "begruendung", "begründung", "hinweis" };
    private static readonly string[] OptionTextKeys = { "text", "antwort", "answer", "option", "label" };
    private static readonly string[] TopicNameKeys = { "name", "title", "titel", "thema", "topic" };
    private static readonly string[] TopicDescriptionKeys = { "description", "beschreibung", "summary", "zusammenfassung" };
    private static readonly string[] TopicListKeys = { "topics", "themen" };

    private static readonly HashSet<string> TrueWords = new(StringComparer.OrdinalIgnoreCase) { "wahr", "true", "richtig", "ja", "stimmt", "korrekt", "yes" };
    private static readonly HashSet<string> FalseWords = new(StringComparer.OrdinalIgnoreCase) { "falsch", "false", "nein", "unwahr", "stimmt nicht", "inkorrekt", "no" };

    private readonly ILlmService _llmService;
    private readonly string _baseDirectory;

    /// <param name="baseDirectory">Basisordner für die Speicherung (Tests); Standard ist <see cref="AppPaths.UserDataDirectory"/>.</param>
    public QuizService(ILlmService llmService, string? baseDirectory = null, int batchSize = DefaultBatchSize)
    {
        _llmService = llmService;
        _baseDirectory = baseDirectory ?? AppPaths.UserDataDirectory;
        BatchSize = Math.Max(1, batchSize);
    }

    public int BatchSize { get; }

    // ---------------- Themenvorschläge ----------------

    public async Task<QuizTopicSuggestionResult> SuggestTopicsAsync(IEnumerable<string> contextChunks, string? outputLanguage = null, CancellationToken cancellationToken = default)
    {
        var chunks = ChunkSelector.SelectRepresentative(contextChunks, MindmapService.MaxChunks);
        if (chunks.Count == 0)
            return new QuizTopicSuggestionResult { FailureReason = MindmapFailureReason.NoContent };

        var prompt =
            "Schlage Quiz-Themen vor, die in den bereitgestellten Textauszügen behandelt werden.\n" +
            "Antworte AUSSCHLIESSLICH als gültiges JSON-Array, ohne Einleitung, ohne Markdown und ohne Schlusssatz.\n" +
            "Schema:\n" +
            "[{\"name\": \"Thema (höchstens 5 Wörter)\", \"description\": \"Ein Satz, worum es in dem Thema geht\"}]\n" +
            "Regeln:\n" +
            "- 3 bis 8 Themen, die sich inhaltlich klar unterscheiden.\n" +
            "- Nur Themen, zu denen die Auszüge genug Inhalt für mehrere Quizfragen liefern. Sprache: Deutsch.";

        prompt = ResultLanguage.ApplyToPrompt(prompt, ResultLanguage.Resolve(outputLanguage, chunks));
        var raw = await _llmService.AskQuestionAsync(TaskPrompt.Mark(TaskKind.QuizTopics, prompt), chunks, cancellationToken);
        var parsed = LenientJson.Analyze(raw, ParseTopicSuggestions);

        return new QuizTopicSuggestionResult
        {
            Topics = parsed.Value ?? new List<QuizTopicSuggestion>(),
            RawResponse = raw ?? string.Empty,
            FailureReason = parsed.FailureReason
        };
    }

    private static bool IsTopic(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ||
        (LenientJson.GetProperty(element, TopicNameKeys) is { ValueKind: JsonValueKind.String } &&
         !element.EnumerateObject().Any(p => p.Value.ValueKind == JsonValueKind.Array));

    public static List<QuizTopicSuggestion>? ParseTopicSuggestions(JsonElement element)
    {
        // Hülle aus dem JSON-Modus (z. B. {"quizthemen": [...], "dokument": "…"}) auspacken
        element = LenientJson.Unwrap(element, e => LenientJson.IsArrayOf(e, IsTopic));
        var items = GetObjectList(element, TopicListKeys);
        var result = new List<QuizTopicSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var name = item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? string.Empty
                : LenientJson.GetString(item, TopicNameKeys);
            name = Clean(name, 80);
            if (name.Length == 0 || !seen.Add(name))
                continue;

            var description = item.ValueKind == JsonValueKind.Object ? Clean(LenientJson.GetString(item, TopicDescriptionKeys), 300) : string.Empty;
            result.Add(new QuizTopicSuggestion { Name = name, Description = description });
            if (result.Count >= 12)
                break;
        }

        return result.Count > 0 ? result : null;
    }

    // ---------------- Fragen ----------------

    public async Task<QuizQuestionGenerationResult> GenerateQuestionsAsync(
        QuizTopic topic,
        IReadOnlyList<string> topicChunks,
        int count,
        IReadOnlyCollection<QuizQuestionType> allowedTypes,
        int batchIndex,
        string? outputLanguage = null,
        CancellationToken cancellationToken = default)
    {
        var chunks = SelectChunksForTopic(topicChunks, topic, batchIndex);
        if (chunks.Count == 0)
            return new QuizQuestionGenerationResult { FailureReason = MindmapFailureReason.NoContent };

        var types = allowedTypes.Count > 0 ? allowedTypes : new[] { QuizQuestionType.SingleChoice };
        var number = Math.Clamp(count, 1, BatchSize);
        // Vorhandene Fragen des Themas geben die Sprache vor (keine gemischten Themen aus Versehen)
        var existingLanguage = topic.Questions.Select(q => q.Language).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        var language = ResultLanguage.FromCode(existingLanguage) ?? ResultLanguage.Resolve(outputLanguage, chunks);
        var prompt = TaskPrompt.Mark(TaskKind.QuizQuestions, ResultLanguage.ApplyToPrompt(BuildQuestionPrompt(topic, number, types), language));

        var raw = await _llmService.AskQuestionAsync(prompt, chunks, cancellationToken);
        var existing = topic.Questions.Select(q => q.Text);
        var parsed = LenientJson.Analyze(raw, element => ParseQuestions(element, types, existing));
        if (parsed.Value != null)
        {
            var trueText = ResultLanguage.Content(language, () => ContentStrings.Content_True);
            var falseText = ResultLanguage.Content(language, () => ContentStrings.Content_False);
            foreach (var question in parsed.Value)
            {
                question.Language = ResultLanguage.Code(language);
                if (question.Type == QuizQuestionType.TrueFalse)
                    question.Options = new List<string> { trueText, falseText };
            }
        }

        return new QuizQuestionGenerationResult
        {
            Questions = parsed.Value ?? new List<QuizQuestion>(),
            RawResponse = raw ?? string.Empty,
            WasRepaired = parsed.WasRepaired,
            FailureReason = parsed.FailureReason
        };
    }

    /// <summary>Prompt für ein Paket Quizfragen. Enthält „Quizfragen“ (Erkennung der Fake-KI), aber weder „Mindmap“ noch „Präsentation“.</summary>
    public static string BuildQuestionPrompt(QuizTopic topic, int count, IReadOnlyCollection<QuizQuestionType> types)
    {
        var sb = new StringBuilder();
        sb.Append($"Erstelle {count} Quizfragen zum Thema „{topic.Name}“");
        if (!string.IsNullOrWhiteSpace(topic.Description))
            sb.Append($" ({topic.Description})");
        sb.Append(" aus den bereitgestellten Textauszügen.\n");
        sb.Append("Antworte AUSSCHLIESSLICH als gültiges JSON-Array, ohne Einleitung, ohne Markdown und ohne Schlusssatz.\n");
        sb.Append("Erlaubte Fragetypen (bitte mischen):\n");

        if (types.Contains(QuizQuestionType.SingleChoice))
            sb.Append("- \"single\": genau 4 Antworten, genau EINE richtig. Beispiel: {\"type\": \"single\", \"question\": \"…?\", \"options\": [\"A\", \"B\", \"C\", \"D\"], \"correct\": [2], \"explanation\": \"…\"}\n");
        if (types.Contains(QuizQuestionType.TrueFalse))
            sb.Append("- \"truefalse\": eine Aussage, die wahr oder falsch ist. Beispiel: {\"type\": \"truefalse\", \"question\": \"Aussage …\", \"options\": [\"Wahr\", \"Falsch\"], \"correct\": [1], \"explanation\": \"…\"}\n");
        if (types.Contains(QuizQuestionType.MultipleChoice))
            sb.Append("- \"multiple\": 4 oder 5 Antworten, MINDESTENS ZWEI richtig. Beispiel: {\"type\": \"multiple\", \"question\": \"Welche … treffen zu?\", \"options\": [\"A\", \"B\", \"C\", \"D\"], \"correct\": [0, 3], \"explanation\": \"…\"}\n");

        sb.Append("Regeln:\n");
        sb.Append("- \"correct\" enthält die Positionen der richtigen Antworten, gezählt ab 0.\n");
        sb.Append("- Falsche Antworten müssen plausibel klingen, dürfen aber laut Text eindeutig falsch sein.\n");
        sb.Append("- \"explanation\": ein bis zwei Sätze, warum die Lösung stimmt – mit Bezug auf den Text.\n");
        sb.Append("- Nur Inhalte verwenden, die in den Auszügen vorkommen. Sprache: Deutsch.\n");

        var existing = topic.Questions.Select(q => q.Text).Where(t => !string.IsNullOrWhiteSpace(t)).TakeLast(MaxExistingQuestionsInPrompt).ToList();
        if (existing.Count > 0)
        {
            sb.Append("Diese Fragen gibt es bereits – stelle andere Fragen:\n");
            foreach (var text in existing)
                sb.Append("- ").Append(text).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Wertet eine Fragenliste aus. Toleriert deutsche Schlüssel, Lösungen als Index (ab 0 oder ab 1),
    /// Buchstabe („B“), Antworttext oder true/false sowie Antworten als Objekte mit „richtig“-Kennzeichen.
    /// Ungültige Fragen und Wiederholungen (auch gegenüber <paramref name="existingTexts"/>) werden verworfen.
    /// </summary>
    public static List<QuizQuestion>? ParseQuestions(JsonElement element, IReadOnlyCollection<QuizQuestionType> allowedTypes, IEnumerable<string>? existingTexts = null)
    {
        var seen = new HashSet<string>((existingTexts ?? Enumerable.Empty<string>()).Select(Normalize));
        var all = new List<QuizQuestion>();

        // Hülle aus dem JSON-Modus (z. B. {"quizfragen": [...], "anzahl": 5}) auspacken
        element = LenientJson.Unwrap(element, e => IsQuestion(e) || LenientJson.IsArrayOf(e, IsQuestion));

        foreach (var item in GetObjectList(element, QuestionListKeys))
        {
            var question = ParseQuestion(item);
            if (question != null && seen.Add(Normalize(question.Text)))
                all.Add(question);
        }

        if (all.Count == 0)
            return null;

        // Erlaubte Typen bevorzugen; hält sich die KI gar nicht daran, lieber alle nehmen als nichts
        var allowed = all.Where(q => allowedTypes.Contains(q.Type)).ToList();
        return allowed.Count > 0 ? allowed : all;
    }

    private static bool IsQuestion(JsonElement element) =>
        LenientJson.HasAny(element, QuestionKeys) &&
        (LenientJson.HasAny(element, OptionsKeys) || LenientJson.HasAny(element, CorrectKeys));

    private static QuizQuestion? ParseQuestion(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var text = Clean(LenientJson.GetString(item, QuestionKeys), MaxQuestionLength);
        if (text.Length == 0)
            return null;

        var declaredType = LenientJson.GetString(item, TypeKeys).Trim().ToLowerInvariant();
        var options = new List<string>();
        var correctFromOptions = new List<int>();

        if (LenientJson.GetProperty(item, OptionsKeys) is { ValueKind: JsonValueKind.Array } optionArray)
        {
            foreach (var option in optionArray.EnumerateArray())
            {
                switch (option.ValueKind)
                {
                    case JsonValueKind.String:
                        options.Add(Clean(option.GetString(), MaxOptionLength));
                        break;
                    case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                        options.Add(option.ToString());
                        break;
                    case JsonValueKind.Object:
                        options.Add(Clean(LenientJson.GetString(option, OptionTextKeys), MaxOptionLength));
                        if (LenientJson.GetProperty(option, CorrectKeys) is { ValueKind: JsonValueKind.True })
                            correctFromOptions.Add(options.Count - 1);
                        break;
                }
            }
        }

        var isTrueFalse = declaredType is "truefalse" or "true_false" or "true/false" or "wahrfalsch" or "wahr/falsch" or "wahr_falsch" or "tf" or "boolean" or "bool"
                          || (options.Count == 2 && IsTrueWord(options[0]) != null && IsTrueWord(options[1]) != null)
                          || (options.Count == 0 && LenientJson.GetProperty(item, CorrectKeys) is { ValueKind: JsonValueKind.True or JsonValueKind.False });

        var explanation = Clean(LenientJson.GetString(item, ExplanationKeys), MaxExplanationLength);
        var correctElement = LenientJson.GetProperty(item, CorrectKeys);

        if (isTrueFalse)
            return ParseTrueFalse(text, options, correctFromOptions, correctElement, explanation);

        if (options.Any(string.IsNullOrWhiteSpace) || options.Count < 2 || options.Count > MaxOptions)
            return null;
        if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
            return null;

        var correct = correctFromOptions.Count > 0 ? correctFromOptions : ParseCorrectIndices(correctElement, options);
        if (correct.Count == 0 || correct.Count == options.Count)
            return null;

        var type = correct.Count > 1 ? QuizQuestionType.MultipleChoice : QuizQuestionType.SingleChoice;
        return new QuizQuestion { Type = type, Text = text, Options = options, CorrectIndices = correct, Explanation = explanation };
    }

    private static QuizQuestion? ParseTrueFalse(string text, List<string> options, List<int> correctFromOptions, JsonElement? correctElement, string explanation)
    {
        bool? isTrue = null;

        if (correctFromOptions.Count == 1 && options.Count == 2)
        {
            isTrue = IsTrueWord(options[correctFromOptions[0]]);
        }
        else if (correctElement is { } c)
        {
            switch (c.ValueKind)
            {
                case JsonValueKind.True:
                    isTrue = true;
                    break;
                case JsonValueKind.False:
                    isTrue = false;
                    break;
                case JsonValueKind.String:
                    isTrue = IsTrueWord(c.GetString() ?? string.Empty);
                    break;
            }

            if (isTrue == null && options.Count == 2)
            {
                var indices = ParseCorrectIndices(c, options);
                if (indices.Count == 1)
                    isTrue = IsTrueWord(options[indices[0]]);
            }
        }

        if (isTrue == null)
            return null;

        return new QuizQuestion
        {
            Type = QuizQuestionType.TrueFalse,
            Text = text,
            Options = new List<string> { "Wahr", "Falsch" },
            CorrectIndices = new List<int> { isTrue.Value ? 0 : 1 },
            Explanation = explanation
        };
    }

    /// <summary>true = „wahr“-Wort, false = „falsch“-Wort, null = keins von beiden.</summary>
    private static bool? IsTrueWord(string text)
    {
        var t = text.Trim().TrimEnd('.', '!').Trim();
        if (TrueWords.Contains(t)) return true;
        if (FalseWords.Contains(t)) return false;
        return null;
    }

    private static List<int> ParseCorrectIndices(JsonElement? value, List<string> options)
    {
        if (value == null)
            return new List<int>();

        var numbers = new List<int>();
        var texts = new List<string>();

        void Collect(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number when e.TryGetInt32(out var n):
                    numbers.Add(n);
                    break;
                case JsonValueKind.String:
                    var s = (e.GetString() ?? string.Empty).Trim();
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        numbers.Add(parsed);
                    else
                        texts.Add(s);
                    break;
                case JsonValueKind.Array:
                    foreach (var child in e.EnumerateArray())
                        Collect(child);
                    break;
            }
        }

        Collect(value.Value);

        var result = new List<int>();

        // Zahlen: ab 0 gezählt; kommt der Wert = Anzahl Antworten vor (und keine 0), war es ab 1 gezählt
        if (numbers.Count > 0)
        {
            var oneBased = !numbers.Contains(0) && numbers.Any(n => n == options.Count);
            result.AddRange(numbers.Select(n => oneBased ? n - 1 : n));
        }

        foreach (var t in texts)
        {
            // Buchstabe A–F
            if (t.Length == 1 && char.IsLetter(t[0]))
            {
                result.Add(char.ToUpperInvariant(t[0]) - 'A');
                continue;
            }

            var index = options.FindIndex(o => string.Equals(o.Trim(), t, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                result.Add(index);
        }

        return result.Distinct().Where(i => i >= 0 && i < options.Count).OrderBy(i => i).ToList();
    }

    /// <summary>Liste von Einträgen aus einem Array oder aus einem Objekt mit Listen-Eigenschaft.</summary>
    private static IEnumerable<JsonElement> GetObjectList(JsonElement element, string[] listKeys)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = element.EnumerateArray().ToList();

            // [{ "questions": [...] }] – Liste in einem Hüllobjekt
            if (items.Count == 1 && LenientJson.GetProperty(items[0], listKeys) is { ValueKind: JsonValueKind.Array } inner)
                return inner.EnumerateArray().ToList();

            return items;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (LenientJson.GetProperty(element, listKeys) is { ValueKind: JsonValueKind.Array } list)
                return list.EnumerateArray().ToList();
            return new[] { element };
        }

        return Enumerable.Empty<JsonElement>();
    }

    // ---------------- Auswahl der Textstellen ----------------

    /// <summary>
    /// Wählt die zum Thema passenden Textstellen (Stichwort-Treffer aus Name und Beschreibung) und wechselt
    /// je Paket den Ausschnitt, damit nicht immer dieselben Stellen abgefragt werden. Gibt es keine Treffer,
    /// werden gleichmäßig verteilte Textstellen genommen.
    /// </summary>
    public static List<string> SelectChunksForTopic(IReadOnlyList<string> chunks, QuizTopic topic, int batchIndex, int count = ChunksPerRequest) =>
        ChunkSelector.SelectByKeywords(chunks, $"{topic.Name} {topic.Description}", batchIndex, count);

    // ---------------- Speicherung ----------------

    public async Task<QuizLibrary> LoadLibraryAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (!File.Exists(path))
            return new QuizLibrary();

        try
        {
            await using var stream = File.OpenRead(path);
            var library = await JsonSerializer.DeserializeAsync<QuizLibrary>(stream) ?? new QuizLibrary();
            library.Topics ??= new List<QuizTopic>();
            library.History ??= new List<QuizResult>();

            foreach (var topic in library.Topics)
            {
                topic.Documents ??= new List<QuizDocumentRef>();
                topic.Questions ??= new List<QuizQuestion>();
                topic.Questions.RemoveAll(q => q == null || q.Options == null || q.CorrectIndices == null || q.CorrectIndices.Count == 0
                                               || q.CorrectIndices.Any(i => i < 0 || i >= q.Options.Count));
            }

            return library;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new QuizLibrary();
        }
    }

    public async Task SaveLibraryAsync(string profileName, QuizLibrary library)
    {
        var path = GetFilePath(profileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Erst in eine temporäre Datei schreiben, dann ersetzen – ein Abbruch soll den Fragenpool nicht zerstören
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, library, new JsonSerializerOptions { WriteIndented = true });
        }

        File.Move(temp, path, overwrite: true);
    }

    private string GetFilePath(string profileName) =>
        Path.Combine(_baseDirectory, "Quizzes", $"{JsonStorageService.ToSafeProfileFileName(profileName)}.json");

    // ---------------- Hilfen ----------------

    private static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return t.Length <= maxLength ? t : t[..(maxLength - 1)].TrimEnd() + "…";
    }

    /// <summary>Vergleichsform für Wiederholungen: klein, nur Buchstaben/Ziffern.</summary>
    public static string Normalize(string text) =>
        new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
