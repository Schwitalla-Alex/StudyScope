using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// VideoViewModel: Erstellen (Präsentation des Profils, Sprechernotizen ohne KI, neue Folien nur fürs Video),
/// „Präsentation wurde geändert“ samt „Folien aktualisieren“, Bearbeiten, Löschen, Wiedergabe mit der Systemstimme
/// (Zeitsteuerung über eine eigene Uhr, Pause, Sprünge, Stichpunkte, Folie ohne Text), Gemini-Audio je Szene (nur
/// geänderte Szenen neu, Abbruch ohne halbe Dateien), Dateiwiedergabe nach Zeitmarken, Koordinator und Profilwechsel.
/// </summary>
[TestFixture]
public class VideoViewModelTests
{
    private const string Profile = "Testprofil";
    private const string DummyKey = "DUMMY_TEST_KEY_VIDEO_789";
    private string _tempDir = null!;
    private JsonStorageService _storage = null!;
    private CountingLlm _llm = null!;
    private VideoScriptService _videoService = null!;
    private PresentationService _presentationService = null!;
    private FakeLiveSpeech _live = null!;
    private FakeFileSpeech _gemini = null!;
    private FakePlayer _player = null!;
    private ManualTime _time = null!;
    private PlaybackCoordinator _coordinator = null!;
    private VideoViewModel _vm = null!;

    [SetUp]
    public async Task SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeVideoVm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new JsonStorageService(_tempDir);
        _llm = new CountingLlm();
        _videoService = new VideoScriptService(_llm, _tempDir);
        _presentationService = new PresentationService(_llm, _tempDir);
        _live = new FakeLiveSpeech();
        _gemini = new FakeFileSpeech();
        _player = new FakePlayer();
        _time = new ManualTime();
        _coordinator = new PlaybackCoordinator();
        _vm = new VideoViewModel(_videoService, _presentationService, new FakeDocuments(), _storage,
            new ITextToSpeechService[] { _live, _gemini }, _player, _coordinator, _time,
            (span, _) => { _time.Advance(span.TotalSeconds); return Task.CompletedTask; });
        await _presentationService.SavePresentationAsync(Profile, VideoScriptServiceTests.SamplePresentation());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<VideoDocument> CreateVideoAsync(bool gemini = false, bool notes = false)
    {
        if (gemini)
            await _storage.SaveSpeechSettingsAsync(Profile, new SpeechSettings { Provider = SpeechProviderType.GeminiTts, GeminiApiKey = DummyKey });
        await _vm.LoadForProfileAsync(Profile);
        _vm.UseNotesDirectly = notes;
        await _vm.GenerateAsync(Array.Empty<DocumentItem>());
        Assert.That(_vm.IsError, Is.False, _vm.StatusMessage);
        return _vm.SelectedVideo!;
    }

    // ---------- Erstellen ----------

    [Test]
    public async Task Load_WithPresentation_FormOpen_OffersNotes()
    {
        await _vm.LoadForProfileAsync(Profile);

        Assert.That(_vm.IsFormOpen, Is.True);
        Assert.That(_vm.HasProfilePresentation, Is.True);
        Assert.That(_vm.SlideSource, Is.EqualTo(VideoSlideSource.ProfilePresentation));
        Assert.That(_vm.CanUseNotes, Is.True);
        Assert.That(_vm.CanGenerate, Is.True);
    }

    [Test]
    public async Task Generate_FromProfilePresentation_WithFakeAi_SavesAndSelects()
    {
        var video = await CreateVideoAsync();

        Assert.That(_llm.Calls, Is.EqualTo(1));
        Assert.That(_vm.StatusMessage, Does.StartWith("✓ Video"));
        Assert.That(video.Scenes, Has.Count.EqualTo(3));
        Assert.That(video.OwnSlides, Is.False);
        Assert.That(_vm.IsFormOpen, Is.False);
        Assert.That(_vm.HasVideo, Is.True);
        Assert.That((await _videoService.LoadLibraryAsync(Profile)).Items.Single().Id, Is.EqualTo(video.Id));
        Assert.That(_vm.IsPresentationChanged, Is.False);
    }

    [Test]
    public async Task Generate_UseNotesDirectly_WithoutAiCall()
    {
        var video = await CreateVideoAsync(notes: true);

        Assert.That(_llm.Calls, Is.EqualTo(0));
        Assert.That(video.NarrationSource, Is.EqualTo(VideoNarrationSource.Notes));
        Assert.That(video.Scenes[1].Narration, Does.StartWith("Jede Zelle"));
    }

    [Test]
    public async Task Generate_NewSlides_OnlyForVideo_ProfilePresentationUnchanged()
    {
        await _vm.LoadForProfileAsync(Profile);
        var before = VideoDocument.ComputePresentationHash(await _presentationService.LoadPresentationAsync(Profile));
        _vm.SlideSource = VideoSlideSource.NewPresentation;

        await _vm.GenerateAsync(Array.Empty<DocumentItem>());
        Assert.That(_vm.IsError, Is.True, "ohne aktivierte Dokumente");
        Assert.That(_vm.StatusMessage, Does.Contain("mindestens ein Dokument"));

        await _vm.GenerateAsync(new[] { new DocumentItem { FileName = "zelle.txt", FilePath = "zelle.txt", IsSelected = true } });
        Assert.That(_vm.IsError, Is.False, _vm.StatusMessage);
        var video = _vm.SelectedVideo!;
        Assert.That(video.OwnSlides, Is.True);
        Assert.That(video.Slides, Is.Not.Empty);
        Assert.That(_llm.Calls, Is.EqualTo(2), "Folien + Sprechtexte");
        Assert.That(VideoDocument.ComputePresentationHash(await _presentationService.LoadPresentationAsync(Profile)), Is.EqualTo(before));
        Assert.That(_vm.IsPresentationChanged, Is.False, "eigene Folien werden nie als geändert gemeldet");
    }

    [Test]
    public async Task PresentationChanged_HintThenRefreshSlides()
    {
        var video = await CreateVideoAsync();
        await _vm.SetNarrationAsync(1, "Eigener Sprechtext.");

        var changed = VideoScriptServiceTests.SamplePresentation();
        changed.Slides.Add(new PresentationSlide { Id = "s9", Title = "Neu", Notes = "Eine neue Folie mit ausführlicher Sprechernotiz für den Test." });
        await _presentationService.SavePresentationAsync(Profile, changed);
        await _vm.EnsureLoadedForProfileAsync(Profile);

        Assert.That(_vm.IsPresentationChanged, Is.True);

        await _vm.RefreshSlidesAsync();

        Assert.That(_vm.IsPresentationChanged, Is.False);
        Assert.That(video.Scenes, Has.Count.EqualTo(4));
        Assert.That(video.Scenes[1].Narration, Is.EqualTo("Eigener Sprechtext."));
        Assert.That(video.Scenes[3].Narration, Does.StartWith("Eine neue Folie"));

        await _presentationService.DeletePresentationAsync(Profile);
        await _vm.EnsureLoadedForProfileAsync(Profile);
        Assert.That(_vm.IsPresentationMissing, Is.True);
        Assert.That(_vm.IsPresentationChanged, Is.False);
    }

    [Test]
    public async Task Rename_Delete_RemovesAudioFolder()
    {
        var video = await CreateVideoAsync();
        var dir = _videoService.GetVideoDirectory(Profile, video);
        VideoScriptServiceTests.WriteWav(Path.Combine(dir, "szene_x.wav"), 0.1);

        Assert.That(await _vm.RenameAsync(video.Id, "Neuer Titel"), Is.True);
        Assert.That(video.Title, Is.EqualTo("Neuer Titel"));

        await _vm.DeleteAsync(video.Id);
        Assert.That(Directory.Exists(dir), Is.False);
        Assert.That(_vm.Items, Is.Empty);
        Assert.That(_vm.IsFormOpen, Is.True);
    }

    // ---------- Systemstimme ----------

    [Test]
    public async Task LivePlayback_SpeaksEveryScene_InOrder_ThenEnds()
    {
        var video = await CreateVideoAsync();
        Assert.That(_vm.CanPlay, Is.True, _vm.PlayDisabledReason);

        await _vm.PlayAsync();

        var expected = video.Scenes.Select(s => s.Narration).ToList();
        Assert.That(string.Join(" ", _live.Spoken.Select(s => s.Text)), Is.EqualTo(string.Join(" ", expected)));
        Assert.That(_live.Spoken.All(s => s.Voice == "de-1"), Is.True);
        Assert.That(_vm.IsPlaying, Is.False);
        Assert.That(_vm.IsEnded, Is.True);
        Assert.That(_vm.CurrentSceneIndex, Is.EqualTo(2));
        Assert.That(_vm.Progress, Is.EqualTo(1));
        Assert.That(_vm.NowPlayingTitle, Is.Null);
    }

    [Test]
    public async Task LivePlayback_TimeDrivesPositionAndBullets_PauseStopsClock_JumpsWork()
    {
        await CreateVideoAsync();
        _live.Block = true;

        var play = _vm.PlayAsync();
        await _live.WaitForSpeakingAsync();
        Assert.That(_vm.CurrentSceneIndex, Is.EqualTo(0));
        Assert.That(_vm.NowPlayingTitle, Is.Not.Null);

        // Sprung zur Folie 2 während der Wiedergabe
        var next = _vm.NextAsync();
        await _live.WaitForSpeakingAsync();
        Assert.That(_vm.CurrentSceneIndex, Is.EqualTo(1));
        Assert.That(_vm.VisibleBulletCount, Is.EqualTo(0), "Stichpunkte erscheinen erst nach dem Folienwechsel");

        _time.Advance(1.0);
        _vm.Tick();
        var start = _vm.Timeline.StartOf(1);
        Assert.That(_vm.Position, Is.EqualTo(start + 1.0).Within(0.01));
        Assert.That(_vm.VisibleBulletCount, Is.EqualTo(1));

        await _vm.PauseAsync();
        Assert.That(_live.PauseCount, Is.EqualTo(1));
        _time.Advance(30);
        _vm.Tick();
        Assert.That(_vm.Position, Is.EqualTo(start + 1.0).Within(0.01), "Pause hält die Uhr an");

        await _vm.TogglePlayPauseAsync();
        Assert.That(_live.ResumeCount, Is.EqualTo(1));
        _time.Advance(4.0); // aktiv 5 s von ca. 7,5 s: alle Stichpunkte innerhalb der ersten 70 %
        _vm.Tick();
        Assert.That(_vm.VisibleBulletCount, Is.EqualTo(3));
        Assert.That(_vm.ProgressText, Does.Contain("Folie 2 von 3"));

        await _vm.StopAsync();
        await Task.WhenAll(play, next).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(_vm.IsPlaying, Is.False);
        Assert.That(_vm.CurrentSceneIndex, Is.EqualTo(1), "Stopp behält die Folie");
        Assert.That(_vm.VisibleBulletCount, Is.EqualTo(3), "außerhalb der Wiedergabe alle Stichpunkte");
    }

    [Test]
    public async Task LivePlayback_VoiceSlowerThanEstimate_SceneGrows_BulletsNeverDisappear()
    {
        await CreateVideoAsync();
        _live.Block = true;
        await _vm.GoToSceneAsync(1);

        var play = _vm.PlayAsync();
        await _live.WaitForSpeakingAsync();
        var estimated = _vm.Timeline.DurationOf(1);

        _time.Advance(estimated * 0.6);
        _vm.Tick();
        var bulletsBefore = _vm.VisibleBulletCount;
        Assert.That(bulletsBefore, Is.GreaterThanOrEqualTo(2));

        // Die Stimme spricht viel länger als geschätzt: die Szene wächst mit, die Position läuft weiter
        _time.Advance(estimated * 2);
        _vm.Tick();
        Assert.That(_vm.Timeline.DurationOf(1), Is.GreaterThan(estimated * 2.5));
        Assert.That(_vm.Position - _vm.Timeline.StartOf(1), Is.EqualTo(estimated * 2.6).Within(0.05));
        Assert.That(_vm.VisibleBulletCount, Is.GreaterThanOrEqualTo(bulletsBefore), "eingeblendete Stichpunkte bleiben sichtbar");
        Assert.That(_vm.CurrentSceneIndex, Is.EqualTo(1));

        await _vm.StopAsync();
        await play.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task LivePlayback_SceneWithoutNarration_ShownForMinimumTime()
    {
        var video = await CreateVideoAsync();
        await _vm.SetNarrationAsync(1, "");
        Assert.That(video.Scenes[1].HasNarration, Is.False);

        var before = _time.Ticks;
        await _vm.PlayAsync();

        Assert.That(_live.Spoken.Count, Is.EqualTo(2), "Folie ohne Text wird nicht gesprochen");
        Assert.That(TimeSpan.FromTicks(_time.Ticks - before).TotalSeconds, Is.GreaterThanOrEqualTo(3.0));
        Assert.That(_vm.IsEnded, Is.True);
    }

    [Test]
    public async Task Seek_Live_StartsAtSceneBeginning()
    {
        await CreateVideoAsync();
        await _vm.SeekAsync(0.99);
        Assert.That(_vm.CurrentSceneIndex, Is.EqualTo(2));
        Assert.That(_vm.Position, Is.EqualTo(_vm.Timeline.StartOf(2)));
    }

    [Test]
    public async Task Presenting_StartAndEsc()
    {
        await CreateVideoAsync();
        await _vm.StartPresentingAsync(1);
        Assert.That(_vm.IsPresenting, Is.True);
        Assert.That(_live.Spoken.First().Text, Is.EqualTo(_vm.SelectedVideo!.Scenes[1].Narration));

        await _vm.ExitPresentingAsync();
        Assert.That(_vm.IsPresenting, Is.False);
        Assert.That(_vm.IsPlaying, Is.False);
    }

    // ---------- Gemini TTS ----------

    [Test]
    public async Task Gemini_BeforeAudio_PlayExplains_EstimateForAllScenes()
    {
        await CreateVideoAsync(gemini: true);

        Assert.That(_vm.CanPlay, Is.False);
        Assert.That(_vm.PlayDisabledReason, Does.Contain("erzeugen Sie zuerst das Audio"));
        Assert.That(_vm.CanGenerateAudio, Is.True, _vm.GenerateAudioDisabledReason);
        Assert.That(_vm.EstimateText(false), Does.StartWith("3 Szene(n)").And.Contain("MB (WAV)").And.Contain("US-Dollar"));
    }

    [Test]
    public async Task Gemini_GenerateAudio_OneRequestPerScene_ExactTimings_PlaysFile()
    {
        var video = await CreateVideoAsync(gemini: true);

        await _vm.GenerateAudioAsync();

        Assert.That(_vm.IsError, Is.False, _vm.StatusMessage);
        Assert.That(_gemini.Texts, Is.EqualTo(video.Scenes.Select(s => s.Narration)));
        Assert.That(_gemini.LastContext!.Settings.GeminiApiKey, Is.EqualTo(DummyKey));
        Assert.That(video.Scenes.All(s => s.HasAudio && !s.NeedsAudio), Is.True);
        Assert.That(video.HasAudioFile, Is.True);
        Assert.That(video.IsAudioOutdated, Is.False);
        Assert.That(video.AudioSceneStarts, Has.Count.EqualTo(3));
        Assert.That(video.AudioSceneStarts[1], Is.EqualTo(video.Scenes[0].AudioDurationSeconds + VideoScriptService.PauseBetweenScenesSeconds).Within(0.01));
        Assert.That(File.Exists(_vm.SelectedAudioFilePath), Is.True);

        Assert.That(_vm.UsesFilePlayback, Is.True);
        Assert.That(_vm.CanPlay, Is.True, _vm.PlayDisabledReason);
        await _vm.PlayAsync();
        Assert.That(_player.Played.Single().Path, Is.EqualTo(_vm.SelectedAudioFilePath));

        _player.Report(video.AudioSceneStarts[2] + 0.1, false);
        Assert.That(_vm.CurrentSceneIndex, Is.EqualTo(2));
        Assert.That(_vm.ProgressText, Does.Contain("Folie 3 von 3").And.Contain("Audiodatei"));

        await _vm.PreviousAsync();
        Assert.That(_player.Played.Last().Start, Is.EqualTo(video.AudioSceneStarts[1]));

        await _vm.SeekAsync(0.5);
        Assert.That(_player.Played.Last().Start, Is.EqualTo(video.AudioDurationSeconds * 0.5).Within(0.01), "Datei: Springen an jede Stelle");

        await _vm.SetRateAsync(1.5);
        Assert.That(_player.Rate, Is.EqualTo(1.5));

        _player.Report(video.AudioDurationSeconds, true);
        Assert.That(_vm.IsPlaying, Is.False);
        Assert.That(_vm.IsEnded, Is.True);
    }

    [Test]
    public async Task Gemini_EditOneScene_OnlyThatSceneRegenerated()
    {
        var video = await CreateVideoAsync(gemini: true);
        await _vm.GenerateAudioAsync();
        var untouched = await File.ReadAllBytesAsync(_videoService.GetSceneAudioPath(Profile, video, video.Scenes[0])!);
        _gemini.Texts.Clear();

        await _vm.SetNarrationAsync(2, "Ganz neuer Text für die letzte Folie.");

        Assert.That(video.IsAudioOutdated, Is.True);
        Assert.That(_vm.UsesFilePlayback, Is.False);
        Assert.That(_vm.PlayDisabledReason, Does.Contain("nur geänderte Szenen"));
        Assert.That(_vm.ScenesNeedingAudio, Has.Count.EqualTo(1));
        Assert.That(_vm.EstimateText(false), Does.StartWith("1 von 3 Szenen"));
        Assert.That(_vm.EstimateText(true), Does.StartWith("3 Szene(n)"));

        await _vm.GenerateAudioAsync();

        Assert.That(_gemini.Texts, Is.EqualTo(new[] { "Ganz neuer Text für die letzte Folie." }));
        Assert.That(video.IsAudioOutdated, Is.False);
        Assert.That(await File.ReadAllBytesAsync(_videoService.GetSceneAudioPath(Profile, video, video.Scenes[0])!), Is.EqualTo(untouched));
        Assert.That(_vm.StatusMessage, Does.Contain("1 Szene(n) neu erzeugt"));

        // Alle neu (z. B. andere Stimme)
        _gemini.Texts.Clear();
        await _vm.GenerateAudioAsync(all: true);
        Assert.That(_gemini.Texts, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Gemini_CancelDuringSecondScene_FirstSceneKept_NoCombinedFile_NoPartFiles()
    {
        var video = await CreateVideoAsync(gemini: true);
        _gemini.BlockAtCall = 2;

        var generation = _vm.GenerateAudioAsync();
        await _gemini.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(_vm.IsGeneratingAudio, Is.True);
        Assert.That(_vm.AudioProgressText, Does.Contain("Szene 2 von 3"));
        _vm.CancelAudioGeneration();
        await generation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(_vm.StatusMessage, Does.Contain("abgebrochen").And.Contain("1 von 3"));
        Assert.That(video.Scenes[0].HasAudio, Is.True);
        Assert.That(video.Scenes[1].HasAudio, Is.False);
        Assert.That(video.HasAudioFile, Is.False);
        var dir = _videoService.GetVideoDirectory(Profile, video);
        Assert.That(Directory.GetFiles(dir, "*.part"), Is.Empty);
        Assert.That((await _videoService.LoadLibraryAsync(Profile)).Items.Single().Scenes[0].HasAudio, Is.True, "fertige Szene gespeichert");

        // Beim nächsten Mal nur die übrigen Szenen
        _gemini.BlockAtCall = 0;
        _gemini.Texts.Clear();
        await _vm.GenerateAudioAsync();
        Assert.That(_gemini.Texts, Has.Count.EqualTo(2));
        Assert.That(video.HasAudioFile, Is.True);
    }

    [Test]
    public async Task Gemini_Error_GermanMessage()
    {
        await CreateVideoAsync(gemini: true);
        _gemini.Fail = new LlmServiceException("Das Kontingent für Gemini TTS ist erschöpft (HTTP 429).", "Gemini TTS", 429, "RESOURCE_EXHAUSTED");

        await _vm.GenerateAudioAsync();

        Assert.That(_vm.IsError, Is.True);
        Assert.That(_vm.StatusMessage, Does.Contain("Kontingent"));
        Assert.That(_vm.StatusDetails, Is.EqualTo("RESOURCE_EXHAUSTED"));
    }

    [Test]
    public async Task SystemVoice_GenerateAudioDisabled_WithHint()
    {
        await CreateVideoAsync();
        Assert.That(_vm.CanGenerateAudio, Is.False);
        Assert.That(_vm.GenerateAudioDisabledReason, Does.Contain("Systemstimme ist kein Dateiexport"));
    }

    // ---------- Koordination und Profilwechsel ----------

    [Test]
    public async Task Coordinator_StartingVideoStopsAudio_AndViceVersa()
    {
        await CreateVideoAsync();
        var audioPlaying = true;
        var audioStopped = 0;
        _coordinator.Register(PlaybackCoordinator.AudioParticipant, () => audioPlaying, () => { audioStopped++; audioPlaying = false; return Task.CompletedTask; });

        await _vm.PlayAsync();
        Assert.That(audioStopped, Is.EqualTo(1));

        _live.Block = true;
        var play = _vm.PlayAsync();
        await _live.WaitForSpeakingAsync();
        await _coordinator.StopOthersAsync(PlaybackCoordinator.AudioParticipant);
        await play.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(_vm.IsPlaying, Is.False);
    }

    [Test]
    public async Task AudioViewModel_RegistersWithCoordinator()
    {
        var audioVm = new AudioViewModel(new AudioScriptService(new FakeLlmService(), _tempDir), new FakeDocuments(), _storage,
            new ITextToSpeechService[] { _live }, _player, _coordinator);
        await audioVm.LoadForProfileAsync(Profile);
        audioVm.Format = AudioScriptFormat.Summary;
        await audioVm.GenerateAsync(new[] { new DocumentItem { FileName = "a.txt", FilePath = "a.txt", IsSelected = true } });
        await CreateVideoAsync();

        _live.Block = true;
        var audioPlay = audioVm.PlayAsync();
        await _live.WaitForSpeakingAsync();
        Assert.That(audioVm.IsPlaying, Is.True);

        var videoPlay = _vm.PlayAsync();
        await _live.WaitForSpeakingAsync();
        Assert.That(audioVm.IsPlaying, Is.False, "Video beendet das Audio");
        Assert.That(_vm.IsPlaying, Is.True);

        await _vm.StopAsync();
        await Task.WhenAll(audioPlay, videoPlay).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ProfileChange_StopsPlayback()
    {
        await CreateVideoAsync();
        _live.Block = true;
        var play = _vm.PlayAsync();
        await _live.WaitForSpeakingAsync();

        await _vm.HandleProfileChangedAsync("Anderes Profil");
        await play.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(_vm.IsPlaying, Is.False);
        Assert.That(_live.StopCount, Is.GreaterThanOrEqualTo(1));
    }

    // ---------- Hilfsklassen ----------

    private sealed class ManualTime : TimeProvider
    {
        public long Ticks { get; private set; } = 1_000_000;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public void Advance(double seconds) => Ticks += (long)(seconds * TimeSpan.TicksPerSecond);
    }

    private sealed class CountingLlm : ILlmService
    {
        private readonly FakeLlmService _inner = new();
        public int Calls { get; private set; }
        public Task<bool> ValidateConnectionAsync(AiConfig? config = null) => Task.FromResult(true);
        public Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
        {
            Calls++;
            return _inner.AskQuestionAsync(prompt, contextChunks, cancellationToken);
        }
    }

    private sealed class FakeLiveSpeech : ILiveSpeechService
    {
        private TaskCompletionSource<bool>? _current;
        private TaskCompletionSource _speaking = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(string Text, string Voice, double Rate)> Spoken { get; } = new();
        public bool Block { get; set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StopCount { get; private set; }

        public SpeechProviderType Provider => SpeechProviderType.SystemVoice;
        public string DisplayName => "Systemstimme (kostenlos, offline)";
        public bool SupportsLivePlayback => true;
        public bool SupportsFileExport => false;

        public Task<SpeechAvailability> GetAvailabilityAsync(SpeechContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(SpeechAvailability.Ready("Bereit."));
        public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpeechVoice>>(new[] { new SpeechVoice("de-1", "Katja", "de-DE") });

        public Task<bool> SpeakAsync(string text, string voiceId, double rate, CancellationToken cancellationToken = default)
        {
            Spoken.Add((text, voiceId, rate));
            _current?.TrySetResult(false);
            if (!Block) return Task.FromResult(true);
            _current = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _speaking.TrySetResult();
            return _current.Task;
        }

        public async Task WaitForSpeakingAsync()
        {
            await _speaking.Task.WaitAsync(TimeSpan.FromSeconds(5));
            _speaking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task PauseAsync() { PauseCount++; return Task.CompletedTask; }
        public Task ResumeAsync() { ResumeCount++; return Task.CompletedTask; }
        public Task StopAsync()
        {
            StopCount++;
            _current?.TrySetResult(false);
            return Task.CompletedTask;
        }
    }

    /// <summary>Gemini-Ersatz: schreibt eine gültige WAV (0,1 s je Wort) und merkt sich die Texte je Anfrage.</summary>
    private sealed class FakeFileSpeech : IAudioFileSpeechService
    {
        private int _calls;
        public List<string> Texts { get; } = new();
        public SpeechContext? LastContext { get; private set; }
        public LlmServiceException? Fail { get; set; }
        public int BlockAtCall { get; set; }
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SpeechProviderType Provider => SpeechProviderType.GeminiTts;
        public string DisplayName => "Gemini TTS (Test)";
        public bool SupportsLivePlayback => false;
        public bool SupportsFileExport => true;

        public Task<SpeechAvailability> GetAvailabilityAsync(SpeechContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(GeminiTextToSpeechService.ResolveApiKey(context).Source == GeminiTextToSpeechService.KeySource.None
                ? SpeechAvailability.Unavailable("Kein API-Schlüssel für Gemini TTS.", GeminiTextToSpeechService.MissingKeyHint)
                : SpeechAvailability.Ready("Bereit."));

        public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SpeechVoice>>(Array.Empty<SpeechVoice>());

        public async Task<AudioFileResult> SynthesizeToFileAsync(AudioSummary summary, SpeechContext context, string filePath,
            IProgress<SpeechSynthesisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            _calls++;
            if (Fail != null) throw Fail;
            if (BlockAtCall > 0 && _calls == BlockAtCall)
            {
                Blocked.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var text = summary.Segments.Single().Text;
            Texts.Add(text);
            var seconds = Math.Round(AudioScriptFormats.CountWords(text) * 0.1, 2);
            VideoScriptServiceTests.WriteWav(filePath, seconds);
            progress?.Report(new SpeechSynthesisProgress(1, 1));
            return new AudioFileResult(seconds, new[] { 0.0 }, "Gemini TTS (Test)");
        }

        public Task<string> TestConnectionAsync(SpeechContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult("✓ Verbindung erfolgreich.");
    }

    private sealed class FakePlayer : IAudioFilePlayer
    {
        public List<(string Path, double Start)> Played { get; } = new();
        public double Rate { get; private set; } = 1;
        public bool IsReady => true;
        public event Action<double, bool>? PositionChanged;

        public Task PlayAsync(string filePath, double startSeconds, double rate)
        {
            Played.Add((filePath, startSeconds));
            Rate = rate;
            return Task.CompletedTask;
        }

        public Task PauseAsync() => Task.CompletedTask;
        public Task ResumeAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task SetRateAsync(double rate) { Rate = rate; return Task.CompletedTask; }
        public void Report(double seconds, bool ended) => PositionChanged?.Invoke(seconds, ended);
    }

    private sealed class FakeDocuments : IDocumentService
    {
        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(2);
        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250) =>
            Task.FromResult(new List<string> { "Die Zelle ist die kleinste Einheit des Lebens.", "Mitochondrien erzeugen Energie für die Zelle." });
    }
}
