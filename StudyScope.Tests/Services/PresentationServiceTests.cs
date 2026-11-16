using System.IO.Compression;
using System.Xml.Linq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Tests für die Präsentation: Auswertung der KI-Antwort, Ableitung aus der Mindmap,
/// Speicherung je Profil und PowerPoint-Export.
/// </summary>
[TestFixture]
public class PresentationServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopePresentationTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PresentationService CreateService(string response = "") => new(new FixedLlm(response), _tempDir);

    // ---------- Auswertung der KI-Antwort ----------

    [Test]
    public void AnalyzeResponse_ValidJson_CreatesTitleAndContentSlides()
    {
        const string json = """
            [{"title": "Zellbiologie", "subtitle": "Grundlagen", "slides": [
                {"title": "Aufbau", "bullets": ["Zellkern", "Membran"], "notes": "Hier erkläre ich den Aufbau."},
                {"title": "Fazit", "bullets": ["Zelle = kleinste Einheit"]}
            ]}]
            """;

        var result = PresentationService.AnalyzeResponse(json, "Fallback");

        Assert.That(result.Value, Is.Not.Null);
        var slides = result.Value!.Slides;
        Assert.That(slides, Has.Count.EqualTo(3));
        Assert.That(slides[0].Kind, Is.EqualTo(SlideKind.Title));
        Assert.That(slides[0].Title, Is.EqualTo("Zellbiologie"));
        Assert.That(slides[0].Subtitle, Is.EqualTo("Grundlagen"));
        Assert.That(slides[1].Bullets, Is.EqualTo(new[] { "Zellkern", "Membran" }));
        Assert.That(slides[1].Notes, Is.EqualTo("Hier erkläre ich den Aufbau."));
        Assert.That(result.Value.Origin, Is.EqualTo(PresentationOrigin.Ai));
    }

    [Test]
    public void AnalyzeResponse_GermanKeysAndBulletsAsText_AreAccepted()
    {
        const string json = """
            {"Titel": "Steuerrecht", "Folien": [
                {"Titel": "Einkommensteuer", "Stichpunkte": "- Progressiver Tarif\n- Grundfreibetrag\n• Splitting", "Sprechernotizen": "Kurz erklären."}
            ]}
            """;

        var result = PresentationService.AnalyzeResponse(json, "Fallback");

        Assert.That(result.Value!.Title, Is.EqualTo("Steuerrecht"));
        Assert.That(result.Value.Slides[1].Bullets, Is.EqualTo(new[] { "Progressiver Tarif", "Grundfreibetrag", "Splitting" }));
        Assert.That(result.Value.Slides[1].Notes, Is.EqualTo("Kurz erklären."));
    }

    [Test]
    public void AnalyzeResponse_PlainSlideList_UsesFallbackTitle()
    {
        const string json = """[{"title": "Folie A", "bullets": ["x"]}, {"title": "Folie B", "bullets": ["y"]}]""";

        var result = PresentationService.AnalyzeResponse(json, "Skript Biologie");

        Assert.That(result.Value!.Title, Is.EqualTo("Skript Biologie"));
        Assert.That(result.Value.Slides.Select(s => s.Title), Is.EqualTo(new[] { "Skript Biologie", "Folie A", "Folie B" }));
    }

    [Test]
    public void AnalyzeResponse_TruncatedResponse_IsRepaired()
    {
        const string raw = "[{\"title\": \"Vortrag\", \"slides\": [{\"title\": \"Eins\", \"bullets\": [\"a\", \"b\"]}, {\"title\": \"Zwei\", \"bullets\": [\"c\", \"d";

        var result = PresentationService.AnalyzeResponse(raw, "Fallback");

        Assert.That(result.WasRepaired, Is.True);
        Assert.That(result.Value!.Slides.Select(s => s.Title), Is.EqualTo(new[] { "Vortrag", "Eins" }));
    }

    [Test]
    public void AnalyzeResponse_TooManyBullets_AreLimited()
    {
        var bullets = string.Join(",", Enumerable.Range(1, 12).Select(i => $"\"Punkt {i}\""));
        var json = $"[{{\"title\": \"T\", \"slides\": [{{\"title\": \"Viel\", \"bullets\": [{bullets}]}}]}}]";

        var result = PresentationService.AnalyzeResponse(json, "Fallback");

        Assert.That(result.Value!.Slides[1].Bullets, Has.Count.EqualTo(PresentationService.MaxBulletsPerSlide));
    }

    [TestCase("", MindmapFailureReason.EmptyResponse)]
    [TestCase("Netzwerkfehler zu Google Gemini", MindmapFailureReason.NoJson)]
    [TestCase("[{\"title\": \"Ohne Folien\", \"slides\": []}]", MindmapFailureReason.NoTopics)]
    [TestCase("[{\"title\": ", MindmapFailureReason.InvalidJson)]
    public void AnalyzeResponse_ClassifiesFailures(string raw, MindmapFailureReason expected)
    {
        var result = PresentationService.AnalyzeResponse(raw, "Fallback");

        Assert.That(result.Value, Is.Null);
        Assert.That(result.FailureReason, Is.EqualTo(expected));
    }

    // ---------- Erzeugung per KI ----------

    [Test]
    public async Task GenerateFromDocumentsAsync_WithoutChunks_DoesNotCallLlm()
    {
        var llm = new FixedLlm("[]");
        var service = new PresentationService(llm, _tempDir);

        var result = await service.GenerateFromDocumentsAsync(Array.Empty<string>(), "T", 8);

        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.NoContent));
        Assert.That(llm.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GenerateFromDocumentsAsync_PromptContainsClampedSlideCountAndNoMindmapKeyword()
    {
        var llm = new FixedLlm("""[{"title": "T", "slides": [{"title": "A", "bullets": ["x"]}]}]""");
        var service = new PresentationService(llm, _tempDir);

        var result = await service.GenerateFromDocumentsAsync(new[] { "Text" }, "T", 99);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(llm.LastPrompt, Does.Contain($"genau {PresentationService.MaxSlides} Inhaltsfolien"));
        Assert.That(llm.LastPrompt, Does.Contain("JSON"));
        Assert.That(llm.LastPrompt, Does.Not.Contain("Mindmap"));
    }

    [Test]
    public async Task GenerateFromDocumentsAsync_WithFakeLlmService_ReturnsPresentation()
    {
        var service = new PresentationService(new FakeLlmService(), _tempDir);

        var result = await service.GenerateFromDocumentsAsync(new[] { "Die Zelle ist die kleinste Einheit des Lebens. Mitochondrien liefern Energie." }, "T", 5);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Presentation!.Slides.Last().Title, Is.EqualTo("Fazit"));
    }

    [Test]
    public void FakePresentationResponse_DetectsOnlyRealPresentationPrompts()
    {
        Assert.That(FakePresentationResponse.IsPresentationPrompt("Erstelle eine Präsentation mit genau 5 Inhaltsfolien ... JSON-Array"), Is.True);
        Assert.That(FakePresentationResponse.IsPresentationPrompt("Was steht in der Präsentation über Zellen?"), Is.False);
    }

    // ---------- Ableitung aus der Mindmap ----------

    private static MindmapDocument CreateMindmap(int branchChildren = 2) => new()
    {
        Root = new MindmapNode
        {
            Title = "Zellbiologie",
            Summary = "Aufbau der Zelle.",
            Children =
            {
                new MindmapNode
                {
                    Title = "Organellen",
                    Summary = "Spezialisierte Strukturen.",
                    Children = Enumerable.Range(1, branchChildren)
                        .Select(i => new MindmapNode { Title = $"Organell {i}", Summary = $"Aufgabe {i}.", Children = { new MindmapNode { Title = $"Detail {i}" } } })
                        .ToList()
                },
                new MindmapNode { Title = "Zellteilung", Summary = "Mitose und Meiose." }
            }
        },
        SourceDocuments = { "skript.pdf" }
    };

    [Test]
    public void CreateFromMindmap_BuildsTitleOverviewBranchAndSummarySlides()
    {
        var presentation = CreateService().CreateFromMindmap(CreateMindmap());

        Assert.That(presentation.Origin, Is.EqualTo(PresentationOrigin.Mindmap));
        Assert.That(presentation.SourceDocuments, Is.EqualTo(new[] { "skript.pdf" }));
        Assert.That(presentation.Slides.Select(s => s.Title),
            Is.EqualTo(new[] { "Zellbiologie", "Überblick", "Organellen", "Zellteilung", "Zusammenfassung" }));

        var title = presentation.Slides[0];
        Assert.That(title.Kind, Is.EqualTo(SlideKind.Title));
        Assert.That(title.Subtitle, Is.EqualTo("Aufbau der Zelle."));

        var organellen = presentation.Slides[2];
        Assert.That(organellen.Bullets, Is.EqualTo(new[] { "Organell 1: Aufgabe 1.", "Organell 2: Aufgabe 2." }));
        Assert.That(organellen.Notes, Does.Contain("Spezialisierte Strukturen.").And.Contain("Detail 1"));

        // Ast ohne Unterthemen: Erklärung als einziger Stichpunkt
        Assert.That(presentation.Slides[3].Bullets, Is.EqualTo(new[] { "Mitose und Meiose." }));
    }

    [Test]
    public void CreateFromMindmap_ManySubtopics_AreSplitIntoContinuationSlides()
    {
        var presentation = CreateService().CreateFromMindmap(CreateMindmap(branchChildren: 9));

        var organellenSlides = presentation.Slides.Where(s => s.Title.StartsWith("Organellen")).ToList();
        Assert.That(organellenSlides, Has.Count.EqualTo(2));
        Assert.That(organellenSlides[0].Bullets, Has.Count.EqualTo(PresentationService.MaxBulletsPerSlide));
        Assert.That(organellenSlides[1].Title, Is.EqualTo("Organellen (Fortsetzung)"));
        Assert.That(organellenSlides[1].Bullets, Has.Count.EqualTo(3));
    }

    // ---------- Speicherung ----------

    [Test]
    public async Task SaveLoadDelete_RoundTrip()
    {
        var service = CreateService();
        var presentation = service.CreateFromMindmap(CreateMindmap());

        await service.SavePresentationAsync("Testprofil", presentation);
        var loaded = await service.LoadPresentationAsync("Testprofil");

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Slides.Select(s => s.Title), Is.EqualTo(presentation.Slides.Select(s => s.Title)));
        Assert.That(File.Exists(Path.Combine(_tempDir, "Presentations", "Testprofil.json")), Is.True);

        await service.DeletePresentationAsync("Testprofil");
        Assert.That(await service.LoadPresentationAsync("Testprofil"), Is.Null);
    }

    [Test]
    public async Task Load_BrokenFile_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Presentations"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Presentations", "Kaputt.json"), "{ kein json");

        Assert.That(await CreateService().LoadPresentationAsync("Kaputt"), Is.Null);
    }

    // ---------- PowerPoint-Export ----------

    [Test]
    public async Task ExportToPptxAsync_WritesValidPackageWithSlidesAndNotes()
    {
        var service = CreateService();
        var presentation = service.CreateFromMindmap(CreateMindmap());
        presentation.Slides[2].Bullets.Add("Sonderzeichen & <Klammern> \"Zitat\" – Emoji 🔬");
        var path = Path.Combine(_tempDir, "test.pptx");

        await service.ExportToPptxAsync(presentation, path);

        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();
        Assert.That(names.Contains("[Content_Types].xml"), Is.True);
        Assert.That(names.Contains("ppt/presentation.xml"), Is.True);
        Assert.That(names.Contains("ppt/notesMasters/notesMaster1.xml"), Is.True);
        for (var i = 1; i <= presentation.Slides.Count; i++)
        {
            Assert.That(names.Contains($"ppt/slides/slide{i}.xml"), Is.True);
            Assert.That(names.Contains($"ppt/notesSlides/notesSlide{i}.xml"), Is.True);
        }

        // Jeder XML-Teil muss wohlgeformt sein
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
        {
            using var stream = entry.Open();
            Assert.DoesNotThrow(() => XDocument.Load(stream), entry.FullName);
        }

        var presentationXml = Read(zip, "ppt/presentation.xml");
        Assert.That(presentationXml, Does.Contain("cx=\"12192000\" cy=\"6858000\""));
        Assert.That(System.Text.RegularExpressions.Regex.Matches(presentationXml, "<p:sldId ").Count, Is.EqualTo(presentation.Slides.Count));

        var slide3 = XDocument.Parse(Read(zip, "ppt/slides/slide3.xml"));
        var texts = slide3.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value).ToList();
        Assert.That(texts, Does.Contain("Organellen"));
        Assert.That(texts, Does.Contain("Sonderzeichen & <Klammern> \"Zitat\" – Emoji 🔬"));

        var notes3 = Read(zip, "ppt/notesSlides/notesSlide3.xml");
        Assert.That(notes3, Does.Contain("Spezialisierte Strukturen."));
    }

    [Test]
    public void FitBulletSize_ShrinksForLongContent()
    {
        var shortBullets = new[] { "Kurz", "Auch kurz" };
        var longBullets = Enumerable.Repeat(new string('x', 180), 6).ToArray();

        Assert.That(PptxWriter.FitBulletSize(shortBullets), Is.EqualTo(2800));
        Assert.That(PptxWriter.FitBulletSize(longBullets), Is.LessThan(2000));
    }

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
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
