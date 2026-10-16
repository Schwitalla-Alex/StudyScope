
using System.Text.Json;
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

    public async Task<List<Flashcard>> GenerateFlashcardsAsync(IEnumerable<string> contextChunks, int count = 5)
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

        var rawResponse = await _llmService.AskQuestionAsync(instructionPrompt, limitedChunks);

        return ParseFlashcardsFromJson(rawResponse);
    }

    private static List<Flashcard> ParseFlashcardsFromJson(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return new List<Flashcard>();

        var cleaned = rawResponse.Trim();

        // 1. Markdown-Codeblöcke bereinigen (```json ... ``` oder ``` ... ```)
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            if (firstNewline != -1)
            {
                cleaned = cleaned.Substring(firstNewline + 1);
            }

            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.LastIndexOf("```")).Trim();
            }
        }

        // 2. Den Bereich zwischen der ersten '[' und der letzten ']' isolieren
        var firstBracket = cleaned.IndexOf('[');
        var lastBracket = cleaned.LastIndexOf(']');

        if (firstBracket != -1 && lastBracket != -1 && lastBracket >= firstBracket)
        {
            cleaned = cleaned.Substring(firstBracket, lastBracket - firstBracket + 1);
        }
        else
        {
            return new List<Flashcard>();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var list = new List<Flashcard>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string q = string.Empty;
                    string a = string.Empty;

                    foreach (var prop in element.EnumerateObject())
                    {
                        // Groß- und Kleinschreibung unempfindlich abgleichen:
                        if (prop.Name.Equals("question", StringComparison.OrdinalIgnoreCase) ||
                            prop.Name.Equals("frage", StringComparison.OrdinalIgnoreCase))
                        {
                            q = prop.Value.GetString() ?? string.Empty;
                        }
                        else if (prop.Name.Equals("answer", StringComparison.OrdinalIgnoreCase) ||
                                 prop.Name.Equals("antwort", StringComparison.OrdinalIgnoreCase))
                        {
                            a = prop.Value.GetString() ?? string.Empty;
                        }
                    }

                    //foreach (var prop in element.EnumerateObject())
                    //{
                    //    if (prop.NameEquals("question") || prop.Name.Equals("frage", StringComparison.OrdinalIgnoreCase))
                    //    {
                    //        q = prop.Value.GetString() ?? string.Empty;
                    //    }
                    //    else if (prop.NameEquals("answer") || prop.Name.Equals("antwort", StringComparison.OrdinalIgnoreCase))
                    //    {
                    //        a = prop.Value.GetString() ?? string.Empty;
                    //    }
                    //}

                    if (!string.IsNullOrWhiteSpace(q) && !string.IsNullOrWhiteSpace(a))
                    {
                        list.Add(new Flashcard
                        {
                            Question = q.Trim(),
                            Answer = a.Trim(),
                            Status = FlashcardStatus.New
                        });
                    }
                }
            }

            return list;
        }
        catch
        {
            return new List<Flashcard>();
        }
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
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StudyScope",
                "Flashcards");

            Directory.CreateDirectory(baseDir);

            var filePath = Path.Combine(baseDir, $"{profileName}.json");
            var json = JsonSerializer.Serialize(flashcards, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        });
    }

    private static string GetFlashcardsFilePath(string profileName)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StudyScope",
            "Flashcards",
            $"{profileName}.json");
    }

    public string ExportToMarkdown(string profileName, IEnumerable<Flashcard> flashcards)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Lernkarteikarten: {profileName}");
        sb.AppendLine($"*Erstellt am: {DateTime.Now:dd.MM.yyyy HH:mm}*");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        int index = 1;
        foreach (var card in flashcards)
        {
            sb.AppendLine($"### Frage {index}: {card.Question}");
            sb.AppendLine();
            sb.AppendLine($"**Antwort:** {card.Answer}");
            sb.AppendLine();
            sb.AppendLine($"*Status:* {card.Status}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            index++;
        }

        return sb.ToString();
    }
}