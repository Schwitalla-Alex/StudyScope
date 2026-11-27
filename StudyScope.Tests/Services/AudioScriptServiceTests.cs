using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Common;

namespace StudyScope.Tests.Services;

/// <summary>
/// Tests für den AudioScriptService: Prompt (gesprochenes Deutsch, Format, Länge, Schwerpunkt), nachsichtige Auswertung
/// (JSON, deutsche Schlüssel, Hüllen, Sprecherzeilen, reiner Text, Reparatur), Sprecherzuordnung, Bereinigung,
/// Fake-KI (Reihenfolge in Dispatcher und FakeLlmService), Speicherung je Profil samt Audiodateien und Export.
/// </summary>
[TestFixture]
public class AudioScriptServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeAudio_" + Guid.NewGuid().ToString("N"));
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
    public void BuildPrompt_StartsWithMarker_AndDemandsSpokenGerman()
    {
        var prompt = AudioScriptService.BuildPrompt(AudioScriptFormat.Summary, AudioScriptLength.Short, null);

        Assert.That(prompt, Does.StartWith("Erstelle ein Audio-Skript"));
        Assert.That(prompt, Does.Contain("JSON-Array mit genau EINEM Objekt"));
        Assert.That(prompt, Does.Contain("\"segments\"").And.Contain("\"speaker\""));
        Assert.That(prompt, Does.Contain("Keine Aufzählungszeichen"));
        Assert.That(prompt, Does.Contain("Abkürzungen ausschreiben"));
        Assert.That(prompt, Does.Contain("Keine Formeln, Tabellen"));
        Assert.That(prompt, Does.Contain("NUR Inhalte aus den Auszügen"));
        Assert.That(prompt, Does.Contain("etwa 400 Wörter").And.Contain("ca. 3 Minuten"));
        Assert.That(prompt, Does.Contain("\"Sprecherin\""));
        Assert.That(prompt, Does.Not.Contain("Schwerpunkt:"));
    }

    [Test]
    public void BuildPrompt_Podcast_NamesBothSpeakers_AndTopic()
    {
        var prompt = AudioScriptService.BuildPrompt(AudioScriptFormat.Podcast, AudioScriptLength.Long, "Zellatmung");

        Assert.That(prompt, Does.Contain("Format: Podcast-Gespräch"));
        Assert.That(prompt, Does.Contain("\"Moderatorin\"").And.Contain("\"Experte\""));
        Assert.That(prompt, Does.Contain("Schwerpunkt: Konzentriere dich auf das Thema „Zellatmung“"));
        Assert.That(prompt, Does.Contain("ca. 15 Minuten"));
        Assert.That(prompt, Does.Not.Contain("Mindmap"), "darf die Mindmap-Erkennung nicht auslösen");
    }

    [Test]
    public void TargetWords_GrowWithLength()
    {
        Assert.That(AudioScriptFormats.TargetWords(AudioScriptLength.Short), Is.LessThan(AudioScriptFormats.TargetWords(AudioScriptLength.Medium)));
        Assert.That(AudioScriptFormats.TargetWords(AudioScriptLength.Medium), Is.LessThan(AudioScriptFormats.TargetWords(AudioScriptLength.Long)));
    }

    [Test]
    public void SelectChunks_WithTopic_PrefersMatchingChunks()
    {
        var chunks = Enumerable.Range(0, 30).Select(i => new SourceChunk("a.txt", $"Allgemeiner Text Nummer {i}.")).ToList();
        chunks[17] = new SourceChunk("b.txt", "Die Photosynthese wandelt Licht in chemische Energie um.");

        var selected = AudioScriptService.SelectChunks(chunks, "Photosynthese", AudioScriptLength.Short);

        Assert.That(selected, Has.Count.EqualTo(AudioScriptService.MaxChunks(AudioScriptLength.Short)));
        Assert.That(selected[0].SourceDocument, Is.EqualTo("b.txt"));
    }

    // ---------- Auswertung ----------

    [Test]
    public void Analyze_StandardJson_ReadsTitleAndSegments()
    {
        var raw = """[{"title": "Zellbiologie", "segments": [{"speaker": "Sprecherin", "text": "Willkommen."}, {"speaker": "Sprecherin", "text": "Zellen sind klein."}]}]""";

        var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Summary, "Ersatz");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Summary!.Title, Is.EqualTo("Zellbiologie"));
        Assert.That(result.Summary.Segments.Select(s => s.Text), Is.EqualTo(new[] { "Willkommen.", "Zellen sind klein." }));
        Assert.That(result.WasRepaired, Is.False);
        Assert.That(result.WasPlainText, Is.False);
    }

    [Test]
    public void Analyze_GermanKeys_AndEnvelope_AreUnderstood()
    {
        var raw = """{"items": [{"titel": "Gespräch", "abschnitte": [{"sprecher": "Moderatorin", "inhalt": "Was ist Osmose?"}, {"sprecher": "Experte", "inhalt": "Diffusion von Wasser."}]}]}""";

        var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Podcast, "Ersatz");

        Assert.That(result.Summary!.Title, Is.EqualTo("Gespräch"));
        Assert.That(result.Summary.Segments.Select(s => s.Speaker), Is.EqualTo(new[] { "Moderatorin", "Experte" }));
        Assert.That(result.Summary.Segments[1].Text, Is.EqualTo("Diffusion von Wasser."));
    }

    [Test]
    public void Analyze_SegmentsAsSpeakerStrings_AndSingleKeyObjects()
    {
        var raw = """[{"title": "T", "dialog": ["Moderatorin: Hallo zusammen.", {"Experte": "Hallo!"}, "Moderatorin: Los geht's."]}]""";

        var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Podcast, "Ersatz");

        Assert.That(result.Summary!.Segments.Select(s => s.Speaker), Is.EqualTo(new[] { "Moderatorin", "Experte", "Moderatorin" }));
        Assert.That(result.Summary.Segments[0].Text, Is.EqualTo("Hallo zusammen."));
        Assert.That(result.Summary.Segments[1].Text, Is.EqualTo("Hallo!"));
    }

    [Test]
    public void Analyze_PlainText_OneSegmentPerParagraph()
    {
        var raw = "# Die Zelle\n\nZellen sind die kleinsten Einheiten des Lebens.\nSie haben eine Membran.\n\nDer Zellkern enthält die Erbinformation.";

        var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Summary, "Ersatz");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.WasPlainText, Is.True);
        Assert.That(result.Summary!.Title, Is.EqualTo("Die Zelle"));
        Assert.That(result.Summary.Segments, Has.Count.EqualTo(2));
        Assert.That(result.Summary.Segments[0].Text, Is.EqualTo("Zellen sind die kleinsten Einheiten des Lebens. Sie haben eine Membran."));
        Assert.That(result.Summary.Segments.All(s => s.Speaker == "Sprecherin"), Is.True);
    }

    [Test]
    public void Analyze_PlainText_WithSpeakerLines_KeepsDialog_ButNotLabelsLikeMerke()
    {
        var raw = "**Moderatorin:** Worum geht es heute?\n**Experte:** Um Enzyme.\nMerke: Enzyme sind Katalysatoren.\nModeratorin: Danke!";

        var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Podcast, "Ersatz");

        var segments = result.Summary!.Segments;
        Assert.That(segments.Select(s => s.Speaker), Is.EqualTo(new[] { "Moderatorin", "Experte", "Moderatorin" }));
        Assert.That(segments[1].Text, Is.EqualTo("Um Enzyme. Merke: Enzyme sind Katalysatoren."), "„Merke“ ist kein Sprecher");
        Assert.That(result.Summary.Title, Is.EqualTo("Ersatz"));
    }

    [Test]
    public void Analyze_TruncatedJson_IsRepaired()
    {
        var raw = "```json\n[{\"title\": \"Abgeschnitten\", \"segments\": [{\"speaker\": \"Sprecherin\", \"text\": \"Erster Satz.\"}, " +
                  "{\"speaker\": \"Sprecherin\", \"text\": \"Zweiter Satz.\"}, {\"speaker\": \"Sprecherin\", \"text\": \"Dritter Sa";

        var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Summary, "Ersatz");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.WasRepaired, Is.True);
        Assert.That(result.Summary!.Segments.Select(s => s.Text), Is.EqualTo(new[] { "Erster Satz.", "Zweiter Satz." }));
    }

    [Test]
    public void Analyze_ScriptAsSingleString_IsSplitLikePlainText()
    {
        var raw = """{"titel": "Briefing", "skript": "Erstens das Wichtigste.\n\nZweitens der Rest."}""";

        var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Briefing, "Ersatz");

        Assert.That(result.Summary!.Title, Is.EqualTo("Briefing"));
        Assert.That(result.Summary.Segments, Has.Count.EqualTo(2));
    }

    [Test]
    public void Analyze_EmptyOrCancelled_Fails()
    {
        Assert.That(AudioScriptService.AnalyzeResponse("", AudioScriptFormat.Summary, "x").FailureReason, Is.EqualTo(MindmapFailureReason.EmptyResponse));
        Assert.That(AudioScriptService.AnalyzeResponse(LlmServiceDispatcher.CancelledResponse, AudioScriptFormat.Summary, "x").IsSuccess, Is.False);
        Assert.That(AudioScriptService.AnalyzeResponse("""[{"title": "Leer", "segments": []}]""", AudioScriptFormat.Summary, "x").FailureReason,
            Is.EqualTo(MindmapFailureReason.NoTopics));
    }

    [Test]
    public void Analyze_CleansBulletsMarkdownAndSourceMarkers()
    {
        var raw = """[{"title": "T", "segments": [{"speaker": "Sprecherin", "text": "- **Erstens** der Kern.\n1. Zweitens [Quelle: a.txt] die Hülle."}]}]""";

        var text = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Summary, "x").Summary!.Segments[0].Text;

        Assert.That(text, Is.EqualTo("Erstens der Kern. Zweitens die Hülle."));
    }

    [Test]
    public void Analyze_SingleSpeakerFormat_MapsAllSpeakersToNarrator()
    {
        var raw = """[{"title": "T", "segments": [{"speaker": "Erzähler", "text": "A."}, {"speaker": "", "text": "B."}]}]""";

        var segments = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Summary, "x").Summary!.Segments;

        Assert.That(segments.All(s => s.Speaker == AudioScriptFormats.NarratorName), Is.True);
    }

    [Test]
    public void NormalizeSpeakers_Podcast_MapsUnknownNamesToFreeRoles_AndFillsMissing()
    {
        var segments = new List<AudioSegment>
        {
            new() { Speaker = "Anna", Text = "Frage" },
            new() { Speaker = "Experte", Text = "Antwort" },
            new() { Speaker = "", Text = "Nachfrage" },
            new() { Speaker = "Dritter", Text = "Einwurf" }
        };

        AudioScriptService.NormalizeSpeakers(segments, AudioScriptFormats.Speakers(AudioScriptFormat.Podcast));

        Assert.That(segments.Select(s => s.Speaker), Is.EqualTo(new[] { "Moderatorin", "Experte", "Moderatorin", "Moderatorin" }));
        Assert.That(segments.Select(s => s.Speaker).Distinct().Count(), Is.LessThanOrEqualTo(AudioScriptService.MaxSpeakers));
    }

    [Test]
    public void LongSegments_AreSplitAtSentenceEnds()
    {
        var sentence = "Dies ist ein Satz mit einigen Wörtern. ";
        var longText = string.Concat(Enumerable.Repeat(sentence, 200));
        var parts = AudioScriptService.SplitLongText(longText, AudioSummary.MaxSegmentLength).ToList();

        Assert.That(parts, Has.Count.GreaterThan(1));
        Assert.That(parts.All(p => p.Length <= AudioSummary.MaxSegmentLength), Is.True);
        Assert.That(parts.All(p => p.EndsWith('.')), Is.True);
    }

    // ---------- Erzeugung und Fake-KI ----------

    [Test]
    public async Task Generate_WithFakeAi_Podcast_HasTwoSpeakersAndSources()
    {
        var service = new AudioScriptService(new FakeLlmService(), _tempDir);
        var chunks = new[]
        {
            new SourceChunk("bio.txt", "Mitochondrien sind die Kraftwerke der Zelle und erzeugen Energie."),
            new SourceChunk("chemie.txt", "Enzyme beschleunigen chemische Reaktionen im Körper deutlich.")
        };

        var result = await service.GenerateAsync(chunks, AudioScriptFormat.Podcast, AudioScriptLength.Short, "", "Ersatz");

        Assert.That(result.IsSuccess, Is.True, result.RawResponse);
        var summary = result.Summary!;
        Assert.That(summary.Format, Is.EqualTo(AudioScriptFormat.Podcast));
        Assert.That(summary.Length, Is.EqualTo(AudioScriptLength.Short));
        Assert.That(summary.Speakers, Is.EqualTo(new[] { "Moderatorin", "Experte" }));
        Assert.That(summary.SourceDocuments, Is.EquivalentTo(new[] { "bio.txt", "chemie.txt" }));
        Assert.That(summary.Segments.Any(s => s.Text.Contains("Mitochondrien")), Is.True);
    }

    [Test]
    public async Task Generate_WithoutText_ReturnsNoContent()
    {
        var service = new AudioScriptService(new FakeLlmService(), _tempDir);
        var result = await service.GenerateAsync(new[] { new SourceChunk("leer.txt", "  ") }, AudioScriptFormat.Summary, AudioScriptLength.Medium, "", "x");
        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.NoContent));
    }

    [Test]
    public async Task FakeAi_AudioPromptWithOtherKeywords_ReturnsScript_ViaDispatcherAndFakeService()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync("Testprofil");
        storage.Setup(s => s.LoadAiConfigForProfileAsync("Testprofil")).ReturnsAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi });
        var dispatcher = new LlmServiceDispatcher(storage.Object, new HttpClient());

        // Schwerpunkt mit Wörtern anderer Bereiche: entscheidend ist nur die Kennzeile
        var prompt = StudyScope.Core.Common.TaskPrompt.Mark(StudyScope.Core.Common.TaskKind.AudioScript,
            AudioScriptService.BuildPrompt(AudioScriptFormat.Summary, AudioScriptLength.Short,
                "Mindmap, Quiz-Themen, Erstelle eine Präsentation, Lernkarteikarten"));
        var chunks = new List<string> { "Osmose bezeichnet die Diffusion von Wasser durch eine Membran." };

        foreach (var llm in new ILlmService[] { dispatcher, new FakeLlmService() })
        {
            var raw = await llm.AskQuestionAsync(prompt, chunks);
            var result = AudioScriptService.AnalyzeResponse(raw, AudioScriptFormat.Summary, "x");

            Assert.That(result.IsSuccess, Is.True, raw);
            Assert.That(result.WasPlainText, Is.False);
            Assert.That(result.Summary!.Title, Does.Contain("Testmodus"));
            Assert.That(result.Summary.Segments.Any(s => s.Text.Contains("Osmose")), Is.True);
        }
    }

    // ---------- Speicherung ----------

    [Test]
    public async Task SaveAndLoad_RoundTrip_PerProfile_WithoutTempFile()
    {
        var service = new AudioScriptService(new FakeLlmService(), _tempDir);
        var library = new AudioLibrary();
        library.Items.Add(new AudioSummary
        {
            Title = "Beitrag",
            Format = AudioScriptFormat.Podcast,
            Segments = { new AudioSegment { Speaker = "Moderatorin", Text = "Hallo." } },
            SourceDocuments = { "a.txt" }
        });

        await service.SaveLibraryAsync("Testprofil", library);
        var loaded = await service.LoadLibraryAsync("Testprofil");
        var other = await service.LoadLibraryAsync("Anderes Profil");

        Assert.That(File.Exists(Path.Combine(_tempDir, "Audio", "Testprofil.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempDir, "Audio", "Testprofil.json.tmp")), Is.False);
        Assert.That(loaded.Items, Has.Count.EqualTo(1));
        Assert.That(loaded.Items[0].Format, Is.EqualTo(AudioScriptFormat.Podcast));
        Assert.That(loaded.Items[0].Segments[0].Text, Is.EqualTo("Hallo."));
        Assert.That(other.Items, Is.Empty);
    }

    [Test]
    public async Task Load_BrokenFile_ReturnsEmptyLibrary()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Audio"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Audio", "Testprofil.json"), "{ kaputt");

        var library = await new AudioScriptService(new FakeLlmService(), _tempDir).LoadLibraryAsync("Testprofil");

        Assert.That(library.Items, Is.Empty);
    }

    [Test]
    public async Task AudioFile_LivesInProfileFolder_AndIsDeleted()
    {
        var service = new AudioScriptService(new FakeLlmService(), _tempDir);
        var directory = service.GetAudioDirectory("Testprofil");
        var file = Path.Combine(directory, "beitrag.wav");
        await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3 });
        var summary = new AudioSummary { AudioFileName = "beitrag.wav" };

        Assert.That(directory, Is.EqualTo(Path.Combine(_tempDir, "Audio", "Testprofil")));
        Assert.That(service.GetAudioFilePath("Testprofil", summary), Is.EqualTo(file));

        service.DeleteAudioFile("Testprofil", summary);

        Assert.That(File.Exists(file), Is.False);
        Assert.That(summary.AudioFileName, Is.Empty);
        Assert.That(service.GetAudioFilePath("Testprofil", summary), Is.Null);
    }

    [Test]
    public async Task GetAudioFilePath_IgnoresDirectoryParts()
    {
        var service = new AudioScriptService(new FakeLlmService(), _tempDir);
        var outside = Path.Combine(_tempDir, "geheim.wav");
        await File.WriteAllBytesAsync(outside, new byte[] { 1 });

        var path = service.GetAudioFilePath("Testprofil", new AudioSummary { AudioFileName = @"..\..\geheim.wav" });

        Assert.That(path, Is.Null);
        Assert.That(File.Exists(outside), Is.True);
    }

    // ---------- Export ----------

    [Test]
    public void Export_Podcast_ShowsSpeakers_SummaryWithout()
    {
        var service = new AudioScriptService(new FakeLlmService(), _tempDir);
        var podcast = new AudioSummary
        {
            Title = "Gespräch",
            Format = AudioScriptFormat.Podcast,
            Segments = { new AudioSegment { Speaker = "Moderatorin", Text = "Frage?" }, new AudioSegment { Speaker = "Experte", Text = "Antwort_mit*Stern." } }
        };
        var single = new AudioSummary
        {
            Title = "Allein",
            Segments = { new AudioSegment { Speaker = "Sprecherin", Text = "Nur Text." } },
            SourceDocuments = { "a.txt" }
        };

        var text = service.ExportToText(podcast, TestCultures.De);
        var markdown = service.ExportToMarkdown(podcast, TestCultures.De);
        var singleText = service.ExportToText(single, TestCultures.De);

        Assert.That(text, Does.StartWith("Gespräch"));
        Assert.That(text, Does.Contain("Moderatorin: Frage?"));
        Assert.That(markdown, Does.StartWith("# Gespräch"));
        Assert.That(markdown, Does.Contain("**Experte:** Antwort\\_mit\\*Stern."));
        Assert.That(singleText, Does.Contain("Nur Text.").And.Not.Contain("Sprecherin:"));
        Assert.That(singleText, Does.Contain("Quellen: a.txt"));
    }
}
