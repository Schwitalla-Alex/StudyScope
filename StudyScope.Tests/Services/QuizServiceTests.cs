using System.Text.Json;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Tests für den QuizService: Auswertung der KI-Antworten (alle Fragetypen, verschiedene Lösungsformate),
/// Themenvorschläge, Auswahl der Textstellen, Prompt und Speicherung.
/// </summary>
[TestFixture]
public class QuizServiceTests
{
    private static readonly QuizQuestionType[] AllTypes =
        { QuizQuestionType.SingleChoice, QuizQuestionType.TrueFalse, QuizQuestionType.MultipleChoice };

    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeQuizTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static List<QuizQuestion>? Parse(string json, IReadOnlyCollection<QuizQuestionType>? types = null, IEnumerable<string>? existing = null)
    {
        using var doc = JsonDocument.Parse(json);
        return QuizService.ParseQuestions(doc.RootElement, types ?? AllTypes, existing);
    }

    // ---------- Fragetypen ----------

    [Test]
    public void ParseQuestions_AllThreeTypes()
    {
        const string json = """
            [
              {"type": "single", "question": "Was ist die kleinste Einheit des Lebens?", "options": ["Atom", "Zelle", "Organ", "Gewebe"], "correct": [1], "explanation": "Laut Text die Zelle."},
              {"type": "truefalse", "question": "Mitochondrien bilden Proteine.", "options": ["Wahr", "Falsch"], "correct": [1]},
              {"type": "multiple", "question": "Welche sind Organellen?", "options": ["Ribosom", "Herz", "Mitochondrium", "Leber"], "correct": [0, 2]}
            ]
            """;

        var questions = Parse(json)!;

        Assert.That(questions, Has.Count.EqualTo(3));
        Assert.That(questions[0].Type, Is.EqualTo(QuizQuestionType.SingleChoice));
        Assert.That(questions[0].CorrectIndices, Is.EqualTo(new[] { 1 }));
        Assert.That(questions[0].Explanation, Is.EqualTo("Laut Text die Zelle."));
        Assert.That(questions[1].Type, Is.EqualTo(QuizQuestionType.TrueFalse));
        Assert.That(questions[1].Options, Is.EqualTo(new[] { "Wahr", "Falsch" }));
        Assert.That(questions[1].CorrectIndices, Is.EqualTo(new[] { 1 }));
        Assert.That(questions[2].Type, Is.EqualTo(QuizQuestionType.MultipleChoice));
        Assert.That(questions[2].CorrectIndices, Is.EqualTo(new[] { 0, 2 }));
    }

    [Test]
    public void ParseQuestions_TypeFollowsNumberOfCorrectAnswers()
    {
        // als "single" angekündigt, aber zwei richtige → Mehrfachauswahl; als "multiple" mit einer richtigen → Einfachauswahl
        const string json = """
            [
              {"type": "single", "question": "F1?", "options": ["a", "b", "c", "d"], "correct": [0, 1]},
              {"type": "multiple", "question": "F2?", "options": ["a", "b", "c", "d"], "correct": [3]}
            ]
            """;

        var questions = Parse(json)!;

        Assert.That(questions[0].Type, Is.EqualTo(QuizQuestionType.MultipleChoice));
        Assert.That(questions[1].Type, Is.EqualTo(QuizQuestionType.SingleChoice));
    }

    // ---------- Lösungsformate ----------

    [TestCase("\"correct\": 2", 2)]
    [TestCase("\"correct\": \"C\"", 2)]
    [TestCase("\"correct\": \"Organ\"", 2)]
    [TestCase("\"richtig\": [\"2\"]", 2)]
    [TestCase("\"correct\": 4", 3)]                 // ab 1 gezählt (4 = letzte von 4 Antworten)
    [TestCase("\"correctIndex\": 0", 0)]
    public void ParseQuestions_DifferentCorrectFormats(string correctJson, int expectedIndex)
    {
        var json = $$"""[{"question": "Frage?", "options": ["Atom", "Zelle", "Organ", "Gewebe"], {{correctJson}}}]""";

        var questions = Parse(json)!;

        Assert.That(questions[0].CorrectIndices, Is.EqualTo(new[] { expectedIndex }));
    }

    [Test]
    public void ParseQuestions_OptionsAsObjectsWithCorrectFlag()
    {
        const string json = """
            [{"frage": "Welche stimmen?", "antworten": [{"text": "A", "richtig": true}, {"text": "B", "richtig": false}, {"text": "C", "richtig": true}], "erklärung": "Weil."}]
            """;

        var question = Parse(json)!.Single();

        Assert.That(question.Type, Is.EqualTo(QuizQuestionType.MultipleChoice));
        Assert.That(question.CorrectIndices, Is.EqualTo(new[] { 0, 2 }));
        Assert.That(question.Explanation, Is.EqualTo("Weil."));
    }

    [TestCase("\"correct\": true", 0)]
    [TestCase("\"correct\": false", 1)]
    [TestCase("\"correct\": \"falsch\"", 1)]
    [TestCase("\"options\": [\"Falsch\", \"Wahr\"], \"correct\": [1]", 0)]   // andere Reihenfolge wird normalisiert
    [TestCase("\"options\": [\"True\", \"False\"], \"correct\": [0]", 0)]
    public void ParseQuestions_TrueFalseVariants(string body, int expectedIndex)
    {
        var json = $$"""[{"type": "truefalse", "question": "Aussage.", {{body}}}]""";

        var question = Parse(json)!.Single();

        Assert.That(question.Type, Is.EqualTo(QuizQuestionType.TrueFalse));
        Assert.That(question.Options, Is.EqualTo(new[] { "Wahr", "Falsch" }));
        Assert.That(question.CorrectIndices, Is.EqualTo(new[] { expectedIndex }));
    }

    // ---------- Ungültiges und Wiederholungen ----------

    [Test]
    public void ParseQuestions_InvalidQuestionsAreDropped()
    {
        const string json = """
            [
              {"question": "", "options": ["a", "b"], "correct": [0]},
              {"question": "Ohne Lösung?", "options": ["a", "b", "c"]},
              {"question": "Alles richtig?", "options": ["a", "b"], "correct": [0, 1]},
              {"question": "Index außerhalb?", "options": ["a", "b", "c"], "correct": [7]},
              {"question": "Doppelte Antworten?", "options": ["a", "a", "b"], "correct": [2]},
              {"question": "Gültig?", "options": ["a", "b", "c"], "correct": [1]}
            ]
            """;

        var questions = Parse(json)!;

        Assert.That(questions.Select(q => q.Text), Is.EqualTo(new[] { "Gültig?" }));
    }

    [Test]
    public void ParseQuestions_DuplicatesAndExistingQuestionsAreRemoved()
    {
        const string json = """
            [
              {"question": "Was ist ATP?", "options": ["a", "b", "c"], "correct": [0]},
              {"question": "was ist ATP", "options": ["x", "y", "z"], "correct": [1]},
              {"question": "Wo liegt die DNA?", "options": ["a", "b", "c"], "correct": [2]}
            ]
            """;

        var questions = Parse(json, existing: new[] { "Wo liegt die DNA?" })!;

        Assert.That(questions.Select(q => q.Text), Is.EqualTo(new[] { "Was ist ATP?" }));
    }

    [Test]
    public void ParseQuestions_PrefersAllowedTypes_ButKeepsOthersIfNothingFits()
    {
        const string json = """
            [
              {"type": "truefalse", "question": "Aussage A.", "correct": true},
              {"question": "Frage B?", "options": ["a", "b", "c"], "correct": [0]}
            ]
            """;

        Assert.That(Parse(json, new[] { QuizQuestionType.TrueFalse })!.Select(q => q.Text), Is.EqualTo(new[] { "Aussage A." }));
        Assert.That(Parse(json, new[] { QuizQuestionType.MultipleChoice })!, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseQuestions_ListInsideWrapperObject()
    {
        const string json = """{"fragen": [{"question": "F?", "options": ["a", "b", "c"], "correct": [0]}]}""";

        Assert.That(Parse(json)!, Has.Count.EqualTo(1));
    }

    // ---------- Erzeugung über den Service ----------

    private static QuizTopic Topic(string name = "Zellbiologie", string description = "Aufbau der Zelle") =>
        new() { Name = name, Description = description };

    [Test]
    public async Task GenerateQuestionsAsync_TruncatedResponse_KeepsCompleteQuestions()
    {
        var llm = new FixedLlm("[{\"question\": \"Erste?\", \"options\": [\"a\", \"b\", \"c\"], \"correct\": [0]}, {\"question\": \"Zweite?\", \"options\": [\"a\", \"b");
        var service = new QuizService(llm, _tempDir);

        var result = await service.GenerateQuestionsAsync(Topic(), new[] { "Text über Zellen." }, 5, AllTypes, 0);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.WasRepaired, Is.True);
        Assert.That(result.Questions.Single().Text, Is.EqualTo("Erste?"));
    }

    [Test]
    public async Task GenerateQuestionsAsync_ServiceError_IsClassified()
    {
        var service = new QuizService(new FixedLlm("Gemini-Fehler: Es ist kein API-Schlüssel hinterlegt."), _tempDir);

        var result = await service.GenerateQuestionsAsync(Topic(), new[] { "Text" }, 5, AllTypes, 0);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.NoJson));
    }

    [Test]
    public async Task GenerateQuestionsAsync_WithoutChunks_DoesNotCallLlm()
    {
        var llm = new FixedLlm("[]");
        var service = new QuizService(llm, _tempDir);

        var result = await service.GenerateQuestionsAsync(Topic(), Array.Empty<string>(), 5, AllTypes, 0);

        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.NoContent));
        Assert.That(llm.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GenerateQuestionsAsync_CountIsLimitedToBatchSize()
    {
        var llm = new FixedLlm("[]");
        var service = new QuizService(llm, _tempDir, batchSize: 10);

        await service.GenerateQuestionsAsync(Topic(), new[] { "Text" }, 50, AllTypes, 0);

        Assert.That(llm.LastPrompt, Does.StartWith("[StudyScope-Auftrag: QuizQuestions]\nErstelle 10 Quizfragen"));
    }

    [Test]
    public void BuildQuestionPrompt_ContainsOnlyAllowedTypesAndExistingQuestions()
    {
        var topic = Topic();
        topic.Questions.Add(new QuizQuestion { Text = "Was ist ATP?" });

        var prompt = QuizService.BuildQuestionPrompt(topic, 7, new[] { QuizQuestionType.TrueFalse });

        Assert.That(prompt, Does.StartWith("Erstelle 7 Quizfragen zum Thema „Zellbiologie“ (Aufbau der Zelle)"));
        Assert.That(prompt, Does.Contain("\"truefalse\""));
        Assert.That(prompt, Does.Not.Contain("\"single\""));
        Assert.That(prompt, Does.Not.Contain("\"multiple\""));
        Assert.That(prompt, Does.Contain("- Was ist ATP?"));
        Assert.That(prompt, Does.Contain("JSON"));
        Assert.That(prompt, Does.Not.Contain("Mindmap"));
        Assert.That(prompt, Does.Not.Contain("Präsentation"));
    }

    [Test]
    public async Task FakeLlm_DeliversValidQuestionsOfAllTypes()
    {
        var service = new QuizService(new FakeLlmService(), _tempDir);
        var chunks = new[] { "Mitochondrien produzieren Energie. Ribosomen synthetisieren Proteine in der Zelle." };

        var result = await service.GenerateQuestionsAsync(Topic(), chunks, 9, AllTypes, 0);

        Assert.That(result.Questions, Has.Count.EqualTo(9));
        Assert.That(result.Questions.Select(q => q.Type).Distinct().Count(), Is.EqualTo(3));
    }

    // ---------- Themenvorschläge ----------

    [Test]
    public async Task SuggestTopicsAsync_ParsesAndRemovesDuplicates()
    {
        var llm = new FixedLlm("""
            ```json
            [{"name": "Zellaufbau", "description": "Bestandteile"}, {"name": "zellaufbau"}, {"thema": "Genetik", "beschreibung": "DNA"}, "Stoffwechsel"]
            ```
            """);
        var service = new QuizService(llm, _tempDir);

        var result = await service.SuggestTopicsAsync(new[] { "Text" });

        Assert.That(result.Topics.Select(t => t.Name), Is.EqualTo(new[] { "Zellaufbau", "Genetik", "Stoffwechsel" }));
        Assert.That(result.Topics[1].Description, Is.EqualTo("DNA"));
        Assert.That(llm.LastPrompt, Does.Contain("Quiz-Themen"));
    }

    [Test]
    public async Task SuggestTopicsAsync_WithoutChunks_ReportsNoContent()
    {
        var result = await new QuizService(new FixedLlm(""), _tempDir).SuggestTopicsAsync(Array.Empty<string>());

        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.NoContent));
    }

    // ---------- Auswahl der Textstellen ----------

    [Test]
    public void SelectChunksForTopic_PrefersMatchingChunksAndRotatesPerBatch()
    {
        var chunks = Enumerable.Range(0, 40).Select(i => i % 4 == 0 ? $"Abschnitt {i}: Mitochondrien und Zellatmung" : $"Abschnitt {i}: anderes Thema").ToList();
        var topic = Topic("Mitochondrien", "Zellatmung");

        var first = QuizService.SelectChunksForTopic(chunks, topic, 0);
        var second = QuizService.SelectChunksForTopic(chunks, topic, 1);

        Assert.That(first, Has.Count.EqualTo(QuizService.ChunksPerRequest));
        Assert.That(first.All(c => c.Contains("Mitochondrien")), Is.True);
        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void SelectChunksForTopic_WithoutMatches_UsesSpreadChunks()
    {
        var chunks = Enumerable.Range(0, 30).Select(i => $"Abschnitt {i}").ToList();

        var selected = QuizService.SelectChunksForTopic(chunks, Topic("Quantenphysik", ""), 0);

        Assert.That(selected, Has.Count.EqualTo(QuizService.ChunksPerRequest));
    }

    // ---------- Speicherung ----------

    [Test]
    public async Task SaveAndLoadLibrary_RoundTrip()
    {
        var service = new QuizService(new FixedLlm(""), _tempDir);
        var topic = Topic();
        topic.Documents.Add(new QuizDocumentRef { FileName = "skript.pdf", FilePath = "/tmp/skript.pdf" });
        topic.Questions.Add(new QuizQuestion { Text = "F?", Options = { "a", "b" }, CorrectIndices = { 1 } });
        var library = new QuizLibrary { Topics = { topic } };
        library.History.Add(new QuizResult { Total = 10, Correct = 7, TopicNames = { "Zellbiologie" } });

        await service.SaveLibraryAsync("Testprofil", library);
        var loaded = await service.LoadLibraryAsync("Testprofil");

        Assert.That(loaded.Topics.Single().Questions.Single().CorrectIndices, Is.EqualTo(new[] { 1 }));
        Assert.That(loaded.History.Single().Percent, Is.EqualTo(70));
        Assert.That(File.Exists(Path.Combine(_tempDir, "Quizzes", "Testprofil.json")), Is.True);
    }

    [Test]
    public async Task LoadLibrary_MissingOrBroken_ReturnsEmptyLibrary_AndDropsInvalidQuestions()
    {
        var service = new QuizService(new FixedLlm(""), _tempDir);
        Assert.That((await service.LoadLibraryAsync("Neu")).Topics, Is.Empty);

        Directory.CreateDirectory(Path.Combine(_tempDir, "Quizzes"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Quizzes", "Kaputt.json"), "{ kein json");
        Assert.That((await service.LoadLibraryAsync("Kaputt")).Topics, Is.Empty);

        var library = new QuizLibrary { Topics = { Topic() } };
        library.Topics[0].Questions.Add(new QuizQuestion { Text = "Ungültig", Options = { "a" }, CorrectIndices = { 5 } });
        await service.SaveLibraryAsync("Testprofil", library);
        Assert.That((await service.LoadLibraryAsync("Testprofil")).Topics[0].Questions, Is.Empty);
    }

    private sealed class FixedLlm : ILlmService
    {
        private readonly string _response;
        public FixedLlm(string response) => _response = response;

        public int CallCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;
            return Task.FromResult(_response);
        }

        public Task<bool> ValidateConnectionAsync(AiConfig? config = null) => Task.FromResult(true);
    }
}
