using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Tests für das ReportViewModel: Laden je Profil, Erstellen mit der Fake-KI, Fehlermeldungen,
/// Umbenennen/Löschen, Einbettungen der Studio-Inhalte und Exporte.
/// </summary>
[TestFixture]
public class ReportViewModelTests
{
    private string _tempDir = null!;
    private string _docA = null!;
    private StubLlm _llm = null!;
    private StudioFakes _studio = null!;
    private ReportViewModel _viewModel = null!;

    [SetUp]
    public async Task SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeReportVmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _docA = Path.Combine(_tempDir, "skript.txt");
        await File.WriteAllTextAsync(_docA, "Inhalt");

        _llm = new StubLlm();
        _studio = new StudioFakes();
        _viewModel = CreateViewModel();
        await _viewModel.LoadForProfileAsync("Testprofil");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ReportViewModel CreateViewModel() => new(
        new ReportService(_llm, _tempDir), new FakeDocumentService(), _studio, _studio, _studio, _studio, new DataTableService(_llm, _tempDir),
        new AudioScriptService(_llm, _tempDir));

    private List<DocumentItem> Documents(bool selected = true) => new()
    {
        new DocumentItem { FileName = "skript.txt", FilePath = _docA, IsSelected = selected }
    };

    // ---------- Laden ----------

    [Test]
    public void Load_WithoutReports_OpensForm_AndEmbedsOnlyAvailableContent()
    {
        Assert.That(_viewModel.Reports, Is.Empty);
        Assert.That(_viewModel.IsFormOpen, Is.True);
        Assert.That(_viewModel.SelectedReport, Is.Null);
        Assert.That(_viewModel.HasMindmap, Is.True);
        Assert.That(_viewModel.FlashcardCount, Is.EqualTo(2));
        Assert.That(_viewModel.QuizQuestionCount, Is.EqualTo(12));
        Assert.That(_viewModel.PresentationSlideCount, Is.EqualTo(0));
        Assert.That(_viewModel.NewEmbeds.Mindmap, Is.True);
        Assert.That(_viewModel.NewEmbeds.Presentation, Is.False);
    }

    [Test]
    public async Task Load_BrokenStudioSource_DoesNotPreventReports()
    {
        _studio.ThrowOnMindmapLoad = true;
        var vm = CreateViewModel();

        await vm.LoadForProfileAsync("Testprofil");

        Assert.That(vm.HasMindmap, Is.False);
        Assert.That(vm.FlashcardCount, Is.EqualTo(2));
    }

    // ---------- Erstellen ----------

    [Test]
    public async Task Generate_WithFakeAi_SavesAndSelectsReport()
    {
        _llm.Response = null; // Fake-KI
        _viewModel.Template = ReportTemplate.Faq;
        _viewModel.CustomTitle = "  Mein FAQ  ";

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.IsError, Is.False, _viewModel.StatusMessage);
        Assert.That(_viewModel.Reports, Has.Count.EqualTo(1));
        var report = _viewModel.SelectedReport!;
        Assert.That(report.Title, Is.EqualTo("Mein FAQ"));
        Assert.That(report.Template, Is.EqualTo(ReportTemplate.Faq));
        Assert.That(report.SourceDocuments, Is.EqualTo(new[] { "skript.txt" }));
        Assert.That(report.Embeds.Presentation, Is.False);
        Assert.That(_viewModel.IsFormOpen, Is.False);
        Assert.That(_viewModel.CustomTitle, Is.Empty);
        Assert.That(_viewModel.CurrentHtml, Does.Contain("Mein FAQ"));

        // Gespeichert: neues ViewModel lädt den Bericht wieder
        var reloaded = CreateViewModel();
        await reloaded.LoadForProfileAsync("Testprofil");
        Assert.That(reloaded.Reports, Has.Count.EqualTo(1));
        Assert.That(reloaded.IsFormOpen, Is.False);
        Assert.That(reloaded.SelectedReport!.Title, Is.EqualTo("Mein FAQ"));
    }

    [Test]
    public async Task Generate_WithoutSelectedDocuments_ShowsError()
    {
        await _viewModel.GenerateAsync(Documents(selected: false));

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.Reports, Is.Empty);
        Assert.That(_llm.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Generate_CustomWithoutInstruction_ShowsError()
    {
        _viewModel.Template = ReportTemplate.Custom;

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("Eigene Vorgabe"));
        Assert.That(_llm.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Generate_InvalidAnswer_ShowsFriendlyMessageWithDetails()
    {
        _llm.Response = "Tut mir leid, dazu kann ich nichts sagen.";

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("Bericht"));
        Assert.That(_viewModel.StatusTips, Is.Not.Empty);
        Assert.That(_viewModel.StatusDetails, Does.Contain("Tut mir leid"));
        Assert.That(_viewModel.Reports, Is.Empty);
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task Generate_RepairedAnswer_ShowsWarning()
    {
        _llm.Response = """[{"title": "Teil", "sections": [{"heading": "Eins", "paragraphs": ["Fertig."]}, {"heading": "Zwei", "paragraphs": ["Abgebro""";

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.Reports, Has.Count.EqualTo(1));
        Assert.That(_viewModel.IsWarning, Is.True);
        Assert.That(_viewModel.IsError, Is.False);
    }

    // ---------- Verwalten ----------

    [Test]
    public async Task RenameAndDelete()
    {
        _llm.Response = null;
        await _viewModel.GenerateAsync(Documents());
        var id = _viewModel.SelectedReport!.Id;

        Assert.That(await _viewModel.RenameReportAsync(id, "   "), Is.False);
        Assert.That(await _viewModel.RenameReportAsync(id, " Neuer Name "), Is.True);
        Assert.That(_viewModel.SelectedReport!.Title, Is.EqualTo("Neuer Name"));
        Assert.That(_viewModel.CurrentHtml, Does.Contain("Neuer Name"));

        await _viewModel.DeleteReportAsync(id);

        Assert.That(_viewModel.Reports, Is.Empty);
        Assert.That(_viewModel.SelectedReport, Is.Null);
        Assert.That(_viewModel.IsFormOpen, Is.True);
        Assert.That(_viewModel.CurrentHtml, Is.Empty);
    }

    [Test]
    public async Task Reports_NewestFirst()
    {
        _llm.Response = null;
        _viewModel.CustomTitle = "Erster";
        await _viewModel.GenerateAsync(Documents());
        await Task.Delay(20);
        _viewModel.CustomTitle = "Zweiter";
        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.Reports.Select(r => r.Title), Is.EqualTo(new[] { "Zweiter", "Erster" }));
    }

    [Test]
    public async Task ToggleEmbed_UpdatesHtmlAndIsSaved()
    {
        _llm.Response = null;
        await _viewModel.GenerateAsync(Documents());
        Assert.That(_viewModel.CurrentHtml, Does.Contain("Frage-Thema-A-1"));

        await _viewModel.ToggleEmbedAsync("quiz");

        Assert.That(_viewModel.SelectedReport!.Embeds.Quiz, Is.False);
        Assert.That(_viewModel.CurrentHtml, Does.Not.Contain("Frage-Thema-A-1"));

        var reloaded = CreateViewModel();
        await reloaded.LoadForProfileAsync("Testprofil");
        Assert.That(reloaded.SelectedReport!.Embeds.Quiz, Is.False);
    }

    // ---------- Studio-Inhalte ----------

    [Test]
    public void GetStudioContent_PrefersMatchingDocuments_AndMixesTopics()
    {
        var report = new Report { SourceDocuments = new List<string> { "skript.txt" } };

        var studio = _viewModel.GetStudioContent(report);

        Assert.That(studio.Flashcards.Select(c => c.Question), Is.EqualTo(new[] { "Karte aus Skript" }));
        Assert.That(studio.QuizQuestions, Has.Count.EqualTo(ReportViewModel.MaxEmbeddedQuizQuestions));
        // abwechselnd aus Thema A und B
        Assert.That(studio.QuizQuestions[0].Text, Is.EqualTo("Frage-Thema-A-1"));
        Assert.That(studio.QuizQuestions[1].Text, Is.EqualTo("Frage-Thema-B-1"));
    }

    [Test]
    public void GetStudioContent_WithoutMatch_UsesAll()
    {
        var studio = _viewModel.GetStudioContent(new Report { SourceDocuments = new List<string> { "unbekannt.pdf" } });

        Assert.That(studio.Flashcards, Has.Count.EqualTo(2));
        Assert.That(studio.QuizQuestions, Has.Count.EqualTo(ReportViewModel.MaxEmbeddedQuizQuestions));
    }

    // ---------- Datentabellen ----------

    private async Task SeedDataTablesAsync(params (string Title, string Source, int Rows, int AgeMinutes)[] tables)
    {
        var library = new DataTableLibrary();
        foreach (var (title, source, rows, age) in tables)
        {
            var table = new DataTableDocument
            {
                Title = title,
                Columns = new List<string> { "Begriff", "Erklärung" },
                SourceDocuments = new List<string> { source },
                CreatedAt = DateTime.Now.AddMinutes(-age)
            };
            for (var i = 0; i < rows; i++)
                table.Rows.Add(new DataTableRow { Cells = new List<string> { $"{title}-Begriff-{i}", "x" }, Source = source });
            library.Tables.Add(table);
        }

        await new DataTableService(_llm, _tempDir).SaveLibraryAsync("Testprofil", library);
        await _viewModel.LoadForProfileAsync("Testprofil");
    }

    [Test]
    public async Task DataTables_NewReportEmbedsThem_AndToggleIsSaved()
    {
        await SeedDataTablesAsync(("Glossar", "skript.txt", 3, 0));
        Assert.That(_viewModel.DataTableCount, Is.EqualTo(1));
        Assert.That(_viewModel.NewEmbeds.DataTables, Is.True);

        _llm.Response = null;
        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.SelectedReport!.Embeds.DataTables, Is.True);
        Assert.That(_viewModel.CurrentHtml, Does.Contain("Glossar-Begriff-0").And.Contain("id=\"datentabellen\""));

        await _viewModel.ToggleEmbedAsync("datatables");
        Assert.That(_viewModel.SelectedReport.Embeds.DataTables, Is.False);
        Assert.That(_viewModel.CurrentHtml, Does.Not.Contain("Glossar-Begriff-0"));

        var reloaded = CreateViewModel();
        await reloaded.LoadForProfileAsync("Testprofil");
        Assert.That(reloaded.SelectedReport!.Embeds.DataTables, Is.False);

        await reloaded.ToggleEmbedAsync("datatables");
        Assert.That(reloaded.CurrentHtml, Does.Contain("Glossar-Begriff-0"));
    }

    [Test]
    public async Task AudioScripts_NewReportEmbedsText_PreferSourceDocuments_AndToggleIsSaved()
    {
        var library = new AudioLibrary();
        library.Items.Add(new AudioSummary
        {
            Title = "Fremder Beitrag", SourceDocuments = new List<string> { "anderes.pdf" },
            Segments = new List<AudioSegment> { new() { Speaker = "Sprecherin", Text = "Fremder Inhalt." } }
        });
        library.Items.Add(new AudioSummary
        {
            Title = "Zellen im Gespräch", Format = AudioScriptFormat.Podcast, SourceDocuments = new List<string> { "skript.txt" },
            AudioFileName = "Zellen_im_Gespraech_1234abcd.wav",
            Segments = new List<AudioSegment>
            {
                new() { Speaker = "Moderatorin", Text = "Was ist eine Zelle?" },
                new() { Speaker = "Experte", Text = "Die kleinste Einheit des Lebens." }
            }
        });
        await new AudioScriptService(_llm, _tempDir).SaveLibraryAsync("Testprofil", library);
        await _viewModel.LoadForProfileAsync("Testprofil");
        Assert.That(_viewModel.AudioScriptCount, Is.EqualTo(2));
        Assert.That(_viewModel.NewEmbeds.AudioScripts, Is.True);

        _llm.Response = null;
        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.SelectedReport!.Embeds.AudioScripts, Is.True);
        Assert.That(_viewModel.CurrentHtml, Does.Contain("Die kleinste Einheit des Lebens.").And.Contain("id=\"audio\"")
            .And.Contain("Audiodatei: Zellen_im_Gespraech_1234abcd.wav"));
        Assert.That(_viewModel.CurrentHtml, Does.Not.Contain("Fremder Inhalt."), "bevorzugt aus den Quelldokumenten");

        await _viewModel.ToggleEmbedAsync("audio");
        Assert.That(_viewModel.CurrentHtml, Does.Not.Contain("Die kleinste Einheit des Lebens."));

        var reloaded = CreateViewModel();
        await reloaded.LoadForProfileAsync("Testprofil");
        Assert.That(reloaded.SelectedReport!.Embeds.AudioScripts, Is.False);
    }

    [Test]
    public async Task DataTables_FormCheckboxCanSwitchThemOff()
    {
        await SeedDataTablesAsync(("Glossar", "skript.txt", 3, 0));
        _viewModel.ToggleNewEmbed("datatables");
        _llm.Response = null;

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.SelectedReport!.Embeds.DataTables, Is.False);
        Assert.That(_viewModel.CurrentHtml, Does.Not.Contain("Glossar-Begriff-0"));
    }

    [Test]
    public async Task GetStudioContent_DataTables_PreferSourceDocuments_NewestFirst_AndAreLimited()
    {
        await SeedDataTablesAsync(
            ("Fremd", "anderes.pdf", 2, 0),
            ("Alt", "skript.txt", 3, 60),
            ("Lang", "skript.txt", 70, 10),
            ("Leer", "skript.txt", 0, 1));

        var studio = _viewModel.GetStudioContent(new Report { SourceDocuments = new List<string> { "SKRIPT.txt" } });

        Assert.That(studio.DataTables.Select(t => t.Table.Title), Is.EqualTo(new[] { "Lang", "Alt" }), "passende Dokumente, neueste zuerst, ohne leere");
        Assert.That(studio.DataTables[0].Rows, Has.Count.EqualTo(ReportViewModel.MaxEmbeddedDataTableRows));
        Assert.That(studio.DataTables[0].IsTruncated, Is.True);
        Assert.That(studio.DataTables[1].IsTruncated, Is.False);

        var fallback = _viewModel.GetStudioContent(new Report { SourceDocuments = new List<string> { "unbekannt.pdf" } });
        Assert.That(fallback.DataTables.Select(t => t.Table.Title), Is.EqualTo(new[] { "Fremd", "Lang", "Alt" }));
    }

    [Test]
    public async Task GetStudioContent_DataTables_AtMostFive()
    {
        await SeedDataTablesAsync(Enumerable.Range(1, 7).Select(i => ($"T{i}", "skript.txt", 1, i)).ToArray());

        var studio = _viewModel.GetStudioContent(new Report { SourceDocuments = new List<string> { "skript.txt" } });

        Assert.That(studio.DataTables, Has.Count.EqualTo(ReportViewModel.MaxEmbeddedDataTables));
        Assert.That(studio.DataTables[0].Table.Title, Is.EqualTo("T1"));
    }

    // ---------- Export ----------

    [Test]
    public async Task Export_AllFormats_WriteFiles()
    {
        _llm.Response = null;
        _viewModel.CustomTitle = "Export: Test/1";
        await _viewModel.GenerateAsync(Documents());

        foreach (var format in Enum.GetValues<ReportExportFormat>())
        {
            var name = _viewModel.SuggestFileName(format);
            Assert.That(name, Does.EndWith("." + ReportViewModel.Extension(format)));
            Assert.That(name, Does.Not.Contain("/"));
            Assert.That(name, Does.Not.Contain(":"));

            var path = Path.Combine(_tempDir, name);
            Assert.That(await _viewModel.ExportAsync(format, path), Is.True, _viewModel.StatusMessage);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task Export_WithoutReport_ReturnsFalse()
    {
        Assert.That(await _viewModel.ExportAsync(ReportExportFormat.Markdown, Path.Combine(_tempDir, "x.md")), Is.False);
    }

    // ---------- Testdoppel ----------

    /// <summary>Antwortet mit festem Text; ohne Text übernimmt die Fake-KI.</summary>
    private sealed class StubLlm : ILlmService
    {
        private readonly FakeLlmService _fake = new();
        public string? Response { get; set; }
        public int CallCount { get; private set; }

        public Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Response == null ? _fake.AskQuestionAsync(prompt, contextChunks, cancellationToken) : Task.FromResult(Response);
        }

        public Task<bool> ValidateConnectionAsync(AiConfig? config = null) => Task.FromResult(true);
    }

    /// <summary>Studio-Inhalte des Profils: Mindmap, 2 Karteikarten, 2 Quiz-Themen mit je 6 Fragen, keine Präsentation.</summary>
    private sealed class StudioFakes : IMindmapService, IFlashcardService, IQuizService, IPresentationService
    {
        public bool ThrowOnMindmapLoad { get; set; }

        public Task<MindmapDocument?> LoadMindmapAsync(string profileName) => ThrowOnMindmapLoad
            ? throw new IOException("defekt")
            : Task.FromResult<MindmapDocument?>(new MindmapDocument { Root = new MindmapNode { Title = "Zelle" } });

        public Task<List<Flashcard>> LoadFlashcardsAsync(string profileName) => Task.FromResult(new List<Flashcard>
        {
            new() { Question = "Karte aus Skript", Answer = "A", SourceDocument = "skript.txt" },
            new() { Question = "Karte aus anderem Dokument", Answer = "B", SourceDocument = "anderes.pdf" }
        });

        public Task<QuizLibrary> LoadLibraryAsync(string profileName)
        {
            var library = new QuizLibrary();
            foreach (var name in new[] { "A", "B" })
            {
                var topic = new QuizTopic { Name = $"Thema {name}" };
                topic.Documents.Add(new QuizDocumentRef { FileName = "skript.txt" });
                for (var i = 1; i <= 6; i++)
                    topic.Questions.Add(new QuizQuestion { Text = $"Frage-Thema-{name}-{i}", Options = new List<string> { "Ja", "Nein" }, CorrectIndices = new List<int> { 0 } });
                library.Topics.Add(topic);
            }
            return Task.FromResult(library);
        }

        public Task<PresentationDocument?> LoadPresentationAsync(string profileName) => Task.FromResult<PresentationDocument?>(null);

        // Nicht benötigt
        public int BatchSize => 10;
        public Task<MindmapGenerationResult> GenerateMindmapAsync(IEnumerable<string> contextChunks, string rootTitle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveMindmapAsync(string profileName, MindmapDocument mindmap) => Task.CompletedTask;
        public Task DeleteMindmapAsync(string profileName) => Task.CompletedTask;
        string IMindmapService.ExportToMarkdown(MindmapDocument mindmap) => string.Empty;
        public Task<List<Flashcard>> GenerateFlashcardsAsync(IEnumerable<string> contextChunks, int count = 5) => throw new NotSupportedException();
        public Task SaveFlashcardsAsync(string profileName, IEnumerable<Flashcard> flashcards) => Task.CompletedTask;
        string IFlashcardService.ExportToMarkdown(string profileName, IEnumerable<Flashcard> flashcards) => string.Empty;
        public Task<QuizTopicSuggestionResult> SuggestTopicsAsync(IEnumerable<string> contextChunks, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<QuizQuestionGenerationResult> GenerateQuestionsAsync(QuizTopic topic, IReadOnlyList<string> topicChunks, int count, IReadOnlyCollection<QuizQuestionType> allowedTypes, int batchIndex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveLibraryAsync(string profileName, QuizLibrary library) => Task.CompletedTask;
        public Task<PresentationGenerationResult> GenerateFromDocumentsAsync(IEnumerable<string> contextChunks, string fallbackTitle, int slideCount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public PresentationDocument CreateFromMindmap(MindmapDocument mindmap) => new();
        public Task SavePresentationAsync(string profileName, PresentationDocument presentation) => Task.CompletedTask;
        public Task DeletePresentationAsync(string profileName) => Task.CompletedTask;
        public Task ExportToPptxAsync(PresentationDocument presentation, string filePath) => Task.CompletedTask;
    }

    private sealed class FakeDocumentService : IDocumentService
    {
        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(1);

        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250) =>
            Task.FromResult(new List<string> { "Die Zelle ist die kleinste Einheit des Lebens. Mitochondrien liefern Energie." });
    }
}
