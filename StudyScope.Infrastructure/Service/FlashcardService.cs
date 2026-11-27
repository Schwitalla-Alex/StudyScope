using StudyScope.Infrastructure.Resources;
using StudyScope.Core.Localization;
using System.Globalization;

using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Verwaltet die Generierung, Speicherung und den Export von Lernkarteikarten.
/// </summary>
public class FlashcardService : IFlashcardService
{
    private readonly ILlmService _llmService;

    public FlashcardService(ILlmService llmService)
    {
        _llmService = llmService;
    }

    public async Task<List<Flashcard>> GenerateFlashcardsAsync(IEnumerable<string> contextChunks, int count = 5, string? outputLanguage = null)
    {
        var chunksList = contextChunks?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
        if (!chunksList.Any())
            return new List<Flashcard>();

        // Begrenzung auf maximal 12 Abschnitte für lokale Modelle
        var limitedChunks = chunksList.Take(12).ToList();

        // Prompt mit präziser Schema-Vorgabe
        var instructionPrompt =
            $"Erstelle genau {count} Lernkarteikarten basierend auf den bereitgestellten Textauszügen.\n" +
            "Antworte AUSSCHLIESSLICH als gültiges JSON-Array von Objekten, ohne Einleitung, ohne Markdown-Formatierung und ohne Schlusssatz:\n" +
            "[{\"question\": \"Frage hier\", \"answer\": \"Antwort hier\"}]";

        var language = ResultLanguage.Resolve(outputLanguage, limitedChunks);
        instructionPrompt = ResultLanguage.ApplyToPrompt(instructionPrompt, language);

        var rawResponse = await _llmService.AskQuestionAsync(TaskPrompt.Mark(TaskKind.Flashcards, instructionPrompt), limitedChunks);

        var cards = ParseFlashcardsFromJson(rawResponse);
        foreach (var card in cards)
            card.Language = ResultLanguage.Code(language);
        return cards;
    }

    private static readonly string[] QuestionKeys = { "question", "frage", "q" };
    private static readonly string[] AnswerKeys = { "answer", "antwort", "a" };

    /// <summary>
    /// Wertet die KI-Antwort nachsichtig aus: Markdown-Codeblöcke, Begleittext, abgeschnittene Antworten
    /// und Hüllen aus dem JSON-Modus ({"items": [...]}, {"karteikarten": [...], "anzahl": 5}, eine einzelne Karte).
    /// </summary>
    public static List<Flashcard> ParseFlashcardsFromJson(string? rawResponse) =>
        LenientJson.Analyze(rawResponse, BuildCards).Value ?? new List<Flashcard>();

    private static bool IsCard(JsonElement element) =>
        LenientJson.HasAny(element, QuestionKeys) && LenientJson.HasAny(element, AnswerKeys);

    private static List<Flashcard>? BuildCards(JsonElement element)
    {
        element = LenientJson.Unwrap(element, e => IsCard(e) || LenientJson.IsArrayOf(e, IsCard));

        IEnumerable<JsonElement> items = element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray(),
            JsonValueKind.Object => new[] { element },
            _ => Enumerable.Empty<JsonElement>()
        };

        var list = new List<Flashcard>();
        foreach (var item in items)
        {
            var question = LenientJson.GetString(item, QuestionKeys).Trim();
            var answer = LenientJson.GetString(item, AnswerKeys).Trim();
            if (question.Length > 0 && answer.Length > 0)
                list.Add(new Flashcard { Question = question, Answer = answer, Status = FlashcardStatus.New });
        }

        return list.Count > 0 ? list : null;
    }

    public Task<List<Flashcard>> LoadFlashcardsAsync(string profileName)
    {
        return Task.Run(() =>
        {
            var filePath = GetFlashcardsFilePath(profileName);
            if (!File.Exists(filePath))
                return new List<Flashcard>();

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<Flashcard>>(json) ?? new List<Flashcard>();
            }
            catch
            {
                return new List<Flashcard>();
            }
        });
    }

    public Task SaveFlashcardsAsync(string profileName, IEnumerable<Flashcard> flashcards)
    {
        return Task.Run(() =>
        {
            var baseDir = Path.Combine(AppPaths.UserDataDirectory, "Flashcards");

            Directory.CreateDirectory(baseDir);

            var filePath = Path.Combine(baseDir, $"{profileName}.json");
            var json = JsonSerializer.Serialize(flashcards, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        });
    }

    private static string GetFlashcardsFilePath(string profileName)
    {
        return Path.Combine(AppPaths.UserDataDirectory, "Flashcards", $"{profileName}.json");
    }

    /// <summary>Bezeichnung des Lernstands in der Sprache des Exports – wie in der Oberfläche (Neu, Gelernt, Wiederholen).</summary>
    public static string StatusDisplayName(FlashcardStatus status) => ExportNames.FlashcardStatus(status);

    public string ExportToMarkdown(string profileName, IEnumerable<Flashcard> flashcards, CultureInfo culture)
    {
        using var scope = CultureScope.Use(culture);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {Loc.Format(ExportStrings.Flashcards_Title, ("profile", profileName))}");
        sb.AppendLine($"*{Loc.Format(ExportStrings.Flashcards_CreatedAt, ("date", ExportNames.DateTime(DateTime.Now)))}*");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        int index = 1;
        foreach (var card in flashcards)
        {
            sb.AppendLine($"### {Loc.Format(ExportStrings.Flashcards_QuestionHeading, ("number", index), ("question", card.Question))}");
            sb.AppendLine();
            sb.AppendLine($"**{ExportStrings.Flashcards_AnswerLabel}** {card.Answer}");
            sb.AppendLine();
            sb.AppendLine($"*{ExportStrings.Flashcards_StatusLabel}* {StatusDisplayName(card.Status)}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            index++;
        }

        return sb.ToString();
    }
}
