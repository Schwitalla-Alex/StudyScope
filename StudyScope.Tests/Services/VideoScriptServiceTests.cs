using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Videoübersicht: Prompt, nachsichtige Auswertung (Hüllen, deutsche Schlüssel, Index ab 0/1, fehlende Szenen aus den
/// Notizen), Sprechernotizen ohne KI, „Folien aktualisieren“, Speicherung samt Audiodateien, Zusammensetzen der
/// Szenen-Audiodateien mit exakten Startzeiten und Erkennungsreihenfolge der Fake-KI.
/// </summary>
[TestFixture]
public class VideoScriptServiceTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeVideo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    internal static PresentationDocument SamplePresentation(bool withNotes = true) => new()
    {
        Title = "Zellbiologie",
        SourceDocuments = { "zelle.txt" },
        Slides =
        {
            new PresentationSlide { Id = "s0", Kind = SlideKind.Title, Title = "Zellbiologie", Subtitle = "Grundlagen", Notes = withNotes ? "Heute geht es um die Grundlagen der Zellbiologie und ihre wichtigsten Bausteine." : "" },
            new PresentationSlide { Id = "s1", Title = "Aufbau der Zelle", Bullets = { "Zellmembran", "Zellkern", "Zytoplasma" }, Notes = withNotes ? "Jede Zelle ist von einer Membran umgeben und enthält den Zellkern." : "" },
            new PresentationSlide { Id = "s2", Title = "Mitochondrien", Bullets = { "Kraftwerke der Zelle", "Eigene DNA" }, Notes = withNotes ? "Mitochondrien erzeugen die Energie, die die Zelle zum Leben braucht." : "" }
        }
    };

    // ---------- Prompt ----------

    [Test]
    public void BuildPrompt_StartsWithMarker_AsksForJsonArray_SlidesGoAsChunks()
    {
        var presentation = SamplePresentation();
        var prompt = VideoScriptService.BuildPrompt(presentation);
        var chunks = VideoScriptService.BuildSlideChunks(presentation);

        Assert.That(prompt, Does.StartWith("Erstelle ein Video-Skript"));
        Assert.That(prompt, Does.Contain("JSON-Array").And.Contain("slideIndex").And.Contain("narration"));
        Assert.That(prompt, Does.Contain("Anzahl Folien: 3"));
        Assert.That(prompt, Does.Not.Contain("Mindmap").And.Not.Contain("Erstelle eine Präsentation"));
        Assert.That(chunks, Has.Count.EqualTo(3));
        Assert.That(chunks[0], Does.StartWith("Folie 1: Zellbiologie (Titelfolie)"));
        Assert.That(chunks[1], Does.Contain("- Zellkern").And.Contain("Sprechernotizen: Jede Zelle"));
    }

    // ---------- Auswertung ----------

    [Test]
    public void Analyze_StandardSchema()
    {
        var raw = """[{"title": "Video", "scenes": [{"slideIndex": 0, "narration": "Willkommen."}, {"slideIndex": 1, "narration": "Die Zelle."}, {"slideIndex": 2, "narration": "Energie."}]}]""";
        var result = VideoScriptService.AnalyzeResponse(raw, 3);

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.Value!.Title, Is.EqualTo("Video"));
        Assert.That(result.Value.Narrations, Is.EqualTo(new[] { "Willkommen.", "Die Zelle.", "Energie." }));
    }

    [Test]
    public void Analyze_OneBasedIndex_GermanKeys_JsonModeEnvelope()
    {
        var raw = """{"items": [{"titel": "Film", "szenen": [{"folie": 1, "sprechtext": "Eins."}, {"folie": "Folie 2", "sprechtext": ["Zwei a.", "Zwei b."]}, {"folie": 3, "text": "- Drei."}]}]}""";
        var result = VideoScriptService.AnalyzeResponse(raw, 3);

        Assert.That(result.Value!.Title, Is.EqualTo("Film"));
        Assert.That(result.Value.Narrations, Is.EqualTo(new[] { "Eins.", "Zwei a. Zwei b.", "Drei." }));
    }

    [Test]
    public void Analyze_NoIndex_UsesPosition_IgnoresExtraScenes()
    {
        var raw = """[{"narration": "A."}, {"narration": "B."}, {"narration": "C."}, {"narration": "Zu viel."}]""";
        var result = VideoScriptService.AnalyzeResponse(raw, 3);

        Assert.That(result.Value!.Narrations, Is.EqualTo(new[] { "A.", "B.", "C." }));
    }

    [Test]
    public void Analyze_TruncatedResponse_IsRepaired()
    {
        var raw = """[{"title": "V", "scenes": [{"slideIndex": 0, "narration": "Erste Szene."}, {"slideIndex": 1, "narration": "Zweite Sz""";
        var result = VideoScriptService.AnalyzeResponse(raw, 3);

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.WasRepaired, Is.True);
        Assert.That(result.Value!.Narrations[0], Is.EqualTo("Erste Szene."));
    }

    [Test]
    public void Analyze_PlainText_IsNoJson()
    {
        var result = VideoScriptService.AnalyzeResponse("Leider kann ich das nicht.", 3);
        Assert.That(result.Value, Is.Null);
        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.NoJson));
    }

    [Test]
    public async Task Generate_MissingScenes_FilledFromNotesOrBullets()
    {
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""[{"title": "KI-Video", "scenes": [{"slideIndex": 0, "narration": "Hallo und willkommen."}]}]""");
        var presentation = SamplePresentation();
        presentation.Slides[2].Notes = string.Empty;
        var service = new VideoScriptService(llm.Object, _tempDir);

        var result = await service.GenerateAsync(presentation, "Rückfall");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.FilledFromNotes, Is.EqualTo(2));
        var video = result.Video!;
        Assert.That(video.Title, Is.EqualTo("KI-Video"));
        Assert.That(video.Scenes, Has.Count.EqualTo(3));
        Assert.That(video.Scenes[0].Narration, Is.EqualTo("Hallo und willkommen."));
        Assert.That(video.Scenes[1].Narration, Does.StartWith("Jede Zelle ist von einer Membran"));
        Assert.That(video.Scenes[2].Narration, Is.EqualTo("Mitochondrien. Kraftwerke der Zelle. Eigene DNA."));
        Assert.That(video.Slides.Select(s => s.Id), Is.EqualTo(new[] { "s0", "s1", "s2" }));
        Assert.That(video.Scenes.Select(s => s.SlideId), Is.EqualTo(new[] { "s0", "s1", "s2" }));
        Assert.That(video.PresentationHash, Is.EqualTo(VideoDocument.ComputePresentationHash(presentation)));
        Assert.That(video.NarrationSource, Is.EqualTo(VideoNarrationSource.Ai));
        Assert.That(video.SourceDocuments, Is.EqualTo(new[] { "zelle.txt" }));

        // Die Folien sind eine Kopie: Änderungen an der Präsentation wirken nicht ins Video
        presentation.Slides[1].Title = "Geändert";
        Assert.That(video.Slides[1].Title, Is.EqualTo("Aufbau der Zelle"));
    }

    [Test]
    public async Task Generate_NoUsableText_FailsWithRawResponse()
    {
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""[{"title": "Leer", "scenes": []}]""");
        var service = new VideoScriptService(llm.Object, _tempDir);

        var result = await service.GenerateAsync(SamplePresentation(), "x");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.RawResponse, Does.Contain("Leer"));
    }

    [Test]
    public void CreateFromNotes_WithoutAi_UsesNotes()
    {
        var llm = new Mock<ILlmService>(MockBehavior.Strict);
        var service = new VideoScriptService(llm.Object, _tempDir);

        Assert.That(VideoScriptService.HasUsableNotes(SamplePresentation()), Is.True);
        Assert.That(VideoScriptService.HasUsableNotes(SamplePresentation(withNotes: false)), Is.False);

        var video = service.CreateFromNotes(SamplePresentation(), "x");
        Assert.That(video.NarrationSource, Is.EqualTo(VideoNarrationSource.Notes));
        Assert.That(video.Scenes[2].Narration, Does.StartWith("Mitochondrien erzeugen"));
        Assert.That(video.Title, Is.EqualTo("Zellbiologie"));
    }

    [Test]
    public void FallbackNarration_TitleSlideWithoutNotes()
    {
        var text = VideoScriptService.FallbackNarration(new PresentationSlide { Kind = SlideKind.Title, Title = "Optik", Subtitle = "Licht und Linsen" });
        Assert.That(text, Is.EqualTo("Willkommen! Es geht um Optik. Licht und Linsen."));
    }

    // ---------- Folien aktualisieren ----------

    [Test]
    public void RefreshSlides_KeepsNarrationsOfMatchingSlides_NewSlidesFromNotes_ReturnsRemoved()
    {
        var service = new VideoScriptService(new FakeLlmService(), _tempDir);
        var video = service.CreateFromNotes(SamplePresentation(), "x");
        video.Scenes[1].Narration = "Eigener Text zur Zelle.";
        var removedScene = video.Scenes[2];

        var changed = SamplePresentation();
        changed.Slides.RemoveAt(2);
        changed.Slides.Add(new PresentationSlide { Id = "s3", Title = "Ribosomen", Bullets = { "Eiweißfabriken" }, Notes = "Ribosomen bauen Eiweiße aus Aminosäuren zusammen." });
        changed.Slides[1].Bullets.Add("Zellwand");

        Assert.That(VideoDocument.ComputePresentationHash(changed), Is.Not.EqualTo(video.PresentationHash));
        var removed = service.RefreshSlides(video, changed);

        Assert.That(removed, Is.EqualTo(new[] { removedScene }));
        Assert.That(video.Scenes, Has.Count.EqualTo(3));
        Assert.That(video.Scenes[1].Narration, Is.EqualTo("Eigener Text zur Zelle."));
        Assert.That(video.Slides[1].Bullets, Does.Contain("Zellwand"));
        Assert.That(video.Scenes[2].Narration, Does.StartWith("Ribosomen bauen"));
        Assert.That(video.PresentationHash, Is.EqualTo(VideoDocument.ComputePresentationHash(changed)));
    }

    // ---------- Speicherung und Audio ----------

    [Test]
    public async Task SaveAndLoad_RoundTrip_PerProfile_WithoutTempFile()
    {
        var service = new VideoScriptService(new FakeLlmService(), _tempDir);
        var library = new VideoLibrary();
        library.Items.Add(service.CreateFromNotes(SamplePresentation(), "x"));

        await service.SaveLibraryAsync("Testprofil", library);
        var loaded = await service.LoadLibraryAsync("Testprofil");
        var other = await service.LoadLibraryAsync("Anderes Profil");

        Assert.That(File.Exists(Path.Combine(_tempDir, "Videos", "Testprofil.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempDir, "Videos", "Testprofil.json.tmp")), Is.False);
        Assert.That(loaded.Items.Single().Scenes, Has.Count.EqualTo(3));
        Assert.That(loaded.Items.Single().Slides[1].Bullets, Has.Count.EqualTo(3));
        Assert.That(other.Items, Is.Empty);
    }

    [Test]
    public async Task Load_BrokenFile_ReturnsEmpty_AndScenesAlignedWithSlides()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Videos"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Videos", "Testprofil.json"), "{ kaputt");
        var service = new VideoScriptService(new FakeLlmService(), _tempDir);
        Assert.That((await service.LoadLibraryAsync("Testprofil")).Items, Is.Empty);

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "Videos", "Testprofil.json"),
            """{"Items": [{"Id": "v1", "Title": "Alt", "Slides": [{"Id": "a", "Title": "A"}, {"Id": "b", "Title": "B"}], "Scenes": [{"Narration": "Nur eine."}]}]}""");
        var video = (await service.LoadLibraryAsync("Testprofil")).Items.Single();
        Assert.That(video.Scenes, Has.Count.EqualTo(2));
        Assert.That(video.Scenes[1].SlideId, Is.EqualTo("b"));
    }

    [Test]
    public void Paths_StayInsideVideoFolder()
    {
        var service = new VideoScriptService(new FakeLlmService(), _tempDir);
        var video = new VideoDocument { Id = @"..\..\böse" };
        var scene = new VideoScene { Id = "x", AudioFileName = @"..\..\secret.wav" };
        video.Scenes.Add(scene);

        var dir = service.GetVideoDirectory("Testprofil", video);
        Assert.That(Path.GetFullPath(dir), Does.StartWith(Path.GetFullPath(Path.Combine(_tempDir, "Videos", "Testprofil"))));
        Assert.That(VideoScriptService.VideoFolderName(video), Is.EqualTo("bse"));
        Assert.That(service.GetSceneAudioPath("Testprofil", video, scene), Is.Null);
        Assert.That(Path.GetFileName(service.GetTargetSceneAudioPath("Testprofil", video, scene)), Is.EqualTo("szene_x.wav"));
    }

    internal static void WriteWav(string path, double seconds, int sampleRate = 24000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[(int)(sampleRate * seconds) * 2];
        using var stream = File.Create(path);
        WavAudio.WriteHeader(stream, sampleRate, 1, 16, data.Length);
        stream.Write(data);
    }

    [Test]
    public async Task AssembleAudio_ExactStarts_SilentSceneGetsPause_NoPartFile()
    {
        var service = new VideoScriptService(new FakeLlmService(), _tempDir);
        var video = service.CreateFromNotes(SamplePresentation(), "x");
        video.Scenes[1].Narration = string.Empty; // Folie ohne Ton
        foreach (var (scene, seconds) in new[] { (video.Scenes[0], 2.0), (video.Scenes[2], 1.5) })
        {
            var path = service.GetTargetSceneAudioPath("Testprofil", video, scene);
            WriteWav(path, seconds);
            scene.AudioFileName = Path.GetFileName(path);
            scene.AudioTextHash = scene.ComputeTextHash();
        }

        var assembly = await service.AssembleAudioAsync("Testprofil", video);

        var gap = VideoScriptService.PauseBetweenScenesSeconds;
        var silent = VideoScriptService.SilentSceneSeconds;
        Assert.That(assembly.SceneStarts[0], Is.EqualTo(0));
        Assert.That(assembly.SceneStarts[1], Is.EqualTo(2.0 + gap).Within(0.01));
        Assert.That(assembly.SceneStarts[2], Is.EqualTo(2.0 + gap + silent + gap).Within(0.01));
        Assert.That(assembly.DurationSeconds, Is.EqualTo(2.0 + gap + silent + gap + 1.5).Within(0.01));
        var dir = service.GetVideoDirectory("Testprofil", video);
        Assert.That(File.Exists(Path.Combine(dir, VideoScriptService.CombinedAudioFileName)), Is.True);
        Assert.That(Directory.GetFiles(dir, "*.part"), Is.Empty);
    }

    [Test]
    public void AssembleAudio_MissingOrOutdatedScene_Throws_GermanMessage()
    {
        var service = new VideoScriptService(new FakeLlmService(), _tempDir);
        var video = service.CreateFromNotes(SamplePresentation(), "x");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => service.AssembleAudioAsync("Testprofil", video));
        Assert.That(ex!.Message, Does.Contain("Folie 1").And.Contain("Audiodatei"));
    }

    [Test]
    public void DeleteAllAudio_RemovesFolder_AndClearsInfo()
    {
        var service = new VideoScriptService(new FakeLlmService(), _tempDir);
        var video = service.CreateFromNotes(SamplePresentation(), "x");
        var path = service.GetTargetSceneAudioPath("Testprofil", video, video.Scenes[0]);
        WriteWav(path, 0.2);
        video.Scenes[0].AudioFileName = Path.GetFileName(path);
        video.AudioFileName = "video.wav";

        service.DeleteAllAudio("Testprofil", video);

        Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.False);
        Assert.That(video.Scenes[0].HasAudio, Is.False);
        Assert.That(video.HasAudioFile, Is.False);
    }

    [Test]
    public void SceneHashes_DetectChanges()
    {
        var scene = new VideoScene { Narration = "Text", AudioFileName = "a.wav" };
        scene.AudioTextHash = scene.ComputeTextHash();
        var video = new VideoDocument { Scenes = { scene }, AudioFileName = "video.wav" };
        video.AudioScenesHash = video.ComputeScenesHash();

        Assert.That(scene.NeedsAudio, Is.False);
        Assert.That(video.IsAudioOutdated, Is.False);

        scene.Narration = "Neuer Text";
        Assert.That(scene.NeedsAudio, Is.True);
        Assert.That(video.IsAudioOutdated, Is.True);
        Assert.That(new VideoScene { Narration = "" }.NeedsAudio, Is.False, "Szene ohne Text braucht kein Audio");
    }

    // ---------- Fake-KI ----------

    [Test]
    public void FakeAi_Order_AfterReportDataTableAudio_BeforeOthers()
    {
        var prompt = VideoScriptService.BuildPrompt(SamplePresentation());
        Assert.That(FakeVideoScriptResponse.IsVideoScriptPrompt(prompt), Is.True);
        Assert.That(FakeReportResponse.IsReportPrompt(prompt), Is.False);
        Assert.That(FakeDataTableResponse.IsDataTablePrompt(prompt), Is.False);
        Assert.That(FakeAudioScriptResponse.IsAudioScriptPrompt(prompt), Is.False);
        Assert.That(FakeVideoScriptResponse.IsVideoScriptPrompt(ReportService.BuildPrompt(ReportTemplate.Custom, "Erstelle ein Video-Skript als JSON")), Is.False);
        Assert.That(FakeVideoScriptResponse.IsVideoScriptPrompt(AudioScriptService.BuildPrompt(AudioScriptFormat.Summary, AudioScriptLength.Short, "Video-Skript")), Is.False);
        Assert.That(FakeVideoScriptResponse.IsVideoScriptPrompt("Erstelle eine Mindmap als JSON-Array"), Is.False);
    }

    [Test]
    public async Task FakeAi_BothPaths_DeliverScenePerSlide_EvenWithMindmapInTitles()
    {
        var presentation = SamplePresentation();
        presentation.Slides[1].Title = "Mindmap und Quiz";

        // FakeLlmService
        var viaFake = await new VideoScriptService(new FakeLlmService(), _tempDir).GenerateAsync(presentation, "x");

        // Fake-Zweig des Dispatchers
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.LoadAiConfigAsync()).ReturnsAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi });
        var viaDispatcher = await new VideoScriptService(new LlmServiceDispatcher(storage.Object, new HttpClient()), _tempDir).GenerateAsync(presentation, "x");

        foreach (var result in new[] { viaFake, viaDispatcher })
        {
            Assert.That(result.IsSuccess, Is.True, result.RawResponse);
            Assert.That(result.FilledFromNotes, Is.EqualTo(0));
            Assert.That(result.Video!.Title, Is.EqualTo("Testmodus: Videoübersicht"));
            Assert.That(result.Video.Scenes[1].Narration, Does.Contain("Mindmap und Quiz").And.Contain("Zellkern"));
        }
    }
}
