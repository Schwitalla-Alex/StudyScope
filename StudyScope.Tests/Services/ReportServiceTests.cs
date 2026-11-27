using System.IO.Compression;
using System.Xml.Linq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Common;

namespace StudyScope.Tests.Services;

/// <summary>
/// Tests für den ReportService: Prompt je Vorlage, nachsichtige Auswertung der KI-Antwort,
/// Fake-KI, Speicherung je Profil und die Exporte (Word, Markdown, Text, HTML).
/// </summary>
[TestFixture]
public class ReportServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeReportTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---------- Prompt ----------

    [Test]
    public void BuildPrompt_StartsWithReportMarker_AndRequestsJson()
    {
        foreach (var template in ReportTemplates.All)
        {
            var prompt = ReportService.BuildPrompt(template, "Vergleiche A und B");
            Assert.That(prompt, Does.StartWith("Erstelle einen Bericht"));
            Assert.That(prompt, Does.Contain("JSON"));
            Assert.That(prompt, Does.Not.Contain("Mindmap"));
        }
    }

    [Test]
    public void BuildPrompt_TemplateSpecificInstructions()
    {
        Assert.That(ReportService.BuildPrompt(ReportTemplate.Glossary, ""), Does.Contain("Glossar"));
        Assert.That(ReportService.BuildPrompt(ReportTemplate.Faq, ""), Does.Contain("FAQ"));
        Assert.That(ReportService.BuildPrompt(ReportTemplate.Briefing, ""), Does.Contain("Briefing"));
        Assert.That(ReportService.BuildPrompt(ReportTemplate.Custom, "Vergleiche die Steuerarten"), Does.Contain("Vergleiche die Steuerarten"));
    }

    // ---------- Auswertung ----------

    [Test]
    public void AnalyzeResponse_FullStructure()
    {
        const string json = """
            [{"title": "Zellbiologie", "summary": "Überblick über die Zelle.",
              "sections": [
                {"heading": "Aufbau", "paragraphs": ["Die Zelle ist die kleinste Einheit."], "bullets": ["Membran", "Kern"],
                 "terms": [{"term": "Mitochondrium", "definition": "Kraftwerk der Zelle"}],
                 "faq": [{"question": "Was ist ein Ribosom?", "answer": "Ort der Proteinsynthese"}],
                 "takeaway": "Ohne Zelle kein Leben."}
              ]}]
            """;

        var result = ReportService.AnalyzeResponse(json, "Ersatztitel");

        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.None));
        var report = result.Value!;
        Assert.That(report.Title, Is.EqualTo("Zellbiologie"));
        Assert.That(report.Summary, Is.EqualTo("Überblick über die Zelle."));
        Assert.That(report.Sections, Has.Count.EqualTo(1));
        var section = report.Sections[0];
        Assert.That(section.Heading, Is.EqualTo("Aufbau"));
        Assert.That(section.Paragraphs, Has.Count.EqualTo(1));
        Assert.That(section.Bullets, Is.EqualTo(new[] { "Membran", "Kern" }));
        Assert.That(section.Terms[0].Term, Is.EqualTo("Mitochondrium"));
        Assert.That(section.Terms[0].Definition, Is.EqualTo("Kraftwerk der Zelle"));
        Assert.That(section.Questions[0].Answer, Is.EqualTo("Ort der Proteinsynthese"));
        Assert.That(section.Takeaway, Is.EqualTo("Ohne Zelle kein Leben."));
    }

    [Test]
    public void AnalyzeResponse_GermanKeysAndCodeFence()
    {
        const string raw = """
            Hier ist der Bericht:
            ```json
            {"titel": "Steuern", "zusammenfassung": "Kurz gesagt.",
             "abschnitte": [{"ueberschrift": "Arten", "absaetze": ["Es gibt direkte und indirekte Steuern."], "stichpunkte": ["Einkommensteuer"], "merksatz": "Steuern finanzieren den Staat."}]}
            ```
            """;

        var report = ReportService.AnalyzeResponse(raw, "Ersatz").Value!;

        Assert.That(report.Title, Is.EqualTo("Steuern"));
        Assert.That(report.Sections[0].Heading, Is.EqualTo("Arten"));
        Assert.That(report.Sections[0].Bullets, Is.EqualTo(new[] { "Einkommensteuer" }));
        Assert.That(report.Sections[0].Takeaway, Is.EqualTo("Steuern finanzieren den Staat."));
    }

    [Test]
    public void AnalyzeResponse_TopLevelGlossaryWithoutSections()
    {
        const string json = """[{"title": "Glossar", "terms": {"Zelle": "kleinste Einheit", "Gewebe": "Verband gleichartiger Zellen"}}]""";

        var report = ReportService.AnalyzeResponse(json, "Ersatz").Value!;

        var terms = report.Sections.SelectMany(s => s.Terms).ToList();
        Assert.That(terms, Has.Count.EqualTo(2));
        Assert.That(terms.Select(t => t.Term), Is.EquivalentTo(new[] { "Zelle", "Gewebe" }));
    }

    [Test]
    public void AnalyzeResponse_TermsAsStrings()
    {
        const string json = """[{"title": "G", "sections": [{"heading": "Begriffe", "terms": ["Zelle: kleinste Einheit", "Organ: Verbund von Geweben"]}]}]""";

        var terms = ReportService.AnalyzeResponse(json, "Ersatz").Value!.Sections[0].Terms;

        Assert.That(terms, Has.Count.EqualTo(2));
        Assert.That(terms[0].Term, Is.EqualTo("Zelle"));
        Assert.That(terms[0].Definition, Is.EqualTo("kleinste Einheit"));
    }

    [Test]
    public void AnalyzeResponse_MissingTitle_UsesFallback()
    {
        const string json = """[{"sections": [{"heading": "A", "paragraphs": ["Text"]}]}]""";

        Assert.That(ReportService.AnalyzeResponse(json, "Lernübersicht: Skript").Value!.Title, Is.EqualTo("Lernübersicht: Skript"));
    }

    [Test]
    public void AnalyzeResponse_TruncatedJson_IsRepaired()
    {
        const string raw = """[{"title": "Abgeschnitten", "sections": [{"heading": "Eins", "paragraphs": ["Vollständig."]}, {"heading": "Zwei", "paragraphs": ["Hier bricht die Ant""";

        var result = ReportService.AnalyzeResponse(raw, "Ersatz");

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.WasRepaired, Is.True);
        Assert.That(result.Value!.Sections[0].Heading, Is.EqualTo("Eins"));
    }

    [Test]
    public void AnalyzeResponse_Failures()
    {
        Assert.That(ReportService.AnalyzeResponse("", "x").FailureReason, Is.EqualTo(MindmapFailureReason.EmptyResponse));
        Assert.That(ReportService.AnalyzeResponse("Ich kann das leider nicht.", "x").FailureReason, Is.EqualTo(MindmapFailureReason.NoJson));
        Assert.That(ReportService.AnalyzeResponse("[{\"title\": \"Leer\"}]", "x").Value, Is.Null);
    }

    [Test]
    public void AnalyzeResponse_LimitsSectionsAndItems()
    {
        var sections = string.Join(",", Enumerable.Range(1, 30).Select(i =>
            $"{{\"heading\": \"Abschnitt {i}\", \"bullets\": [{string.Join(",", Enumerable.Range(1, 40).Select(b => $"\"Punkt {b}\""))}]}}"));
        var report = ReportService.AnalyzeResponse($"[{{\"title\": \"Lang\", \"sections\": [{sections}]}}]", "x").Value!;

        Assert.That(report.Sections, Has.Count.EqualTo(ReportService.MaxSections));
        Assert.That(report.Sections[0].Bullets, Has.Count.EqualTo(ReportService.MaxItemsPerList));
    }

    // ---------- Erzeugung ----------

    [Test]
    public async Task GenerateReportAsync_WithFakeAi_ProducesReport()
    {
        var service = new ReportService(new FakeLlmService(), _tempDir);

        var result = await service.GenerateReportAsync(
            new[] { "Die Zelle ist die kleinste Einheit des Lebens.", "Mitochondrien liefern Energie." },
            ReportTemplate.Glossary, string.Empty, "Ersatz");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Report!.Template, Is.EqualTo(ReportTemplate.Glossary));
        Assert.That(result.Report.Sections, Is.Not.Empty);
    }

    [Test]
    public async Task GenerateReportAsync_NoChunks_DoesNotCallAi()
    {
        var llm = new FixedLlm("[]");
        var service = new ReportService(llm, _tempDir);

        var result = await service.GenerateReportAsync(Array.Empty<string>(), ReportTemplate.StudyGuide, "", "x");

        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.NoContent));
        Assert.That(llm.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GenerateReportAsync_Custom_KeepsInstruction()
    {
        var llm = new FixedLlm("""[{"title": "T", "sections": [{"heading": "H", "paragraphs": ["P"]}]}]""");
        var service = new ReportService(llm, _tempDir);

        var result = await service.GenerateReportAsync(new[] { "Text" }, ReportTemplate.Custom, "  Nur Vorteile nennen  ", "x");

        Assert.That(result.Report!.CustomInstruction, Is.EqualTo("Nur Vorteile nennen"));
        Assert.That(llm.LastPrompt, Does.Contain("Nur Vorteile nennen"));
    }

    // ---------- Speicherung ----------

    [Test]
    public async Task SaveAndLoad_RoundTrip_PerProfile()
    {
        var service = new ReportService(new FixedLlm(""), _tempDir);
        var library = new ReportLibrary();
        library.Reports.Add(SampleReport());

        await service.SaveLibraryAsync("Testprofil", library);
        var loaded = await service.LoadLibraryAsync("Testprofil");
        var other = await service.LoadLibraryAsync("Anderes Profil");

        Assert.That(File.Exists(Path.Combine(_tempDir, "Reports", "Testprofil.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempDir, "Reports", "Testprofil.json.tmp")), Is.False);
        Assert.That(loaded.Reports, Has.Count.EqualTo(1));
        Assert.That(loaded.Reports[0].Sections[0].Terms[0].Term, Is.EqualTo("Zelle"));
        Assert.That(loaded.Reports[0].Embeds.Quiz, Is.False);
        Assert.That(other.Reports, Is.Empty);
    }

    [Test]
    public async Task Load_BrokenFile_ReturnsEmptyLibrary()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Reports"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Reports", "Testprofil.json"), "{ kaputt");

        var library = await new ReportService(new FixedLlm(""), _tempDir).LoadLibraryAsync("Testprofil");

        Assert.That(library.Reports, Is.Empty);
    }

    // ---------- Exporte ----------

    [Test]
    public async Task ExportToDocx_IsValidWordDocument()
    {
        var service = new ReportService(new FixedLlm(""), _tempDir);
        var path = Path.Combine(_tempDir, "Bericht.docx");

        await service.ExportToDocxAsync(SampleReport(), SampleStudio(), path, TestCultures.De);

        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.That(names, Does.Contain("[Content_Types].xml"));
        Assert.That(names, Does.Contain("word/document.xml"));
        Assert.That(names, Does.Contain("word/styles.xml"));

        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
        {
            using var stream = entry.Open();
            Assert.DoesNotThrow(() => XDocument.Load(stream), entry.FullName);
        }

        using var docStream = zip.GetEntry("word/document.xml")!.Open();
        var text = string.Concat(XDocument.Load(docStream).Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value));
        Assert.That(text, Does.Contain("Zellbiologie"));
        Assert.That(text, Does.Contain("Mitochondrium"));
        Assert.That(text, Does.Contain("Kraftwerk"));          // Karteikarte (eingebettet)
        Assert.That(text, Does.Not.Contain("Welche Organelle")); // Quiz ist abgewählt
    }

    [Test]
    public void ExportToMarkdown_ContainsReportAndEmbeds()
    {
        var service = new ReportService(new FixedLlm(""), _tempDir);

        var md = service.ExportToMarkdown(SampleReport(), SampleStudio(), TestCultures.De);

        Assert.That(md, Does.StartWith("# Zellbiologie"));
        Assert.That(md, Does.Contain("## Aufbau"));
        Assert.That(md, Does.Contain("**Mitochondrium:** Kraftwerk der Zelle"));
        Assert.That(md, Does.Contain("> 💡 **Merksatz:**"));
        Assert.That(md, Does.Contain("## Mindmap"));
        Assert.That(md, Does.Contain("## Karteikarten (1)"));
        Assert.That(md, Does.Not.Contain("## Selbsttest"));
    }

    [Test]
    public void ExportToPlainText_HasNoMarkdownSyntax()
    {
        var service = new ReportService(new FixedLlm(""), _tempDir);
        var report = SampleReport();
        report.Embeds.Quiz = true;

        var text = service.ExportToPlainText(report, SampleStudio(), TestCultures.De);

        Assert.That(text, Does.StartWith("ZELLBIOLOGIE"));
        Assert.That(text, Does.Not.Contain("**"));
        Assert.That(text, Does.Contain("Selbsttest"));
        Assert.That(text, Does.Contain("Lösungen: 1. B"));
    }

    [Test]
    public void ExportToHtml_IsSelfContainedAndEscaped()
    {
        var service = new ReportService(new FixedLlm(""), _tempDir);
        var report = SampleReport();
        report.Title = "Zellen <script>alert(1)</script>";
        report.Embeds.Quiz = true;

        var html = service.ExportToHtml(report, SampleStudio(), TestCultures.De);

        Assert.That(html, Does.StartWith("<!DOCTYPE html>"));
        Assert.That(html, Does.Not.Contain("<script>alert(1)</script>"));
        Assert.That(html, Does.Contain("&lt;script&gt;"));
        Assert.That(html, Does.Not.Contain("src=\"http"));
        Assert.That(html, Does.Contain("Welche Organelle"));
        Assert.That(html, Does.Contain("data-correct"));
    }

    // ---------- Testdaten ----------

    private static Report SampleReport() => new()
    {
        Title = "Zellbiologie",
        Summary = "Überblick über die Zelle.",
        Template = ReportTemplate.StudyGuide,
        SourceDocuments = new List<string> { "skript.txt" },
        Embeds = new ReportEmbeds { Mindmap = true, Flashcards = true, Quiz = false, Presentation = true },
        Sections = new List<ReportSection>
        {
            new()
            {
                Heading = "Aufbau",
                Paragraphs = new List<string> { "Die Zelle ist die kleinste Einheit des Lebens." },
                Bullets = new List<string> { "Membran", "Kern" },
                Terms = new List<ReportTerm> { new() { Term = "Zelle", Definition = "kleinste Einheit" }, new() { Term = "Mitochondrium", Definition = "Kraftwerk der Zelle" } },
                Questions = new List<ReportQuestion> { new() { Question = "Was ist ein Ribosom?", Answer = "Ort der Proteinsynthese" } },
                Takeaway = "Ohne Zelle kein Leben."
            }
        }
    };

    private static ReportStudioContent SampleStudio() => new()
    {
        Mindmap = new MindmapDocument
        {
            Root = new MindmapNode { Title = "Zelle", Children = new List<MindmapNode> { new() { Title = "Organellen", Summary = "Funktionseinheiten" } } }
        },
        Flashcards = new List<Flashcard> { new() { Question = "Was ist das Mitochondrium?", Answer = "Das Kraftwerk der Zelle", SourceDocument = "skript.txt" } },
        QuizQuestions = new List<QuizQuestion>
        {
            new() { Type = QuizQuestionType.SingleChoice, Text = "Welche Organelle liefert Energie?", Options = new List<string> { "Ribosom", "Mitochondrium" }, CorrectIndices = new List<int> { 1 } }
        },
        Presentation = new PresentationDocument
        {
            Title = "Zellen",
            Slides = new List<PresentationSlide> { new() { Title = "Überblick", Bullets = new List<string> { "Aufbau" } } }
        }
    };

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
