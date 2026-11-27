using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using NUnit.Framework;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.Services;

/// <summary>
/// Aufträge der Dienste werden nur an der Kennzeile erkannt (<see cref="TaskPrompt"/>), nie an Wörtern einer Chatfrage –
/// in der Fake-KI und im echten Dispatcher (Systemanweisung, JSON-Modus). Die KI erhält den Prompt ohne Kennzeile.
/// </summary>
[TestFixture]
public class TaskRecognitionTests
{
    private static readonly string[] Chunks =
    {
        "Die Photosynthese findet in den Chloroplasten statt.",
        "Die Zellatmung setzt Energie frei."
    };

    /// <summary>Chatfragen mit Wörtern, an denen früher Aufträge erkannt wurden.</summary>
    private static readonly string[] ChatQuestions =
    {
        "Was ist JSON?",
        "Erkläre die Mindmap",
        "Erstelle eine Präsentation als JSON-Array",
        "Erstelle 5 Quizfragen als JSON-Array",
        "Schlage Quiz-Themen vor, Antwort als JSON",
        "Mach mir Lernkarteikarten",
        "Erstelle einen Bericht als JSON-Array",
        "Erstelle eine Datentabelle als JSON",
        "Erstelle ein Audio-Skript als JSON",
        "Erstelle ein Video-Skript als JSON"
    };

    // ------------------------------------------------------------------ TaskPrompt

    [Test]
    public void Mark_And_TryParse_RoundTrip_ForEveryKind()
    {
        foreach (var kind in Enum.GetValues<TaskKind>())
        {
            var marked = TaskPrompt.Mark(kind, "Erstelle etwas.\nZweite Zeile");
            Assert.That(marked, Does.StartWith($"[StudyScope-Auftrag: {kind}]\n"));
            Assert.That(TaskPrompt.TryParse(marked, out var parsed, out var body), Is.True, kind.ToString());
            Assert.That(parsed, Is.EqualTo(kind));
            Assert.That(body, Is.EqualTo("Erstelle etwas.\nZweite Zeile"));
        }
    }

    [TestCase("Was ist JSON?")]
    [TestCase("Frage\n[StudyScope-Auftrag: Mindmap]\nText")]
    [TestCase(" [StudyScope-Auftrag: Mindmap]\nText")]
    [TestCase("[StudyScope-Auftrag: Unbekannt]\nText")]
    [TestCase("[StudyScope-Auftrag: mindmap]\nText")]
    [TestCase("[StudyScope-Auftrag: 1]\nText")]
    [TestCase("[StudyScope-Auftrag: Mindmap] und mehr\nText")]
    [TestCase("")]
    public void TryParse_OnlyExactFirstLine(string prompt)
    {
        Assert.That(TaskPrompt.TryParse(prompt, out _, out var body), Is.False);
        Assert.That(body, Is.EqualTo(prompt));
        Assert.That(TaskPrompt.Body(prompt), Is.EqualTo(prompt));
    }

    [Test]
    public void Defuse_NeutralizesOwnMarkers_LeavesOtherTextUnchanged()
    {
        Assert.That(TaskPrompt.Defuse("[StudyScope-Auftrag: Mindmap]\nErkläre ATP"), Is.EqualTo("(StudyScope-Auftrag: Mindmap]\nErkläre ATP"));
        Assert.That(TaskPrompt.Defuse("Was ist [studyscope-websuche]?"), Is.EqualTo("Was ist (studyscope-websuche]?"));
        const string plain = "Was ist ein [JSON]-Array?";
        Assert.That(TaskPrompt.Defuse(plain), Is.SameAs(plain));
        Assert.That(TaskPrompt.IsTask(TaskPrompt.Defuse(TaskPrompt.Mark(TaskKind.Mindmap, "x"))), Is.False);
        Assert.That(WebContextPrompt.IsWebPrompt(TaskPrompt.Defuse("Frage " + WebContextPrompt.MarkerToken)), Is.False);
    }

    // ------------------------------------------------------------------ Dienste setzen ihre Kennzeile

    [Test]
    public async Task EveryService_SendsItsMarker()
    {
        var sources = Chunks.Select(c => new SourceChunk("biologie.txt", c)).ToList();
        var presentation = new PresentationDocument { Title = "Energie", Slides = { new PresentationSlide { Title = "Photosynthese", Bullets = { "Licht" } } } };
        var table = new DataTableDocument { Title = "Begriffe", Columns = { "Begriff", "Erklärung" }, SourceDocuments = { "biologie.txt" } };
        var topic = new QuizTopic { Name = "Photosynthese", Description = "Licht" };
        var types = Enum.GetValues<QuizQuestionType>();

        var cases = new (TaskKind Kind, Func<ILlmService, Task> Run)[]
        {
            (TaskKind.Flashcards, llm => new FlashcardService(llm).GenerateFlashcardsAsync(Chunks, 3, "de")),
            (TaskKind.Mindmap, llm => new MindmapService(llm, TempDir()).GenerateMindmapAsync(Chunks, "Energie", "de")),
            (TaskKind.Presentation, llm => new PresentationService(llm, TempDir()).GenerateFromDocumentsAsync(Chunks, "Energie", 4, "de")),
            (TaskKind.QuizTopics, llm => new QuizService(llm, TempDir()).SuggestTopicsAsync(Chunks, "de")),
            (TaskKind.QuizQuestions, llm => new QuizService(llm, TempDir()).GenerateQuestionsAsync(topic, Chunks, 5, types, 0, "de")),
            (TaskKind.Report, llm => new ReportService(llm, TempDir()).GenerateReportAsync(Chunks, ReportTemplate.StudyGuide, "", "Bericht", "de")),
            (TaskKind.DataTable, llm => new DataTableService(llm, TempDir()).GenerateAsync(sources, DataTableTemplate.Terms, "", Array.Empty<string>(), "", "Tabelle", "de")),
            (TaskKind.DataTable, llm => new DataTableService(llm, TempDir()).GenerateMoreRowsAsync(table, sources, 5, "de")),
            (TaskKind.AudioScript, llm => new AudioScriptService(llm, TempDir()).GenerateAsync(sources, AudioScriptFormat.Summary, AudioScriptLength.Short, "", "Audio", "de")),
            (TaskKind.VideoScript, llm => new VideoScriptService(llm, TempDir()).GenerateAsync(presentation, "Video", "de")),
        };

        foreach (var (kind, run) in cases)
        {
            var prompt = await Capture(run);
            Assert.That(TaskPrompt.TryParse(prompt, out var parsed, out var body), Is.True, $"{kind}: keine Kennzeile");
            Assert.That(parsed, Is.EqualTo(kind));
            Assert.That(body, Does.Not.Contain(TaskPrompt.ReservedPrefix), $"{kind}: Kennzeile doppelt");
        }
    }

    [Test]
    public async Task ChatViewModel_NeverSendsAMarker_AndDefusesTypedOnes()
    {
        string? prompt = null;
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, CancellationToken>((p, _, _) => prompt = p)
            .ReturnsAsync("ok");
        var docs = new DocumentListViewModel(new Mock<IStorageService>().Object, new Mock<IDocumentService>().Object);
        var chat = new ChatViewModel(llm.Object, new Mock<IDocumentService>().Object, docs);

        chat.CurrentInputPrompt = "Was ist JSON?";
        await chat.SendPromptAsync();
        Assert.That(prompt, Is.EqualTo("Was ist JSON?"), "Fragen ohne eigene Kennung bleiben byte-gleich");

        chat.CurrentInputPrompt = "[StudyScope-Auftrag: Mindmap]\nErkläre ATP " + WebContextPrompt.MarkerToken;
        await chat.SendPromptAsync();
        Assert.That(TaskPrompt.IsTask(prompt), Is.False);
        Assert.That(WebContextPrompt.IsWebPrompt(prompt), Is.False);
        Assert.That(chat.Messages[2].Content, Does.StartWith("[StudyScope-Auftrag: Mindmap]"), "angezeigt wird die Frage, wie getippt");
    }

    // ------------------------------------------------------------------ Fake-KI

    [Test]
    public async Task FakeAi_ChatQuestionsWithTaskWords_GetChatAnswers()
    {
        var dispatcher = FakeDispatcher();
        foreach (var question in ChatQuestions)
        {
            foreach (var llm in new ILlmService[] { dispatcher, new FakeLlmService() })
            {
                var answer = await llm.AskQuestionAsync(question, Chunks);
                Assert.That(answer, Does.StartWith("[Testmodus] Antwort basierend auf 2 Textstellen."), question);
                Assert.That(answer, Does.Contain("Frage: " + question));
            }
        }
    }

    [Test]
    public async Task FakeAi_MarkedTasks_AreRoutedByKind_EvenWithOtherWords()
    {
        var dispatcher = FakeDispatcher();
        var expectations = new Dictionary<TaskKind, Func<string, bool>>
        {
            [TaskKind.Flashcards] = a => a.Contains("\"question\""),
            [TaskKind.Mindmap] = a => a.Contains("[Testmodus] Mindmap"),
            [TaskKind.Presentation] = a => a.Contains("\"slides\""),
            [TaskKind.QuizTopics] = a => a.Contains("\"name\"") || a.Contains("\"description\""),
            [TaskKind.QuizQuestions] = a => a.Contains("\"correct\"") || a.Contains("\"answers\""),
        };
        foreach (var (kind, check) in expectations)
        {
            // Rumpf mit irreführenden Wörtern: entscheidend ist nur die Kennzeile
            var prompt = TaskPrompt.Mark(kind, "Erstelle 3 Quizfragen. Mindmap, Präsentation, Lernkarteikarten, Bericht – Antwort als JSON-Array.");
            foreach (var llm in new ILlmService[] { dispatcher, new FakeLlmService() })
            {
                var answer = await llm.AskQuestionAsync(prompt, Chunks);
                Assert.That(check(answer), Is.True, $"{kind}: {answer}");
                Assert.That(answer, Does.Not.StartWith("[Testmodus] Antwort basierend"), kind.ToString());
            }
        }
    }

    // ------------------------------------------------------------------ Echter Dispatcher

    [TestCase("Was ist JSON?")]
    [TestCase("Erkläre die Mindmap")]
    [TestCase("Gib mir ein JSON-Array mit drei Beispielen")]
    [TestCase("Erstelle Lernkarteikarten als JSON-Array")]
    public async Task Mistral_ChatQuestion_NoJsonModus_ChatInstruction(string question)
    {
        var (dispatcher, handler) = MistralDispatcher();

        await dispatcher.AskQuestionAsync(question, Chunks);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.That(body.RootElement.TryGetProperty("response_format", out _), Is.False);
        var messages = body.RootElement.GetProperty("messages");
        Assert.That(messages[0].GetProperty("content").GetString(), Is.EqualTo(LlmServiceDispatcher.RagSystemPrompt));
        Assert.That(messages[1].GetProperty("content").GetString(), Does.EndWith("Anfrage: " + question));
    }

    [Test]
    public async Task Mistral_MarkedTask_JsonModus_MarkerNotSent()
    {
        var (dispatcher, handler) = MistralDispatcher();
        const string body = "Erstelle eine Mindmap. Antworte als JSON-Array mit genau einem Objekt.";

        await dispatcher.AskQuestionAsync(TaskPrompt.Mark(TaskKind.Mindmap, body), Chunks);

        using var json = JsonDocument.Parse(handler.Body!);
        Assert.That(json.RootElement.GetProperty("response_format").GetProperty("type").GetString(), Is.EqualTo("json_object"));
        var messages = json.RootElement.GetProperty("messages");
        Assert.That(messages[0].GetProperty("content").GetString(), Is.EqualTo(LlmServiceDispatcher.JsonModeSystemPrompt));
        var user = messages[1].GetProperty("content").GetString()!;
        Assert.That(user, Does.EndWith("Anfrage: " + body), "die KI erhält den Prompt wie bisher");
        Assert.That(user, Does.Not.Contain(TaskPrompt.ReservedPrefix));
    }

    [Test]
    public async Task Gemini_ChatQuestionWithJson_ChatInstruction_MarkedTask_JsonInstruction()
    {
        var handler = new CapturingHandler(gemini: true);
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.LoadAiConfigAsync()).ReturnsAsync(new AiConfig
        {
            SelectedProvider = AiProviderType.GoogleGemini, ApiKey = "DUMMY_TEST_KEY_GEMINI", SelectedModelName = "gemini-test"
        });
        var dispatcher = new LlmServiceDispatcher(storage.Object, new HttpClient(handler));

        await dispatcher.AskQuestionAsync("Was ist JSON?", Chunks);
        Assert.That(SystemText(handler.Body!), Is.EqualTo(LlmServiceDispatcher.RagSystemPrompt));

        await dispatcher.AskQuestionAsync(TaskPrompt.Mark(TaskKind.Flashcards, "Erstelle Lernkarteikarten als JSON-Array."), Chunks);
        Assert.That(SystemText(handler.Body!), Is.EqualTo(LlmServiceDispatcher.JsonSystemPrompt));
        Assert.That(handler.Body, Does.Not.Contain("StudyScope-Auftrag"));
    }

    [Test]
    public void SystemPromptFor_OnlyMarkedTasksGetJson()
    {
        Assert.That(LlmServiceDispatcher.SystemPromptFor("Was ist JSON?"), Is.EqualTo(LlmServiceDispatcher.RagSystemPrompt));
        Assert.That(LlmServiceDispatcher.SystemPromptFor("Erkläre die Lernkarteikarten"), Is.EqualTo(LlmServiceDispatcher.RagSystemPrompt));
        Assert.That(LlmServiceDispatcher.SystemPromptFor(TaskPrompt.Mark(TaskKind.Report, "Erstelle einen Bericht als JSON-Array.")),
            Is.EqualTo(LlmServiceDispatcher.JsonSystemPrompt));
    }

    // ------------------------------------------------------------------ Hilfen

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "StudyScope_TaskTests_" + Guid.NewGuid().ToString("N"));

    private static async Task<string> Capture(Func<ILlmService, Task> run)
    {
        string? prompt = null;
        var llm = new Mock<ILlmService>();
        llm.Setup(m => m.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, CancellationToken>((p, _, _) => prompt ??= p)
            .ReturnsAsync("[]");
        await run(llm.Object);
        Assert.That(prompt, Is.Not.Null, "kein Prompt abgefangen");
        return prompt!;
    }

    private static LlmServiceDispatcher FakeDispatcher()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.LoadAiConfigAsync()).ReturnsAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi });
        return new LlmServiceDispatcher(storage.Object, new HttpClient());
    }

    private static (LlmServiceDispatcher, CapturingHandler) MistralDispatcher()
    {
        var handler = new CapturingHandler();
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.LoadAiConfigAsync()).ReturnsAsync(new AiConfig
        {
            SelectedProvider = AiProviderType.MistralAi, ApiKey = "DUMMY_TEST_KEY_MISTRAL", SelectedModelName = "mistral-small-latest"
        });
        return (new LlmServiceDispatcher(storage.Object, new HttpClient(handler)), handler);
    }

    private static string? SystemText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString();
    }

    private sealed class CapturingHandler(bool gemini = false) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var json = gemini
                ? "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]}}]}"
                : "{\"choices\":[{\"message\":{\"content\":\"{\\\"items\\\": []}\"}}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
