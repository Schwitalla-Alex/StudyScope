using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Tests für das QuizViewModel: Themen anlegen/vorschlagen, Quiz starten (Fragen paketweise ergänzen),
/// Ergebnis und Verlauf, „Falsche wiederholen“, Fehlermeldungen.
/// </summary>
[TestFixture]
public class QuizViewModelTests
{
    private string _tempDir = null!;
    private string _docA = null!;
    private string _docB = null!;
    private FakeQuizService _service = null!;
    private QuizViewModel _viewModel = null!;

    [SetUp]
    public async Task SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeQuizVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _docA = Path.Combine(_tempDir, "a.txt");
        _docB = Path.Combine(_tempDir, "b.txt");
        await File.WriteAllTextAsync(_docA, "Inhalt A");
        await File.WriteAllTextAsync(_docB, "Inhalt B");

        _service = new FakeQuizService();
        _viewModel = new QuizViewModel(_service, new FakeDocumentService(), new Random(1));
        await _viewModel.LoadForProfileAsync("Testprofil");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private List<DocumentItem> Docs(bool aSelected = true, bool bSelected = false) => new()
    {
        new DocumentItem { FileName = "a.txt", FilePath = _docA, IsSelected = aSelected },
        new DocumentItem { FileName = "b.txt", FilePath = _docB, IsSelected = bSelected }
    };

    private async Task<QuizTopic> CreateTopicAsync(string name)
    {
        _viewModel.OpenNewTopicForm(Docs());
        _viewModel.FormName = name;
        Assert.That(await _viewModel.SaveTopicFormAsync(Docs()), Is.True);
        return _viewModel.Topics.Single(t => t.Name == name);
    }

    // ---------- Themen ----------

    [Test]
    public async Task SaveTopicForm_CreatesSelectedTopicWithDocuments()
    {
        _viewModel.OpenNewTopicForm(Docs(aSelected: true, bSelected: false));
        _viewModel.ToggleFormDocument(_docB);
        _viewModel.FormName = "  Zellbiologie ";
        _viewModel.FormDescription = "Organellen";

        var saved = await _viewModel.SaveTopicFormAsync(Docs());

        Assert.That(saved, Is.True);
        var topic = _viewModel.Topics.Single();
        Assert.That(topic.Name, Is.EqualTo("Zellbiologie"));
        Assert.That(topic.Description, Is.EqualTo("Organellen"));
        Assert.That(topic.Documents.Select(d => d.FileName), Is.EqualTo(new[] { "a.txt", "b.txt" }));
        Assert.That(topic.Origin, Is.EqualTo(QuizTopicOrigin.Manual));
        Assert.That(_viewModel.IsTopicSelected(topic.Id), Is.True);
        Assert.That(_viewModel.IsTopicFormOpen, Is.False);
        Assert.That(_service.SaveCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task SaveTopicForm_ValidatesNameDuplicatesAndDocuments()
    {
        await CreateTopicAsync("Genetik");

        _viewModel.OpenNewTopicForm(Docs());
        _viewModel.FormName = "";
        Assert.That(await _viewModel.SaveTopicFormAsync(Docs()), Is.False);
        Assert.That(_viewModel.FormError, Does.Contain("Namen"));

        _viewModel.FormName = "genetik";
        Assert.That(await _viewModel.SaveTopicFormAsync(Docs()), Is.False);
        Assert.That(_viewModel.FormError, Does.Contain("gibt es bereits"));

        _viewModel.FormName = "Neu";
        _viewModel.ToggleFormDocument(_docA); // abwählen → keine Dokumente
        Assert.That(await _viewModel.SaveTopicFormAsync(Docs()), Is.False);
        Assert.That(_viewModel.FormError, Does.Contain("Dokument"));
    }

    [Test]
    public async Task EditTopic_ChangesNameButKeepsQuestions()
    {
        var topic = await CreateTopicAsync("Alt");
        topic.Questions.Add(SampleQuestion("vorhanden"));

        _viewModel.OpenEditTopicForm(topic.Id);
        Assert.That(_viewModel.IsEditingTopic, Is.True);
        _viewModel.FormName = "Neu";
        await _viewModel.SaveTopicFormAsync(Docs());

        Assert.That(_viewModel.Topics.Single().Name, Is.EqualTo("Neu"));
        Assert.That(_viewModel.Topics.Single().Questions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task DeleteAndResetTopic()
    {
        var topic = await CreateTopicAsync("Thema");
        topic.Questions.Add(SampleQuestion("q"));

        await _viewModel.ResetTopicQuestionsAsync(topic.Id);
        Assert.That(topic.Questions, Is.Empty);

        await _viewModel.DeleteTopicAsync(topic.Id);
        Assert.That(_viewModel.Topics, Is.Empty);
        Assert.That(_viewModel.SelectedTopicCount, Is.EqualTo(0));
    }

    [Test]
    public async Task SuggestTopics_AddsCheckedSuggestionsWithSelectedDocuments()
    {
        await CreateTopicAsync("Genetik");
        _service.Suggestions = new QuizTopicSuggestionResult
        {
            Topics = { new() { Name = "Zellaufbau", Description = "Bestandteile" }, new() { Name = "genetik" }, new() { Name = "Stoffwechsel" } }
        };

        await _viewModel.SuggestTopicsAsync(Docs(aSelected: false, bSelected: true));

        Assert.That(_viewModel.Suggestions.Select(s => s.Suggestion.Name), Is.EqualTo(new[] { "Zellaufbau", "Stoffwechsel" }), "vorhandene Themen werden ausgelassen");

        _viewModel.ToggleSuggestion(_viewModel.Suggestions[1]); // Stoffwechsel abwählen
        await _viewModel.AcceptSuggestionsAsync();

        var added = _viewModel.Topics.Single(t => t.Name == "Zellaufbau");
        Assert.That(added.Origin, Is.EqualTo(QuizTopicOrigin.Ai));
        Assert.That(added.Documents.Single().FileName, Is.EqualTo("b.txt"));
        Assert.That(_viewModel.Topics.Any(t => t.Name == "Stoffwechsel"), Is.False);
        Assert.That(_viewModel.Suggestions, Is.Empty);
    }

    [Test]
    public async Task SuggestTopics_WithoutSelectedDocuments_ShowsError()
    {
        await _viewModel.SuggestTopicsAsync(Docs(aSelected: false, bSelected: false));

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_service.SuggestCalls, Is.EqualTo(0));
    }

    // ---------- Quiz starten ----------

    [Test]
    public async Task StartQuiz_WithoutTopic_ShowsError()
    {
        await _viewModel.StartQuizAsync();

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.Screen, Is.EqualTo(QuizScreen.Library));
    }

    [Test]
    public async Task StartQuiz_GeneratesMissingQuestionsInBatches_AndSavesPool()
    {
        var topic = await CreateTopicAsync("Thema");
        _viewModel.QuestionCount = 25;

        await _viewModel.StartQuizAsync();

        Assert.That(_service.GenerateRequests, Is.EqualTo(new[] { 10, 10, 5 }), "Pakete zu je 10");
        Assert.That(_service.BatchIndices, Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(topic.Questions, Has.Count.EqualTo(25));
        Assert.That(_viewModel.Screen, Is.EqualTo(QuizScreen.Running));
        Assert.That(_viewModel.Session!.Count, Is.EqualTo(25));
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task StartQuiz_ReusesExistingPool()
    {
        var topic = await CreateTopicAsync("Thema");
        for (var i = 0; i < 12; i++) topic.Questions.Add(SampleQuestion($"vorhanden {i}"));
        _viewModel.QuestionCount = 10;

        await _viewModel.StartQuizAsync();

        Assert.That(_service.GenerateRequests, Is.Empty);
        Assert.That(_viewModel.Session!.Count, Is.EqualTo(10));
    }

    [Test]
    public async Task StartQuiz_SplitsQuestionsAcrossTopics()
    {
        var a = await CreateTopicAsync("A");
        var b = await CreateTopicAsync("B");
        _viewModel.QuestionCount = 7;

        await _viewModel.StartQuizAsync();

        Assert.That(a.Questions, Has.Count.EqualTo(4));
        Assert.That(b.Questions, Has.Count.EqualTo(3));
        Assert.That(_viewModel.Session!.Items.Select(i => i.TopicName).Distinct().OrderBy(n => n), Is.EqualTo(new[] { "A", "B" }));
    }

    [Test]
    public async Task StartQuiz_LlmDeliversTooFew_StartsWithAvailableQuestionsAndWarns()
    {
        await CreateTopicAsync("Thema");
        _service.MaxQuestionsPerCall = 3;
        _service.TotalLimit = 6;  // danach liefert die KI nichts Neues mehr
        _viewModel.QuestionCount = 20;

        await _viewModel.StartQuizAsync();

        Assert.That(_viewModel.Session!.Count, Is.EqualTo(6));
        Assert.That(_viewModel.IsWarning, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("nur 6 von 20"));
    }

    [Test]
    public async Task StartQuiz_LlmFailsCompletely_ShowsQuizSpecificError()
    {
        await CreateTopicAsync("Thema");
        _service.FailWith = new QuizQuestionGenerationResult { RawResponse = "kaputt [{\"q", FailureReason = MindmapFailureReason.InvalidJson };

        await _viewModel.StartQuizAsync();

        Assert.That(_viewModel.Screen, Is.EqualTo(QuizScreen.Library));
        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("kein Quiz"));
        Assert.That(_viewModel.StatusDetails, Does.Contain("kaputt"));
        Assert.That(_service.GenerateRequests, Has.Count.EqualTo(2), "nach zwei leeren Antworten wird aufgegeben");
    }

    [Test]
    public async Task StartQuiz_MissingDocument_IsReported()
    {
        var topic = await CreateTopicAsync("Thema");
        File.Delete(_docA);

        await _viewModel.StartQuizAsync();

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusTips.Any(t => t.Contains("a.txt")), Is.True);
        Assert.That(topic.Questions, Is.Empty);
    }

    [Test]
    public async Task StartQuiz_Cancelled_KeepsGeneratedQuestions()
    {
        var topic = await CreateTopicAsync("Thema");
        _service.OnGenerate = count => { if (_service.GenerateRequests.Count == 2) _viewModel.Cancel(); };
        _viewModel.QuestionCount = 30;

        await _viewModel.StartQuizAsync();

        Assert.That(_viewModel.Screen, Is.EqualTo(QuizScreen.Library));
        Assert.That(_viewModel.StatusMessage, Does.Contain("abgebrochen"));
        Assert.That(topic.Questions, Has.Count.EqualTo(10), "erstes Paket bleibt erhalten");
    }

    // ---------- Ergebnis, Verlauf, Wiederholen ----------

    [Test]
    public async Task FinishQuiz_SavesResultInHistory_AndRepeatWrongStartsWithWrongQuestions()
    {
        var topic = await CreateTopicAsync("Thema");
        _viewModel.QuestionCount = 5;
        _viewModel.Mode = QuizMode.Exam;
        await _viewModel.StartQuizAsync();

        var session = _viewModel.Session!;
        for (var i = 0; i < session.Count; i++)
        {
            var q = session.Current!.Question;
            var answer = i < 3 ? q.CorrectIndices[0] : Enumerable.Range(0, q.Options.Count).First(o => !q.CorrectIndices.Contains(o));
            _viewModel.SelectOption(answer);
            _viewModel.NextQuestion();
        }

        await _viewModel.FinishQuizAsync();

        Assert.That(_viewModel.Screen, Is.EqualTo(QuizScreen.Result));
        Assert.That(_viewModel.LastResult!.Correct, Is.EqualTo(3));
        Assert.That(_viewModel.WrongCount, Is.EqualTo(2));
        Assert.That(_viewModel.History.Single().TopicNames, Is.EqualTo(new[] { "Thema" }));
        Assert.That(_viewModel.TopicStatistics(topic.Id)!.Value.Percent, Is.EqualTo(60));

        _viewModel.RepeatWrongQuestions();

        Assert.That(_viewModel.Screen, Is.EqualTo(QuizScreen.Running));
        Assert.That(_viewModel.Session!.Count, Is.EqualTo(2));
        Assert.That(_viewModel.Session.Mode, Is.EqualTo(QuizMode.Exam));
    }

    [Test]
    public async Task AbortQuiz_DoesNotWriteHistory()
    {
        await CreateTopicAsync("Thema");
        await _viewModel.StartQuizAsync();

        _viewModel.AbortQuiz();

        Assert.That(_viewModel.Screen, Is.EqualTo(QuizScreen.Library));
        Assert.That(_viewModel.History, Is.Empty);
    }

    [Test]
    public async Task ClearHistory_RemovesAllResults()
    {
        await CreateTopicAsync("Thema");
        _viewModel.QuestionCount = 5;
        await _viewModel.StartQuizAsync();
        await _viewModel.FinishQuizAsync();

        await _viewModel.ClearHistoryAsync();

        Assert.That(_viewModel.History, Is.Empty);
    }

    [Test]
    public void QuestionCount_IsClampedTo5To100()
    {
        _viewModel.QuestionCount = 1;
        Assert.That(_viewModel.QuestionCount, Is.EqualTo(5));
        _viewModel.QuestionCount = 500;
        Assert.That(_viewModel.QuestionCount, Is.EqualTo(100));
    }

    [Test]
    public async Task ToggleType_LimitsGeneratedAndAvailableQuestions()
    {
        var topic = await CreateTopicAsync("Thema");
        topic.Questions.Add(SampleQuestion("s"));
        _viewModel.ToggleType(QuizQuestionType.SingleChoice);

        Assert.That(_viewModel.IsTypeAllowed(QuizQuestionType.SingleChoice), Is.False);
        Assert.That(_viewModel.AvailableQuestions(topic), Is.EqualTo(0));

        _viewModel.ToggleType(QuizQuestionType.TrueFalse);
        _viewModel.ToggleType(QuizQuestionType.MultipleChoice);
        await _viewModel.StartQuizAsync();
        Assert.That(_viewModel.StatusMessage, Does.Contain("Fragetyp"));
    }

    [Test]
    public async Task LoadForProfile_ResetsState()
    {
        await CreateTopicAsync("Thema");
        await _viewModel.StartQuizAsync();

        await _viewModel.LoadForProfileAsync("Anderes Profil");

        Assert.That(_viewModel.Screen, Is.EqualTo(QuizScreen.Library));
        Assert.That(_viewModel.Session, Is.Null);
        Assert.That(_viewModel.Topics, Is.Empty);
    }

    [TestCase(95, "Hervorragend")]
    [TestCase(80, "Sehr gut")]
    [TestCase(60, "Gut")]
    [TestCase(20, "wiederholen")]
    public void Verdict_DependsOnScore(double percent, string expected)
    {
        Assert.That(QuizViewModel.Verdict(percent), Does.Contain(expected));
    }

    // ---------- Attrappen ----------

    private static QuizQuestion SampleQuestion(string text) =>
        new() { Type = QuizQuestionType.SingleChoice, Text = text, Options = { "a", "b", "c", "d" }, CorrectIndices = { 1 } };

    private sealed class FakeQuizService : IQuizService
    {
        private readonly Dictionary<string, QuizLibrary> _store = new();
        private int _created;

        public int BatchSize => 10;
        public int SaveCount { get; private set; }
        public int SuggestCalls { get; private set; }
        public List<int> GenerateRequests { get; } = new();
        public List<int> BatchIndices { get; } = new();
        public int MaxQuestionsPerCall { get; set; } = int.MaxValue;
        public int TotalLimit { get; set; } = int.MaxValue;
        public QuizQuestionGenerationResult? FailWith { get; set; }
        public Action<int>? OnGenerate { get; set; }
        public QuizTopicSuggestionResult Suggestions { get; set; } = new();

        public Task<QuizTopicSuggestionResult> SuggestTopicsAsync(IEnumerable<string> contextChunks, string? outputLanguage = null, CancellationToken cancellationToken = default)
        {
            SuggestCalls++;
            return Task.FromResult(Suggestions);
        }

        public Task<QuizQuestionGenerationResult> GenerateQuestionsAsync(QuizTopic topic, IReadOnlyList<string> topicChunks, int count,
            IReadOnlyCollection<QuizQuestionType> allowedTypes, int batchIndex, string? outputLanguage = null, CancellationToken cancellationToken = default)
        {
            GenerateRequests.Add(count);
            BatchIndices.Add(batchIndex);
            OnGenerate?.Invoke(count);

            if (FailWith != null)
                return Task.FromResult(FailWith);

            var number = Math.Min(Math.Min(count, MaxQuestionsPerCall), Math.Max(0, TotalLimit - _created));
            var questions = Enumerable.Range(0, number).Select(_ => SampleQuestion($"{topic.Name} Frage {++_created}")).ToList();
            return Task.FromResult(new QuizQuestionGenerationResult { Questions = questions });
        }

        public Task<QuizLibrary> LoadLibraryAsync(string profileName) =>
            Task.FromResult(_store.TryGetValue(profileName, out var lib) ? lib : new QuizLibrary());

        public Task SaveLibraryAsync(string profileName, QuizLibrary library)
        {
            SaveCount++;
            _store[profileName] = library;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDocumentService : IDocumentService
    {
        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(1);

        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250) =>
            Task.FromResult(new List<string> { $"Inhalt von {Path.GetFileName(filePath)}" });
    }
}
