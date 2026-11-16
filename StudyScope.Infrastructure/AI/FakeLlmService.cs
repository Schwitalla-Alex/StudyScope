using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.AI;

public class FakeLlmService : ILlmService
{
    public Task<bool> ValidateConnectionAsync(AiConfig? config = null)
    {
        return Task.FromResult(true);
    }

    public Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
    {
        var chunks = contextChunks?.ToList() ?? new List<string>();

        // Mindmap angefordert (vor dem Karteikarten-Fall prüfen, da der Prompt ebenfalls "JSON" enthält)
        if (FakeMindmapResponse.IsMindmapPrompt(prompt))
        {
            return Task.FromResult(FakeMindmapResponse.Create(chunks));
        }

        // Präsentation angefordert (ebenfalls vor dem Karteikarten-Fall)
        if (FakePresentationResponse.IsPresentationPrompt(prompt))
        {
            return Task.FromResult(FakePresentationResponse.Create(chunks));
        }

        // 1. Fall: Generierung von Karteikarten angefordert -> Valides JSON zurückgeben
        if (prompt.Contains("Lernkarteikarten", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = chunks.FirstOrDefault() ?? "Beispielinhalt";
            var preview = snippet.Substring(0, Math.Min(40, snippet.Length));

            var json = $$"""
        [
          {
            "question": "Was besagt der Textauszug?",
            "answer": "Der Auszug lautet auszugsweise: '{{preview}}...'"
          },
          {
            "question": "Wie viele Textstellen wurden übergeben?",
            "answer": "Es wurden genau {{chunks.Count}} Textstelle(n) analysiert."
          }
        ]
        """;
            return Task.FromResult(json);
        }

        // 2. Fall: Normaler Chatverlauf
        if (!chunks.Any())
        {
            return Task.FromResult($"[Testmodus] Keine Dokumentinhalte aktiv. Deine Frage war: '{prompt}'");
        }

        var firstChunkPreview = chunks.First().Substring(0, Math.Min(50, chunks.First().Length));
        return Task.FromResult($"[Testmodus] Antwort basierend auf {chunks.Count} Textstelle(n).\nFrage: {prompt}\nAuszug: {firstChunkPreview}...");
    }
}
