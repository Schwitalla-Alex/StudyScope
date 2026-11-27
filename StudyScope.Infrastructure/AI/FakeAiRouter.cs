using StudyScope.Core.Common;

namespace StudyScope.Infrastructure.AI;

/// <summary>
/// Gemeinsame Weiche der Fake-KI für <see cref="Services.LlmServiceDispatcher"/> und <see cref="FakeLlmService"/>:
/// Aufträge der Dienste erkennt sie nur an der Kennzeile (<see cref="TaskPrompt"/>), nie an Wörtern wie „JSON“,
/// „Mindmap“ oder „Quiz-Themen“ – eine Chatfrage mit solchen Wörtern bekommt immer eine Chat-Antwort. Die Antworten
/// werten den Prompt ohne Kennzeile aus (z. B. „Erstelle 5 Quizfragen“, „Format: Podcast-Gespräch“, Spalten,
/// Sprachzeile).
/// </summary>
public static class FakeAiRouter
{
    public static string Answer(string prompt, IReadOnlyList<string> chunks)
    {
        if (TaskPrompt.TryParse(prompt, out var kind, out var body))
        {
            var en = FakeLanguage.IsEnglish(body);
            return kind switch
            {
                TaskKind.Report => FakeReportResponse.Create(body, chunks),
                TaskKind.DataTable => FakeDataTableResponse.Create(body, chunks),
                TaskKind.AudioScript => FakeAudioScriptResponse.Create(body, chunks),
                TaskKind.VideoScript => FakeVideoScriptResponse.Create(body, chunks),
                TaskKind.Mindmap => FakeMindmapResponse.Create(chunks, en),
                TaskKind.Presentation => FakePresentationResponse.Create(chunks, en),
                TaskKind.QuizTopics => FakeQuizResponse.CreateTopics(chunks, en),
                TaskKind.QuizQuestions => FakeQuizResponse.CreateQuestions(body, chunks),
                TaskKind.Flashcards => FakeLanguage.Flashcards(body, chunks),
                _ => FakeLanguage.Chat(body, chunks)
            };
        }

        // Chat mit Websuche: Markierung in der Frage (die Auszüge werden nie als Auftrag gelesen)
        if (WebContextPrompt.IsWebPrompt(prompt))
            return FakeLanguage.WebChat(prompt, chunks);

        return FakeLanguage.Chat(prompt, chunks);
    }
}
