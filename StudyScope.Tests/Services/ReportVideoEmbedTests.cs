using System.IO.Compression;
using System.Xml.Linq;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Video-Skripte als Einbettung in Berichten: ältere Berichte ohne Eigenschaft bleiben unverändert, Filter, Exporte
/// (Word/Markdown/Text als Abschnitt je Folie, HTML einklappbar) mit „Video: Titel“ statt eingebetteter Folien/Audio.
/// </summary>
[TestFixture]
public class ReportVideoEmbedTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeReportVideo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ReportService Service() => new(new FakeLlmService(), _tempDir);

    internal static VideoDocument Video(string title = "Zellen erklärt")
    {
        var video = new VideoDocument
        {
            Title = title,
            Slides = new List<PresentationSlide>
            {
                new() { Kind = SlideKind.Title, Title = "Die Zelle" },
                new() { Title = "Aufbau", Bullets = new List<string> { "Zellkern", "Membran" } },
                new() { Title = "Ohne Ton" }
            }
        };
        video.Scenes = new List<VideoScene>
        {
            new() { SlideId = video.Slides[0].Id, Narration = "Willkommen zur Zelle." },
            new() { SlideId = video.Slides[1].Id, Narration = "Jede Zelle hat einen Kern und eine Membran." },
            new() { SlideId = video.Slides[2].Id, Narration = "" }
        };
        return video;
    }

    private static Report Report(bool? video) => new()
    {
        Title = "Biologie",
        Template = ReportTemplate.Briefing,
        Embeds = new ReportEmbeds { Mindmap = false, Flashcards = false, Quiz = false, Presentation = false, DataTables = false, AudioScripts = false, VideoScripts = video },
        Sections = new List<ReportSection> { new() { Heading = "Überblick", Paragraphs = new List<string> { "Text." } } }
    };

    private static ReportStudioContent Studio(VideoDocument video) => new() { VideoScripts = new[] { video } };

    [Test]
    public async Task Load_OldReportWithoutProperty_DoesNotEmbedVideo()
    {
        var folder = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Testprofil.json"), """
            {"Reports": [
              {"Id": "alt", "Title": "Alt", "Sections": [{"Heading": "A", "Paragraphs": ["x"]}],
               "Embeds": {"Mindmap": true, "DataTables": true, "AudioScripts": true}},
              {"Id": "neu", "Title": "Neu", "Sections": [{"Heading": "A", "Paragraphs": ["x"]}],
               "Embeds": {"Mindmap": true, "DataTables": true, "AudioScripts": true, "VideoScripts": true}}
            ]}
            """);

        var library = await Service().LoadLibraryAsync("Testprofil");

        Assert.That(library.Reports.Single(r => r.Id == "alt").Embeds.VideoScripts, Is.False);
        Assert.That(library.Reports.Single(r => r.Id == "alt").Embeds.AudioScripts, Is.True, "andere Einstellungen bleiben");
        Assert.That(library.Reports.Single(r => r.Id == "neu").Embeds.VideoScripts, Is.True);
    }

    [Test]
    public void Filter_And_ExportsOfOldReports_Unchanged()
    {
        var studio = Studio(Video());
        Assert.That(studio.Filter(new ReportEmbeds()).VideoScripts, Is.Empty, "fehlende Angabe = älterer Bericht");
        Assert.That(studio.Filter(new ReportEmbeds { VideoScripts = true }).VideoScripts, Has.Count.EqualTo(1));
        Assert.That(studio.HasAny, Is.True);

        var service = Service();
        foreach (var report in new[] { Report(null), Report(false) })
        {
            Assert.That(service.ExportToMarkdown(report, studio), Is.EqualTo(service.ExportToMarkdown(report, ReportStudioContent.Empty)));
            Assert.That(service.ExportToPlainText(report, studio), Is.EqualTo(service.ExportToPlainText(report, ReportStudioContent.Empty)));
            Assert.That(service.ExportToHtml(report, studio), Is.EqualTo(service.ExportToHtml(report, ReportStudioContent.Empty)));
        }
    }

    [Test]
    public void VideoWithoutAnyNarration_IsNotEmbedded()
    {
        var video = Video();
        foreach (var scene in video.Scenes) scene.Narration = "  ";
        var studio = Studio(video);

        Assert.That(studio.HasAny, Is.False);
        var service = Service();
        Assert.That(service.ExportToMarkdown(Report(true), studio), Is.EqualTo(service.ExportToMarkdown(Report(true), ReportStudioContent.Empty)));
    }

    [Test]
    public void TextExports_ContainSceneHeadings_AndVideoHint_SkipScenesWithoutText()
    {
        var service = Service();

        var md = service.ExportToMarkdown(Report(true), Studio(Video()));
        var text = service.ExportToPlainText(Report(true), Studio(Video()));

        Assert.That(md, Does.Contain("## 🎬 Zellen erklärt").And.Contain("**Folie 2: Aufbau**")
            .And.Contain("Jede Zelle hat einen Kern und eine Membran.").And.Contain("Video: Zellen erklärt").And.Contain("3 Folien"));
        Assert.That(md, Does.Not.Contain("Folie 3: Ohne Ton"), "Szenen ohne Sprechtext werden ausgelassen");
        Assert.That(text, Does.Contain("Video-Skript – Zellen erklärt").And.Contain("Folie 1: Die Zelle")
            .And.Contain("Willkommen zur Zelle.").And.Contain("Video: Zellen erklärt"));
    }

    [Test]
    public void VideoMeta_UsesDurationAndProviderOfCurrentAudioOnly()
    {
        var video = Video();
        video.AudioFileName = "video.wav";
        video.AudioDurationSeconds = 185;
        video.AudioCreatedWith = "Gemini TTS";
        video.AudioScenesHash = video.ComputeScenesHash();

        Assert.That(ReportTextExporter.VideoMeta(video), Does.Contain("ca. 3 Min.").And.Contain("vertont mit Gemini TTS"));

        video.Scenes[1].Narration = "Geänderter Text.";
        Assert.That(ReportTextExporter.VideoMeta(video), Does.Not.Contain("vertont mit").And.Contain("unter 1 Min."),
            "veraltete Audiodatei: Dauer geschätzt, keine Angabe zur Vertonung");
    }

    [Test]
    public void Html_ScriptIsCollapsible_AndNoMediaEmbedded()
    {
        // WebUtility.HtmlEncode schreibt Umlaute als Zeichenreferenz (&#228;) – geprüft wird der dekodierte Text
        var html = System.Net.WebUtility.HtmlDecode(Service().ExportToHtml(Report(true), Studio(Video())));

        Assert.That(html, Does.Contain("<details class=\"audio-script video-script\">").And.Contain("id=\"video\"")
            .And.Contain("Video: Zellen erklärt").And.Contain("🎬 Video-Skript").And.Contain("Folie 2: Aufbau"));
        Assert.That(html, Does.Not.Contain("<audio").And.Not.Contain("<video").And.Not.Contain("data:audio"));
    }

    [Test]
    public async Task Docx_ContainsScriptSection()
    {
        var path = Path.Combine(_tempDir, "bericht.docx");

        await Service().ExportToDocxAsync(Report(true), Studio(Video()), path);

        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        var doc = XDocument.Load(stream);
        var text = string.Concat(doc.Descendants(W + "t").Select(t => t.Value));
        Assert.That(text, Does.Contain("Video-Skript – Zellen erklärt").And.Contain("Folie 2: Aufbau")
            .And.Contain("Jede Zelle hat einen Kern und eine Membran.").And.Contain("Video: Zellen erklärt"));
        Assert.That(zip.Entries.Any(e => e.FullName.EndsWith(".wav") || e.FullName.EndsWith(".png")), Is.False);
    }
}
