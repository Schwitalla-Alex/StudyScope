using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Tests für die Mindmap-Erzeugung: robuste Auswertung der KI-Antwort, Auswahl der Textstellen,
/// Speicherung je Profil und Markdown-Export.
/// </summary>
[TestFixture]
public class MindmapServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeMindmapTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---------- Auswertung der KI-Antwort ----------

    [Test]
    public void ParseMindmap_ArrayWithOneRoot_ReturnsTree()
    {
        const string json = """
            [{"title": "Zellbiologie", "summary": "Aufbau der Zelle.", "children": [
                {"title": "Zellkern", "summary": "Enthält die DNA.", "children": [
                    {"title": "Chromosomen", "children": []}
                ]},
                {"title": "Mitochondrien", "summary": "Energiegewinnung."}
            ]}]
            """;

        var root = MindmapService.ParseMindmap(json, "Fallback");

        Assert.That(root, Is.Not.Null);
        Assert.That(root!.Title, Is.EqualTo("Zellbiologie"));
        Assert.That(root.Summary, Is.EqualTo("Aufbau der Zelle."));
        Assert.That(root.Children, Has.Count.EqualTo(2));
        Assert.That(root.Children[0].Children[0].Title, Is.EqualTo("Chromosomen"));
        Assert.That(root.Children[1].Children, Is.Empty);
    }

    [Test]
    public void ParseMindmap_ObjectInMarkdownCodeBlockWithText_ReturnsTree()
    {
        const string raw = "Hier ist die Mindmap:\n```json\n{\"title\": \"BGB\", \"children\": [{\"title\": \"Schuldrecht\"}]}\n```\nViel Erfolg!";

        var root = MindmapService.ParseMindmap(raw, "Fallback");

        Assert.That(root, Is.Not.Null);
        Assert.That(root!.Title, Is.EqualTo("BGB"));
        Assert.That(root.Children, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseMindmap_CodeBlockWithoutText_ReturnsTree()
    {
        const string raw = "```json\n[{\"title\": \"A\", \"children\": [{\"title\": \"B\"}]}]\n```";

        var root = MindmapService.ParseMindmap(raw, "Fallback");

        Assert.That(root?.Children.Single().Title, Is.EqualTo("B"));
    }

    [Test]
    public void ParseMindmap_SeveralRoots_AreGroupedUnderFallbackTitle()
    {
        const string json = """[{"title": "Thema 1"}, {"title": "Thema 2"}, {"title": ""}]""";

        var root = MindmapService.ParseMindmap(json, "Skript Mathematik");

        Assert.That(root, Is.Not.Null);
        Assert.That(root!.Title, Is.EqualTo("Skript Mathematik"));
        Assert.That(root.Children.Select(c => c.Title), Is.EqualTo(new[] { "Thema 1", "Thema 2" }));
    }

    [Test]
    public void ParseMindmap_GermanKeysAndStringChildren_AreAccepted()
    {
        const string json = """[{"Titel": "Steuerrecht", "Zusammenfassung": "Grundlagen.", "Unterthemen": ["Einkommensteuer", "Umsatzsteuer"]}]""";

        var root = MindmapService.ParseMindmap(json, "Fallback");

        Assert.That(root, Is.Not.Null);
        Assert.That(root!.Title, Is.EqualTo("Steuerrecht"));
        Assert.That(root.Summary, Is.EqualTo("Grundlagen."));
        Assert.That(root.Children.Select(c => c.Title), Is.EqualTo(new[] { "Einkommensteuer", "Umsatzsteuer" }));
    }

    [TestCase("")]
    [TestCase("Leider kann ich das nicht.")]
    [TestCase("[Anfrage durch Benutzer abgebrochen]")]
    [TestCase("[{\"title\": \"kaputt\", ")]
    [TestCase("[]")]
    public void ParseMindmap_UnusableResponse_ReturnsNull(string raw)
    {
        Assert.That(MindmapService.ParseMindmap(raw, "Fallback"), Is.Null);
    }

    [Test]
    public void ParseMindmap_TooDeepTree_IsCutAtMaxDepth()
    {
        // 8 Ebenen verschachtelt
        var json = "{\"title\": \"E0\"}";
        for (var i = 1; i <= 8; i++)
            json = $"{{\"title\": \"E{i}\", \"children\": [{json}]}}";

        var root = MindmapService.ParseMindmap(json, "Fallback");

        var depth = 0;
        var node = root;
        while (node!.Children.Count > 0)
        {
            node = node.Children[0];
            depth++;
        }

        Assert.That(depth, Is.EqualTo(MindmapService.MaxDepth));
    }

    [Test]
    public void ParseMindmap_TooManyNodes_IsLimited()
    {
        var children = string.Join(",", Enumerable.Range(1, 500).Select(i => $"{{\"title\": \"K{i}\"}}"));
        var json = $"[{{\"title\": \"Wurzel\", \"children\": [{children}]}}]";

        var root = MindmapService.ParseMindmap(json, "Fallback");

        Assert.That(root!.DescendantsAndSelf().Count(), Is.EqualTo(MindmapService.MaxNodes));
    }

    [Test]
    public void ParseMindmap_AssignsUniqueIds()
    {
        const string json = """[{"title": "A", "children": [{"title": "B"}, {"title": "C"}]}]""";

        var ids = MindmapService.ParseMindmap(json, "Fallback")!.DescendantsAndSelf().Select(n => n.Id).ToList();

        Assert.That(ids.Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void ParseMindmap_FakeAiResponse_IsValid()
    {
        var raw = FakeMindmapResponse.Create(new List<string> { "Photosynthese wandelt Lichtenergie in chemische Energie um.", "Zweite Textstelle" });

        var root = MindmapService.ParseMindmap(raw, "Fallback");

        Assert.That(root, Is.Not.Null);
        Assert.That(root!.Children, Has.Count.EqualTo(2));
    }

    // ---------- Abgeschnittene Antworten und Fehlerursachen ----------

    [Test]
    public void AnalyzeResponse_TruncatedResponse_IsRepairedUpToLastCompleteTopic()
    {
        // Typisch für lokale Modelle: Antwort bricht mitten im dritten Ast ab
        const string raw = """
            [{"title": "Tätigkeitsprofil", "summary": "Überblick.", "children": [
                {"title": "Berufserfahrung", "summary": "Stationen.", "children": [
                    {"title": "Softwareentwicklung", "summary": "C# und WPF."},
                    {"title": "Datenbanken", "summary": "SQL Server."}
                ]},
                {"title": "Kompetenzen", "summary": "Fachlich und methodisch."},
                {"title": "Projekte", "summary": "Ausgewählte Proj
            """;

        var result = MindmapService.AnalyzeResponse(raw, "Fallback");

        Assert.That(result.Root, Is.Not.Null);
        Assert.That(result.WasRepaired, Is.True);
        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.None));
        Assert.That(result.Root!.Title, Is.EqualTo("Tätigkeitsprofil"));
        Assert.That(result.Root.Children.Select(c => c.Title), Is.EqualTo(new[] { "Berufserfahrung", "Kompetenzen" }));
        Assert.That(result.Root.Children[0].Children, Has.Count.EqualTo(2));
    }

    [Test]
    public void AnalyzeResponse_TruncatedInsideCodeBlock_IsRepaired()
    {
        const string raw = "```json\n[{\"title\": \"A\", \"children\": [{\"title\": \"B\"}, {\"title\": \"C\", \"summ";

        var result = MindmapService.AnalyzeResponse(raw, "Fallback");

        Assert.That(result.WasRepaired, Is.True);
        Assert.That(result.Root!.Children.Select(c => c.Title), Is.EqualTo(new[] { "B" }));
    }

    [Test]
    public void AnalyzeResponse_BracketsInsideStrings_DoNotConfuseRepair()
    {
        const string raw = "[{\"title\": \"Mengen {a, b} und [x]\", \"children\": [{\"title\": \"Klammer ] im \\\"Text\\\"\"}, {\"title\": \"abgeschn";

        var result = MindmapService.AnalyzeResponse(raw, "Fallback");

        Assert.That(result.Root!.Title, Is.EqualTo("Mengen {a, b} und [x]"));
        Assert.That(result.Root.Children.Single().Title, Is.EqualTo("Klammer ] im \"Text\""));
    }

    [Test]
    public void AnalyzeResponse_CompleteResponse_IsNotMarkedAsRepaired()
    {
        var result = MindmapService.AnalyzeResponse("""[{"title": "A", "children": [{"title": "B"}]}]""", "Fallback");

        Assert.That(result.WasRepaired, Is.False);
        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.None));
    }

    [TestCase("", MindmapFailureReason.EmptyResponse)]
    [TestCase("   ", MindmapFailureReason.EmptyResponse)]
    [TestCase("Gemini-Fehler: Es ist kein API-Schlüssel in den Einstellungen hinterlegt.", MindmapFailureReason.NoJson)]
    [TestCase("Ollama-Fehler [404]: model not found", MindmapFailureReason.NoJson)]
    [TestCase("Gerne! Die wichtigsten Themen sind A, B und C.", MindmapFailureReason.NoJson)]
    [TestCase("[{\"titel_falsch\": 1}]", MindmapFailureReason.NoTopics)]
    [TestCase("[{\"title\": ", MindmapFailureReason.InvalidJson)]
    public void AnalyzeResponse_ClassifiesFailures(string raw, MindmapFailureReason expected)
    {
        var result = MindmapService.AnalyzeResponse(raw, "Fallback");

        Assert.That(result.Root, Is.Null);
        Assert.That(result.FailureReason, Is.EqualTo(expected));
    }

    [Test]
    public async Task GenerateMindmapAsync_PassesRepairFlagAndFailureReason()
    {
        var truncated = new MindmapService(new RecordingLlm("[{\"title\": \"A\", \"children\": [{\"title\": \"B\"}, {\"title\": \"C"), _tempDir);
        var serviceError = new MindmapService(new RecordingLlm("Netzwerkfehler zu Mistral AI: Zeitüberschreitung"), _tempDir);

        var repaired = await truncated.GenerateMindmapAsync(new[] { "Text" }, "Titel");
        var failed = await serviceError.GenerateMindmapAsync(new[] { "Text" }, "Titel");
        var noContent = await serviceError.GenerateMindmapAsync(Array.Empty<string>(), "Titel");

        Assert.That(repaired.IsSuccess, Is.True);
        Assert.That(repaired.WasRepaired, Is.True);
        Assert.That(failed.FailureReason, Is.EqualTo(MindmapFailureReason.NoJson));
        Assert.That(noContent.FailureReason, Is.EqualTo(MindmapFailureReason.NoContent));
    }

    // ---------- Auswahl der Textstellen ----------

    [Test]
    public void SelectRepresentativeChunks_SpreadsOverWholeDocument()
    {
        var chunks = Enumerable.Range(0, 100).Select(i => $"Abschnitt {i}").ToList();

        var selected = MindmapService.SelectRepresentativeChunks(chunks, 5);

        Assert.That(selected, Is.EqualTo(new[] { "Abschnitt 0", "Abschnitt 25", "Abschnitt 50", "Abschnitt 74", "Abschnitt 99" }));
    }

    [Test]
    public void SelectRepresentativeChunks_FewChunks_ReturnsAllWithoutEmpty()
    {
        var selected = MindmapService.SelectRepresentativeChunks(new[] { "a", " ", "b" }, 16);

        Assert.That(selected, Is.EqualTo(new[] { "a", "b" }));
    }

    // ---------- Erzeugung ----------

    [Test]
    public async Task GenerateMindmapAsync_WithoutChunks_DoesNotCallLlm()
    {
        var llm = new RecordingLlm("[]");
        var service = new MindmapService(llm, _tempDir);

        var result = await service.GenerateMindmapAsync(new List<string>(), "Titel");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(llm.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GenerateMindmapAsync_SendsMindmapPromptAndLimitedChunks()
    {
        var llm = new RecordingLlm("""[{"title": "Wurzel", "children": [{"title": "Ast"}]}]""");
        var service = new MindmapService(llm, _tempDir);
        var chunks = Enumerable.Range(0, 40).Select(i => $"Text {i}").ToList();

        var result = await service.GenerateMindmapAsync(chunks, "Titel");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Root!.Title, Is.EqualTo("Wurzel"));
        Assert.That(llm.LastPrompt, Does.Contain("Mindmap"));
        Assert.That(llm.LastPrompt, Does.Contain("JSON"));
        Assert.That(llm.LastChunks, Has.Count.EqualTo(MindmapService.MaxChunks));
    }

    [Test]
    public async Task GenerateMindmapAsync_InvalidResponse_KeepsRawResponse()
    {
        var llm = new RecordingLlm("Gemini-Fehler: Es ist kein API-Schlüssel in den Einstellungen hinterlegt.");
        var service = new MindmapService(llm, _tempDir);

        var result = await service.GenerateMindmapAsync(new[] { "Text" }, "Titel");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.RawResponse, Does.Contain("API-Schlüssel"));
    }

    [Test]
    public async Task GenerateMindmapAsync_WithFakeLlmService_ReturnsMindmap()
    {
        var service = new MindmapService(new FakeLlmService(), _tempDir);

        var result = await service.GenerateMindmapAsync(new[] { "Erste Textstelle mit Inhalt", "Zweite Textstelle" }, "Titel");

        Assert.That(result.IsSuccess, Is.True);
    }

    // ---------- Speicherung ----------

    [Test]
    public async Task SaveAndLoad_RoundTrip_PreservesTree()
    {
        var service = new MindmapService(new RecordingLlm(""), _tempDir);
        var mindmap = new MindmapDocument
        {
            Root = new MindmapNode
            {
                Title = "Wurzel",
                Children = { new MindmapNode { Title = "Ast", Summary = "Erklärung" } }
            },
            SourceDocuments = { "skript.pdf" }
        };

        await service.SaveMindmapAsync("Testprofil", mindmap);
        var loaded = await service.LoadMindmapAsync("Testprofil");

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Root.Title, Is.EqualTo("Wurzel"));
        Assert.That(loaded.Root.Children[0].Summary, Is.EqualTo("Erklärung"));
        Assert.That(loaded.SourceDocuments, Is.EqualTo(new[] { "skript.pdf" }));
        Assert.That(File.Exists(Path.Combine(_tempDir, "Mindmaps", "Testprofil.json")), Is.True);
    }

    [Test]
    public async Task Load_MissingOrBrokenFile_ReturnsNull()
    {
        var service = new MindmapService(new RecordingLlm(""), _tempDir);
        Assert.That(await service.LoadMindmapAsync("Gibtsnicht"), Is.Null);

        Directory.CreateDirectory(Path.Combine(_tempDir, "Mindmaps"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Mindmaps", "Kaputt.json"), "{ kein json");
        Assert.That(await service.LoadMindmapAsync("Kaputt"), Is.Null);
    }

    [Test]
    public async Task Load_DuplicateIds_AreReplaced()
    {
        var service = new MindmapService(new RecordingLlm(""), _tempDir);
        var mindmap = new MindmapDocument
        {
            Root = new MindmapNode { Id = "x", Title = "Wurzel", Children = { new MindmapNode { Id = "x", Title = "Ast" } } }
        };
        await service.SaveMindmapAsync("Testprofil", mindmap);

        var loaded = await service.LoadMindmapAsync("Testprofil");

        Assert.That(loaded!.Root.Id, Is.Not.EqualTo(loaded.Root.Children[0].Id));
    }

    [Test]
    public async Task Delete_RemovesFile()
    {
        var service = new MindmapService(new RecordingLlm(""), _tempDir);
        await service.SaveMindmapAsync("Testprofil", new MindmapDocument { Root = new MindmapNode { Title = "W" } });

        await service.DeleteMindmapAsync("Testprofil");

        Assert.That(await service.LoadMindmapAsync("Testprofil"), Is.Null);
    }

    // ---------- Export ----------

    [Test]
    public void ExportToMarkdown_WritesHeadingsAndNestedList()
    {
        var service = new MindmapService(new RecordingLlm(""), _tempDir);
        var mindmap = new MindmapDocument
        {
            Root = new MindmapNode
            {
                Title = "Wurzel",
                Children =
                {
                    new MindmapNode
                    {
                        Title = "Ast",
                        Children = { new MindmapNode { Title = "Blatt", Summary = "Kurz", Children = { new MindmapNode { Title = "Detail" } } } }
                    }
                }
            }
        };

        var md = service.ExportToMarkdown(mindmap);

        Assert.That(md, Does.Contain("# Wurzel"));
        Assert.That(md, Does.Contain("## Ast"));
        Assert.That(md, Does.Contain("- **Blatt** – Kurz"));
        Assert.That(md, Does.Contain("  - **Detail**"));
    }

    /// <summary>Einfache Attrappe, die Aufrufe mitschreibt und eine feste Antwort liefert.</summary>
    private sealed class RecordingLlm : ILlmService
    {
        private readonly string _response;
        public RecordingLlm(string response) => _response = response;

        public int CallCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;
        public List<string> LastChunks { get; private set; } = new();

        public Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;
            LastChunks = contextChunks.ToList();
            return Task.FromResult(_response);
        }

        public Task<bool> ValidateConnectionAsync(AiConfig? config = null) => Task.FromResult(true);
    }
}
