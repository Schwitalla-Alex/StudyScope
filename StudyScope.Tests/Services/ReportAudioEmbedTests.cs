using System.IO.Compression;
using System.Xml.Linq;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Common;

namespace StudyScope.Tests.Services;

/// <summary>
/// Audio-Skripte als Einbettung in Berichten: ältere Berichte ohne Eigenschaft bleiben unverändert, Filter, Exporte
/// (Word/Markdown/Text als Abschnitt, HTML einklappbar) mit „Audiodatei: …“ statt eingebetteter Datei.
/// </summary>
[TestFixture]
public class ReportAudioEmbedTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeReportAudio_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ReportService Service() => new(new FakeLlmService(), _tempDir);

    private static AudioSummary Podcast(string audioFile = "Zellen_1234abcd.wav") => new()
    {
        Title = "Zellen im Gespräch",
        Format = AudioScriptFormat.Podcast,
        AudioFileName = audioFile,
        Segments = new List<AudioSegment>
        {
            new() { Speaker = "Moderatorin", Text = "Was ist eine Zelle?" },
            new() { Speaker = "Experte", Text = "Die kleinste Einheit des Lebens." }
        }
    };

    private static Report Report(bool? audio) => new()
    {
        Title = "Biologie",
        Template = ReportTemplate.Briefing,
        Embeds = new ReportEmbeds { Mindmap = false, Flashcards = false, Quiz = false, Presentation = false, DataTables = false, AudioScripts = audio },
        Sections = new List<ReportSection> { new() { Heading = "Überblick", Paragraphs = new List<string> { "Text." } } }
    };

    private static ReportStudioContent Studio(AudioSummary audio) => new() { AudioScripts = new[] { audio } };

    [Test]
    public async Task Load_OldReportWithoutProperty_DoesNotEmbedAudio()
    {
        var folder = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Testprofil.json"), """
            {"Reports": [
              {"Id": "alt", "Title": "Alt", "Sections": [{"Heading": "A", "Paragraphs": ["x"]}],
               "Embeds": {"Mindmap": true, "Flashcards": true, "Quiz": true, "Presentation": true, "DataTables": true}},
              {"Id": "neu", "Title": "Neu", "Sections": [{"Heading": "A", "Paragraphs": ["x"]}],
               "Embeds": {"Mindmap": true, "DataTables": true, "AudioScripts": true}}
            ]}
            """);

        var library = await Service().LoadLibraryAsync("Testprofil");

        Assert.That(library.Reports.Single(r => r.Id == "alt").Embeds.AudioScripts, Is.False);
        Assert.That(library.Reports.Single(r => r.Id == "alt").Embeds.DataTables, Is.True, "andere Einstellungen bleiben");
        Assert.That(library.Reports.Single(r => r.Id == "neu").Embeds.AudioScripts, Is.True);
    }

    [Test]
    public void Filter_And_ExportsOfOldReports_Unchanged()
    {
        var studio = Studio(Podcast());
        Assert.That(studio.Filter(new ReportEmbeds()).AudioScripts, Is.Empty, "fehlende Angabe = älterer Bericht");
        Assert.That(studio.Filter(new ReportEmbeds { AudioScripts = true }).AudioScripts, Has.Count.EqualTo(1));
        Assert.That(studio.HasAny, Is.True);

        var service = Service();
        foreach (var report in new[] { Report(null), Report(false) })
        {
            Assert.That(service.ExportToMarkdown(report, studio, TestCultures.De), Is.EqualTo(service.ExportToMarkdown(report, ReportStudioContent.Empty, TestCultures.De)));
            Assert.That(service.ExportToPlainText(report, studio, TestCultures.De), Is.EqualTo(service.ExportToPlainText(report, ReportStudioContent.Empty, TestCultures.De)));
            Assert.That(service.ExportToHtml(report, studio, TestCultures.De), Is.EqualTo(service.ExportToHtml(report, ReportStudioContent.Empty, TestCultures.De)));
        }
    }

    [Test]
    public void TextExports_ContainScriptSection_WithAudioFileName()
    {
        var service = Service();

        var md = service.ExportToMarkdown(Report(true), Studio(Podcast()), TestCultures.De);
        var text = service.ExportToPlainText(Report(true), Studio(Podcast()), TestCultures.De);

        Assert.That(md, Does.Contain("## 🎧 Zellen im Gespräch").And.Contain("**Experte:** Die kleinste Einheit des Lebens.")
            .And.Contain("Audiodatei: Zellen_1234abcd.wav").And.Contain("Podcast-Gespräch"));
        Assert.That(text, Does.Contain("Audio-Skript – Zellen im Gespräch").And.Contain("Moderatorin: Was ist eine Zelle?")
            .And.Contain("Audiodatei: Zellen_1234abcd.wav"));
    }

    [Test]
    public void TextExport_WithoutAudioFile_HasNoFileLine_SingleSpeakerWithoutNames()
    {
        var audio = new AudioSummary
        {
            Title = "Kurz erklärt",
            Segments = new List<AudioSegment> { new() { Speaker = "Sprecherin", Text = "Nur ein Sprecher." } }
        };

        var md = Service().ExportToMarkdown(Report(true), Studio(audio), TestCultures.De);

        Assert.That(md, Does.Contain("Nur ein Sprecher.").And.Not.Contain("Audiodatei:").And.Not.Contain("**Sprecherin:**"));
    }

    [Test]
    public void Html_ScriptIsCollapsible_AndAudioNotEmbedded()
    {
        var html = Service().ExportToHtml(Report(true), Studio(Podcast()), TestCultures.De);

        Assert.That(html, Does.Contain("<details class=\"audio-script\">").And.Contain("id=\"audio\"")
            .And.Contain("Audiodatei: Zellen_1234abcd.wav").And.Contain("🎧 Audio-Skript"));
        Assert.That(html, Does.Not.Contain("<audio").And.Not.Contain("data:audio"));
    }

    [Test]
    public async Task Docx_ContainsScriptSection()
    {
        var path = Path.Combine(_tempDir, "bericht.docx");

        await Service().ExportToDocxAsync(Report(true), Studio(Podcast()), path, TestCultures.De);

        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        var doc = XDocument.Load(stream);
        var text = string.Concat(doc.Descendants(W + "t").Select(t => t.Value));
        Assert.That(text, Does.Contain("Audio-Skript – Zellen im Gespräch").And.Contain("Experte: Die kleinste Einheit des Lebens.")
            .And.Contain("Audiodatei: Zellen_1234abcd.wav"));
        Assert.That(zip.Entries.Any(e => e.FullName.EndsWith(".wav")), Is.False);
    }
}
