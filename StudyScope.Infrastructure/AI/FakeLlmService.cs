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

        // Bericht zuerst: eine eigene Vorgabe des Benutzers kann Wörter wie "Mindmap" enthalten
        if (FakeReportResponse.IsReportPrompt(prompt))
        {
            return Task.FromResult(FakeReportResponse.Create(prompt, chunks));
        }

        // Datentabelle direkt danach: eine eigene Vorgabe kann ebenfalls "Mindmap", "Quiz" oder "JSON" enthalten
        if (FakeDataTableResponse.IsDataTablePrompt(prompt))
        {
            return Task.FromResult(FakeDataTableResponse.Create(prompt, chunks));
        }

        // Audio-Skript nach Bericht und Datentabelle: ein Schwerpunkt kann ebenfalls "Mindmap" oder "Quiz" enthalten
        if (FakeAudioScriptResponse.IsAudioScriptPrompt(prompt))
        {
            return Task.FromResult(FakeAudioScriptResponse.Create(prompt, chunks));
        }

        // Video-Skript nach dem Audio-Skript: Folientitel können "Mindmap" oder "Quiz" enthalten, der Prompt enthält "JSON"
        if (FakeVideoScriptResponse.IsVideoScriptPrompt(prompt))
        {
            return Task.FromResult(FakeVideoScriptResponse.Create(prompt, chunks));
        }

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

        // Quiz: Themenvorschläge bzw. Fragen (ebenfalls vor dem Karteikarten-Fall)
        if (FakeQuizResponse.IsTopicPrompt(prompt))
        {
            return Task.FromResult(FakeQuizResponse.CreateTopics(chunks));
        }

        if (FakeQuizResponse.IsQuestionPrompt(prompt))
        {
            return Task.FromResult(FakeQuizResponse.CreateQuestions(prompt, chunks));
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
            return Task.FromResult($"[Testmodus] Keine Dokumentinhalte aktiv. Ihre Frage war: '{prompt}'");
        }

        var firstChunkPreview = chunks.First().Substring(0, Math.Min(50, chunks.First().Length));
        return Task.FromResult($"[Testmodus] Antwort basierend auf {chunks.Count} Textstelle(n).\nFrage: {prompt}\nAuszug: {firstChunkPreview}...");
    }
}
