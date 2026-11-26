using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Common;
using StudyScope.Tests.Services;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Exporte in der übergebenen Sprache (3b-2): feste Texte, Namen gespeicherter Werte (Vorlagen, Sprecher, Lernstand),
/// Kopf der Quellspalte, <c>lang</c>/<c>dir</c> in HTML, <c>w:lang</c>/<c>a:rPr lang</c>/<c>dc:language</c> in Office-Dateien,
/// CSV-Trennzeichen nach dem regionalen Format und dass die Kultur nach dem Export wieder die alte ist.
/// </summary>
[TestFixture]
public class ExportLocalizationTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeExportLoc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---------- CultureScope ----------

    [Test]
    [SetCulture("de-CH")]
    public void CultureScope_SetsBothCultures_AndRestoresThem()
    {
        var ui = CultureInfo.CurrentUICulture;
        using (CultureScope.Use(TestCultures.En))
        {
            Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo("en"));
            Assert.That(CultureInfo.CurrentCulture.TwoLetterISOLanguageName, Is.EqualTo("en"), "andere Sprache → Format der Exportsprache");
        }
        using (CultureScope.Use(TestCultures.De))
            Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo("de-CH"), "gleiche Sprache → regionales Format bleibt");

        Assert.That(CultureInfo.CurrentUICulture, Is.EqualTo(ui));
        Assert.That(CultureInfo.CurrentCulture.Name, Is.EqualTo("de-CH"));
    }

    [Test]
    public void LanguageTags_ForOfficeAndHtml()
    {
        Assert.That(CultureScope.SpecificLanguageTag(TestCultures.En), Is.EqualTo("en-US"));
        Assert.That(CultureScope.SpecificLanguageTag(CultureInfo.GetCultureInfo("de")), Is.EqualTo("de-DE"));
        Assert.That(CultureScope.SpecificLanguageTag(TestCultures.De), Is.EqualTo("de-DE"));
        Assert.That(CultureScope.HtmlLanguageTag(TestCultures.En), Is.EqualTo("en"));
        Assert.That(CultureScope.Direction(CultureInfo.GetCultureInfo("ar")), Is.EqualTo("rtl"));
        Assert.That(CultureScope.Direction(TestCultures.De), Is.EqualTo("ltr"));
    }

    [Test]
    public void Export_DoesNotChangeTheCultureOfTheCaller()
    {
        var ui = CultureInfo.CurrentUICulture;
        var format = CultureInfo.CurrentCulture;
        ReportTextExporter.ToMarkdown(SampleReport(), SampleStudio(), TestCultures.En);
        ReportHtmlExporter.Export(SampleReport(), SampleStudio(), TestCultures.En);
        Assert.That(CultureInfo.CurrentUICulture, Is.EqualTo(ui));
        Assert.That(CultureInfo.CurrentCulture, Is.EqualTo(format));
    }

    // ---------- Bericht ----------

    [Test]
    public void ReportMarkdown_German_And_English()
    {
        var de = ReportTextExporter.ToMarkdown(SampleReport(), SampleStudio(), TestCultures.De);
        var en = ReportTextExporter.ToMarkdown(SampleReport(), SampleStudio(), TestCultures.En);

        Assert.That(de, Does.Contain("Lernübersicht · erstellt am").And.Contain("**Begriffe**").And.Contain("**Merksatz:**")
            .And.Contain("## Selbsttest").And.Contain("**Lösungen: 1. B · 2. Falsch**").And.Contain("Quellen: skript.txt"));
        Assert.That(en, Does.Contain("Study guide · created").And.Contain("**Terms**").And.Contain("**Key takeaway:**")
            .And.Contain("## Mind map").And.Contain("## Flashcards (1)").And.Contain("## Self-test").And.Contain("**Solutions: 1. B · 2. Falsch**")
            .And.Contain("Sources: skript.txt").And.Contain("### Slide 1: Überblick").And.Contain("| Source |")
            .And.Contain("**Host:**").And.Contain("**Expert:**").And.Contain("narrated with Gemini TTS")
            .And.Contain("**Slide 1: Zellbiologie**").And.Contain("(playable in the “Video” tab)").And.Contain("Terms & definitions"));
        Assert.That(en, Does.Not.Contain("Merksatz").And.Not.Contain("Quelle").And.Not.Contain("Folie").And.Not.Contain("Moderatorin"));
    }

    [Test]
    public void ReportPlainText_English_UsesEnglishLabels()
    {
        var en = ReportTextExporter.ToPlainText(SampleReport(), SampleStudio(), TestCultures.En);
        Assert.That(en, Does.Contain("Terms:").And.Contain("Q: Was ist ein Ribosom?").And.Contain("A: Ort der Proteinsynthese")
            .And.Contain("Key takeaway: Ohne Zelle").And.Contain("Data table – Begriffe").And.Contain("Audio script – Zellen im Gespräch")
            .And.Contain("Video script – Zellen erklärt").And.Contain("   Source: skript.txt").And.Contain("2 segments"));
        Assert.That(en, Does.Not.Contain("Merksatz").And.Not.Contain("Begriffe:").And.Not.Contain("F: "));
    }

    [Test]
    public void ReportHtml_LanguageAttributes_AndTexts()
    {
        var de = WebUtility.HtmlDecode(ReportHtmlExporter.Export(SampleReport(), SampleStudio(), TestCultures.De));
        var en = WebUtility.HtmlDecode(ReportHtmlExporter.Export(SampleReport(), SampleStudio(), TestCultures.En));

        Assert.That(de, Does.Contain("<html lang=\"de-DE\" dir=\"ltr\">").And.Contain(">Prüfen</button>").And.Contain("Erstellt mit StudyScope"));
        Assert.That(en, Does.Contain("<html lang=\"en\" dir=\"ltr\">").And.Contain(">Check</button>").And.Contain("Created with StudyScope")
            .And.Contain(">Contents</div>").And.Contain("Click a card to see the answer.").And.Contain(">Flip all</button>")
            .And.Contain("title=\"Sort\"").And.Contain("Solution: B").And.Contain("1 slide").And.Contain("(True or false?)".Trim('(', ')')));
        Assert.That(Markup(en), Does.Not.Contain("Prüfen").And.Not.Contain("Karteikarten").And.Not.Contain("Merksatz").And.Not.Contain("Quelle<"));
    }

    [Test]
    public void ReportHtml_ScriptTexts_AreDataAttributes_InTheExportLanguage()
    {
        var en = WebUtility.HtmlDecode(ReportHtmlExporter.Export(SampleReport(), SampleStudio(), TestCultures.En));
        Assert.That(en, Does.Contain("data-t-score=\"{r} of {c} correct (2 questions)\"").And.Contain("data-t-choose=\"Please choose an answer.\"")
            .And.Contain("data-t-correct=\"✓ Correct!\"").And.Contain("data-t-collapse=\"Collapse all\""));
        Assert.That(en, Does.Not.Contain("Bitte eine Antwort").And.Not.Contain("Alle zuklappen").And.Not.Contain("'de'"), "keine festen deutschen Texte im Skript");

        var de = WebUtility.HtmlDecode(ReportHtmlExporter.Export(SampleReport(), SampleStudio(), TestCultures.De));
        Assert.That(de, Does.Contain("data-t-score=\"{r} von {c} richtig (2 Fragen)\""));
    }

    [Test]
    public void ReportHtml_RightToLeftLanguage_SetsDir()
    {
        var html = ReportHtmlExporter.Export(SampleReport(), SampleStudio(), CultureInfo.GetCultureInfo("ar"));
        Assert.That(html, Does.Contain("<html lang=\"ar\" dir=\"rtl\">"));
    }

    [Test]
    public void ReportHtml_Css_UsesLogicalProperties()
    {
        var html = ReportHtmlExporter.Export(SampleReport(), SampleStudio(), TestCultures.De);
        var css = Regex.Match(html, "<style>(.*?)</style>", RegexOptions.Singleline).Groups[1].Value;
        // nur der Bericht selbst (Folien-CSS der App und mittiger Abspielknopf stehen nicht darin, solange kein Player eingebettet ist)
        Assert.That(css, Does.Not.Match(@"(margin|padding|border)-(left|right)\s*:|text-align:\s*(left|right)|float:\s*(left|right)"));
    }

    [Test]
    public async Task ReportDocx_English_LanguageStylesAndHeadings()
    {
        var path = Path.Combine(_tempDir, "bericht.docx");
        var report = SampleReport();
        await new ReportService(new FakeLlmService(), _tempDir).ExportToDocxAsync(report, SampleStudio(), path, TestCultures.En);

        using var zip = ZipFile.OpenRead(path);
        var styles = Read(zip, "word/styles.xml");
        var document = Read(zip, "word/document.xml");
        var core = Read(zip, "docProps/core.xml");

        Assert.That(styles, Does.Contain("<w:lang w:val=\"en-US\"/>").And.Contain("StudyScope Question").And.Contain("StudyScope Table"));
        Assert.That(styles, Does.Not.Contain("de-DE").And.Not.Contain("StudyScope Frage"));
        Assert.That(core, Does.Contain("<dc:language>en-US</dc:language>"));
        var texts = string.Join("|", XDocument.Parse(document).Descendants(W + "t").Select(t => t.Value));
        Assert.That(texts, Does.Contain("Terms").And.Contain("Explanation").And.Contain("Key takeaway:").And.Contain("Self-test")
            .And.Contain("Solutions").And.Contain("Presentation – Zellen").And.Contain("Slide 1: Überblick").And.Contain("Data table")
            .And.Contain("Source").And.Contain("Host: ").And.Contain("Video script – Zellen erklärt"));
        Assert.That(texts, Does.Not.Contain("Merksatz").And.Not.Contain("Lösungen").And.Not.Contain("Quelle"));
    }

    [Test]
    public async Task ReportDocx_German_KeepsGermanLanguage()
    {
        var path = Path.Combine(_tempDir, "bericht.docx");
        await new ReportService(new FakeLlmService(), _tempDir).ExportToDocxAsync(SampleReport(), SampleStudio(), path, TestCultures.De);
        using var zip = ZipFile.OpenRead(path);
        Assert.That(Read(zip, "word/styles.xml"), Does.Contain("<w:lang w:val=\"de-DE\"/>").And.Contain("StudyScope Merksatz"));
    }

    // ---------- Präsentation ----------

    [Test]
    public async Task Pptx_English_LanguageAndShapeNames()
    {
        var path = Path.Combine(_tempDir, "vortrag.pptx");
        var presentation = SampleStudio().Presentation!;
        presentation.Slides.Insert(0, new PresentationSlide { Kind = SlideKind.Title, Title = "Zellen", Notes = "Begrüßung" });
        await new PresentationService(new FakeLlmService(), _tempDir).ExportToPptxAsync(presentation, path, TestCultures.En);

        using var zip = ZipFile.OpenRead(path);
        var slide = Read(zip, "ppt/slides/slide2.xml");
        var notes = Read(zip, "ppt/notesSlides/notesSlide1.xml");
        Assert.That(slide, Does.Contain("lang=\"en-US\"").And.Contain("name=\"Title\"").And.Contain("name=\"Footer\"").And.Contain("name=\"Slide number\""));
        Assert.That(notes, Does.Contain("lang=\"en-US\"").And.Contain("name=\"Notes placeholder\""));
        Assert.That(Read(zip, "ppt/slideLayouts/slideLayout1.xml"), Does.Contain("name=\"Blank\""));
        Assert.That(Read(zip, "docProps/core.xml"), Does.Contain("<dc:language>en-US</dc:language>"));
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.Ordinal)))
            Assert.That(Read(zip, entry.FullName), Does.Not.Contain("de-DE").And.Not.Contain("Fußzeile"), entry.FullName);
    }

    // ---------- Datentabelle ----------

    [Test]
    public void CsvSeparator_FollowsTheDecimalSeparatorOfTheFormat()
    {
        Assert.That(DataTableExporter.CsvSeparator(CultureInfo.GetCultureInfo("de-DE")), Is.EqualTo(';'));
        Assert.That(DataTableExporter.CsvSeparator(CultureInfo.GetCultureInfo("fr-FR")), Is.EqualTo(';'));
        Assert.That(DataTableExporter.CsvSeparator(CultureInfo.GetCultureInfo("en-US")), Is.EqualTo(','));
        Assert.That(DataTableExporter.CsvSeparator(CultureInfo.GetCultureInfo("en-GB")), Is.EqualTo(','));
    }

    [Test]
    public void Csv_EnglishHeader_WithComma_QuotesFieldsWithCommas()
    {
        var csv = DataTableExporter.ToCsv(SampleTable(), SampleTable().Rows, TestCultures.En, ',');
        var lines = csv.Split("\r\n");
        Assert.That(lines[0], Is.EqualTo("Begriff,Definition,Source"));
        Assert.That(lines[1], Is.EqualTo("Zelle,\"kleinste Einheit, lebendig\",skript.txt"));
    }

    [Test]
    [SetCulture("en-US")]
    public void ServiceCsv_SeparatorFromRegionalFormat_HeaderFromExportLanguage()
    {
        var service = new DataTableService(new FakeLlmService(), _tempDir);
        var text = Encoding.UTF8.GetString(service.ExportToCsv(SampleTable(), SampleTable().Rows, TestCultures.De)).TrimStart('﻿');
        Assert.That(text, Does.StartWith("Begriff,Definition,Quelle\r\n"), "englisches Format → Komma, deutsche Exportsprache → „Quelle“");
    }

    [Test]
    [SetCulture("de-DE")]
    public void ServiceCsv_GermanFormat_UsesSemicolon_EvenForEnglishExport()
    {
        var service = new DataTableService(new FakeLlmService(), _tempDir);
        var text = Encoding.UTF8.GetString(service.ExportToCsv(SampleTable(), SampleTable().Rows, TestCultures.En)).TrimStart('﻿');
        Assert.That(text, Does.StartWith("Begriff;Definition;Source\r\n"));
    }

    [Test]
    public void DataTableMarkdown_English()
    {
        var md = DataTableExporter.ToMarkdown(SampleTable(), SampleTable().Rows, TestCultures.En);
        Assert.That(md, Does.Contain("*Terms & definitions · Topic: Zellen · 2 rows*").And.Contain("| Begriff | Definition | Source |"));
    }

    [Test]
    public async Task Xlsx_English_HeaderSheetNameAndLanguage()
    {
        var table = SampleTable();
        table.Title = "";
        var path = Path.Combine(_tempDir, "tabelle.xlsx");
        await new DataTableService(new FakeLlmService(), _tempDir).ExportToXlsxAsync(table, table.Rows, path, TestCultures.En);

        using var zip = ZipFile.OpenRead(path);
        Assert.That(Read(zip, "xl/workbook.xml"), Does.Contain("name=\"Data table\""));
        Assert.That(Read(zip, "xl/worksheets/sheet1.xml"), Does.Contain(">Source<"));
        Assert.That(Read(zip, "docProps/core.xml"), Does.Contain("<dc:language>en-US</dc:language>"));
    }

    [Test]
    public void SourceColumnName_IsReservedInEveryLanguage()
    {
        Assert.That(DataTableDocument.SourceColumnName, Is.EqualTo("Quelle"));
        Assert.That(DataTableDocument.IsReservedColumnName("Quelle"), Is.True);
        Assert.That(DataTableDocument.IsReservedColumnName(" source "), Is.True, "englischer Name auch bei deutscher Oberfläche reserviert");
        Assert.That(DataTableDocument.IsReservedColumnName("Quellen"), Is.False);
        Assert.That(DataTableDocument.SourceColumnNames(), Is.SupersetOf(new[] { "Quelle", "Source" }));
    }

    [Test]
    [SetUICulture("en")]
    public void SourceColumnName_English()
    {
        Assert.That(DataTableDocument.SourceColumnName, Is.EqualTo("Source"));
        Assert.That(DataTableDocument.IsReservedColumnName("Quelle"), Is.True);
    }

    // ---------- Karteikarten, Mindmap, Audio, Video ----------

    [Test]
    public void FlashcardMarkdown_English()
    {
        var cards = new List<Flashcard>
        {
            new() { Question = "Was ist ATP?", Answer = "Energieträger", Status = FlashcardStatus.Known },
            new() { Question = "Was ist DNA?", Answer = "Erbgut", Status = FlashcardStatus.Difficult }
        };
        var md = new FlashcardService(new FakeLlmService()).ExportToMarkdown("Testprofil", cards, TestCultures.En);
        Assert.That(md, Does.Contain("# Flashcards: Testprofil").And.Contain("*Created: ").And.Contain("### Question 1: Was ist ATP?")
            .And.Contain("**Answer:** Energieträger").And.Contain("*Status:* Learned").And.Contain("*Status:* Review"));
        Assert.That(md, Does.Not.Contain("Antwort").And.Not.Contain("Gelernt"));
    }

    [Test]
    public void MindmapMarkdown_English()
    {
        var mindmap = SampleStudio().Mindmap!;
        mindmap.SourceDocuments = new List<string> { "skript.txt" };
        var md = new MindmapService(new FakeLlmService(), _tempDir).ExportToMarkdown(mindmap, TestCultures.En);
        Assert.That(md, Does.Contain("*Sources: skript.txt – created "));
    }

    [Test]
    public void AudioExports_English_TranslateFormatAndSpeakers()
    {
        var service = new AudioScriptService(new FakeLlmService(), _tempDir);
        var audio = SampleAudio();
        audio.SourceDocuments = new List<string> { "skript.txt" };
        audio.Topic = "Zellkern";

        var text = service.ExportToText(audio, TestCultures.En);
        var md = service.ExportToMarkdown(audio, TestCultures.En);
        Assert.That(text, Does.Contain("Podcast conversation · ").And.Contain("Focus: Zellkern").And.Contain("· created ")
            .And.Contain("Sources: skript.txt").And.Contain("Host: Was ist eine Zelle?").And.Contain("Expert: Die kleinste Einheit"));
        Assert.That(md, Does.Contain("**Host:**").And.Contain("*Sources: skript.txt*"));
        Assert.That(text + md, Does.Not.Contain("Moderatorin").And.Not.Contain("Schwerpunkt").And.Not.Contain("Quellen"));
    }

    [Test]
    public void VideoScriptExports_English()
    {
        var service = new VideoScriptService(new FakeLlmService(), _tempDir);
        var video = SampleVideo();
        var text = service.ExportToText(video, TestCultures.En);
        var md = service.ExportToMarkdown(video, TestCultures.En);
        Assert.That(text, Does.Contain("3 slides · ").And.Contain(" · created ").And.Contain("Slide 1: "));
        Assert.That(md, Does.Contain("## Slide 2: "));
        Assert.That(text + md, Does.Not.Contain("Folie").And.Not.Contain("erstellt"));
    }

    [Test]
    public async Task VideoHtml_English_PageAndPlayerTexts()
    {
        var video = SampleVideo();
        var data = await VideoHtmlExporter.BuildPlayerDataAsync(video, audioPath: null, mp3: null);
        var html = WebUtility.HtmlDecode(VideoHtmlExporter.Export(data, TestCultures.En));

        Assert.That(html, Does.Contain("<html lang=\"en\" dir=\"ltr\">").And.Contain("Keyboard (after clicking into the player)")
            .And.Contain("Narration per slide").And.Contain("Created with StudyScope").And.Contain("without sound")
            .And.Contain("aria-label=\"Video: Zellen erklärt\"").And.Contain("data-t-slide=\"Slide {n} / {t}\"").And.Contain("data-t-play=\"Play\"")
            .And.Contain(">Slide 1 / 3<").And.Contain("title=\"No sound\"").And.Contain("Without sound:"), "Hinweis ohne Ton in der Exportsprache");
        Assert.That(Markup(html), Does.Not.Contain("Abspielen").And.Not.Contain("Vollbild").And.Not.Contain("Folie").And.Not.Contain("Ohne Ton"));
    }

    [Test]
    public void ReportHtml_EmbeddedPlayer_FollowsTheExportLanguage()
    {
        var video = SampleVideo();
        var data = VideoHtmlExporter.BuildPlayerDataAsync(video, audioPath: null, mp3: null).GetAwaiter().GetResult();
        var report = SampleReport();
        var html = new ReportService(new FakeLlmService(), _tempDir).ExportToHtml(report, new ReportStudioContent { VideoScripts = new[] { video } },
            new ReportHtmlOptions { VideoPlayers = new Dictionary<string, VideoPlayerData> { [video.Id] = data } }, TestCultures.En);
        Assert.That(html, Does.Contain("🎬 Video</h2>").And.Contain("Press ▶ to play").And.Contain("data-t-pause=\"Pause\"").And.Contain("Previous slide"));
    }

    // ---------- ViewModels übergeben die Exportsprache ----------

    [Test]
    public void ViewModels_PassTheExportCulture_ToEveryExport()
    {
        var root = RepoRoot();
        var call = new Regex(@"_\w+\.(ExportTo\w+)\(", RegexOptions.Compiled);
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "StudyScope.ViewModels"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in call.Matches(lines[i]))
                {
                    var rest = lines[i][m.Index..];
                    if (!rest.Contains("ExportCulture.Current", StringComparison.Ordinal))
                        problems.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Groups[1].Value} ohne ExportCulture.Current");
                }
            }
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    // ---------- Testdaten ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StudyScope.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("StudyScope.slnx nicht gefunden");
    }

    /// <summary>Sichtbares Markup ohne &lt;style&gt;/&lt;script&gt; (deren Code-Kommentare bleiben deutsch und sind nicht sichtbar).</summary>
    private static string Markup(string html) =>
        Regex.Replace(html, @"<(style|script)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline);

    private static string Read(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    private static DataTableDocument SampleTable()
    {
        var table = new DataTableDocument
        {
            Title = "Begriffe",
            Template = DataTableTemplate.Terms,
            Topic = "Zellen",
            Columns = new List<string> { "Begriff", "Definition" }
        };
        table.Rows.Add(new DataTableRow { Cells = new List<string> { "Zelle", "kleinste Einheit, lebendig" }, Source = "skript.txt" });
        table.Rows.Add(new DataTableRow { Cells = new List<string> { "Kern", "Steuerzentrale" }, Source = "skript.txt" });
        return table;
    }

    private static AudioSummary SampleAudio() => new()
    {
        Title = "Zellen im Gespräch",
        Format = AudioScriptFormat.Podcast,
        Segments = new List<AudioSegment>
        {
            new() { Speaker = "Moderatorin", Text = "Was ist eine Zelle?" },
            new() { Speaker = "Experte", Text = "Die kleinste Einheit des Lebens." }
        }
    };

    private static VideoDocument SampleVideo()
    {
        var video = new VideoScriptService(new FakeLlmService()).CreateFromNotes(VideoScriptServiceTests.SamplePresentation(), "Zellbiologie");
        video.Title = "Zellen erklärt";
        return video;
    }

    private static Report SampleReport() => new()
    {
        Title = "Zellbiologie",
        Summary = "Überblick über die Zelle.",
        Template = ReportTemplate.StudyGuide,
        SourceDocuments = new List<string> { "skript.txt" },
        CreatedAt = new DateTime(2026, 11, 26, 14, 30, 0),
        Embeds = new ReportEmbeds { DataTables = true, AudioScripts = true, VideoScripts = true },
        Sections = new List<ReportSection>
        {
            new()
            {
                Heading = "Aufbau",
                Paragraphs = new List<string> { "Die Zelle ist die kleinste Einheit des Lebens." },
                Terms = new List<ReportTerm> { new() { Term = "Zelle", Definition = "kleinste Einheit" } },
                Questions = new List<ReportQuestion> { new() { Question = "Was ist ein Ribosom?", Answer = "Ort der Proteinsynthese" } },
                Takeaway = "Ohne Zelle kein Leben."
            }
        }
    };

    private static ReportStudioContent SampleStudio()
    {
        var video = SampleVideo();
        video.AudioFileName = "video.wav";
        video.AudioDurationSeconds = 60;
        video.AudioCreatedWith = "Gemini TTS";
        video.AudioSceneStarts = video.Scenes.Select((_, i) => i * 20.0).ToList();
        video.AudioScenesHash = video.ComputeScenesHash();
        var table = SampleTable();
        return new ReportStudioContent
        {
            Mindmap = new MindmapDocument
            {
                Root = new MindmapNode { Title = "Zelle", Children = new List<MindmapNode> { new() { Title = "Organellen", Summary = "Funktionseinheiten" } } }
            },
            Flashcards = new List<Flashcard> { new() { Question = "Was ist das Mitochondrium?", Answer = "Das Kraftwerk der Zelle" } },
            QuizQuestions = new List<QuizQuestion>
            {
                new() { Type = QuizQuestionType.SingleChoice, Text = "Welche Organelle liefert Energie?", Options = new List<string> { "Ribosom", "Mitochondrium" }, CorrectIndices = new List<int> { 1 } },
                new() { Type = QuizQuestionType.TrueFalse, Text = "Bakterien haben einen Zellkern.", Options = new List<string> { "Wahr", "Falsch" }, CorrectIndices = new List<int> { 1 } }
            },
            Presentation = new PresentationDocument
            {
                Title = "Zellen",
                Slides = new List<PresentationSlide> { new() { Title = "Überblick", Bullets = new List<string> { "Aufbau" } } }
            },
            DataTables = new[] { new ReportDataTable(table, table.Rows) },
            AudioScripts = new[] { SampleAudio() },
            VideoScripts = new[] { video }
        };
    }
}
