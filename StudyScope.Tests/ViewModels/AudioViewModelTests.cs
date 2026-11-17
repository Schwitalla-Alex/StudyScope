using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Tests für das AudioViewModel: Skript erstellen und bearbeiten, Löschen samt Audiodatei, Wahl der Sprachausgabe
/// (je Profil gespeichert), Verfügbarkeitsprüfung und Freigabe der Knöpfe für alle Kombinationen aus Chat-KI
/// (lokal/Cloud), Sprachausgabe (System/Gemini) und Schlüssel (ja/nein) sowie der Player (Segmente, Vor/Zurück,
/// Pause, Stopp, Tempo).
/// </summary>
[TestFixture]
public class AudioViewModelTests
{
    private const string Profile = "Testprofil";
    private const string DummyKey = "DUMMY_TEST_KEY_TTS_789";
    private string _tempDir = null!;
    private JsonStorageService _storage = null!;
    private AudioScriptService _audioService = null!;
    private FakeLiveSpeech _live = null!;
    private AudioViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeAudioVm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new JsonStorageService(_tempDir);
        _audioService = new AudioScriptService(new FakeLlmService(), _tempDir);
        _live = new FakeLiveSpeech();
        _viewModel = CreateViewModel();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AudioViewModel CreateViewModel() =>
        new(_audioService, new FakeDocumentService(), _storage, new ITextToSpeechService[] { _live, new GeminiTextToSpeechService() });

    private static List<DocumentItem> Documents(bool selected = true) => new()
    {
        new DocumentItem { FileName = "skript.txt", FilePath = "skript.txt", IsSelected = selected }
    };

    private async Task<AudioSummary> CreateScriptAsync(AudioScriptFormat format = AudioScriptFormat.Summary)
    {
        await _viewModel.LoadForProfileAsync(Profile);
        _viewModel.Format = format;
        await _viewModel.GenerateAsync(Documents());
        Assert.That(_viewModel.IsError, Is.False, _viewModel.StatusMessage);
        return _viewModel.SelectedSummary!;
    }

    // ---------- Laden und Erstellen ----------

    [Test]
    public async Task Load_WithoutItems_OpensForm_AndDisablesPlayer()
    {
        await _viewModel.LoadForProfileAsync(Profile);

        Assert.That(_viewModel.Items, Is.Empty);
        Assert.That(_viewModel.IsFormOpen, Is.True);
        Assert.That(_viewModel.CanPlay, Is.False);
        Assert.That(_viewModel.PlayDisabledReason, Does.Contain("Skript"));
        Assert.That(_viewModel.SelectedProvider, Is.EqualTo(SpeechProviderType.SystemVoice), "Standard ist die Systemstimme");
    }

    [Test]
    public async Task Generate_WithFakeAi_CreatesSavesAndSelects()
    {
        _viewModel.CustomTitle = "Mein Podcast";
        var summary = await CreateScriptAsync(AudioScriptFormat.Podcast);

        Assert.That(summary.Title, Is.EqualTo("Mein Podcast"));
        Assert.That(summary.Speakers, Is.EqualTo(new[] { "Moderatorin", "Experte" }));
        Assert.That(summary.SourceDocuments, Is.EqualTo(new[] { "skript.txt" }));
        Assert.That(_viewModel.IsFormOpen, Is.False);
        Assert.That(_viewModel.StatusMessage, Does.StartWith("✓"));

        var reloaded = await _audioService.LoadLibraryAsync(Profile);
        Assert.That(reloaded.Items.Single().Title, Is.EqualTo("Mein Podcast"));
    }

    [Test]
    public async Task Generate_WithoutSelectedDocuments_ShowsError()
    {
        await _viewModel.LoadForProfileAsync(Profile);
        await _viewModel.GenerateAsync(Documents(selected: false));

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.Items, Is.Empty);
    }

    [Test]
    public async Task Edit_Segments_AreSavedImmediately()
    {
        var summary = await CreateScriptAsync(AudioScriptFormat.Podcast);
        var count = summary.Segments.Count;

        await _viewModel.SetSegmentTextAsync(0, "  Geänderter Einstieg.  ");
        await _viewModel.SetSegmentSpeakerAsync(0, "Experte");
        var inserted = await _viewModel.AddSegmentAfterAsync(0);
        await _viewModel.DeleteSegmentAsync(count); // letztes (nach dem Einfügen)

        var saved = (await _audioService.LoadLibraryAsync(Profile)).Items.Single();
        Assert.That(saved.Segments[0].Text, Is.EqualTo("Geänderter Einstieg."));
        Assert.That(saved.Segments[0].Speaker, Is.EqualTo("Experte"));
        Assert.That(inserted, Is.EqualTo(1));
        Assert.That(saved.Segments[1].Speaker, Is.EqualTo("Moderatorin"), "neuer Abschnitt wechselt den Sprecher");
        Assert.That(saved.Segments, Has.Count.EqualTo(count));
    }

    [Test]
    public async Task SetSegmentText_Empty_DeletesSegment()
    {
        var summary = await CreateScriptAsync();
        var count = summary.Segments.Count;

        await _viewModel.SetSegmentTextAsync(1, "   ");

        Assert.That(_viewModel.SelectedSummary!.Segments, Has.Count.EqualTo(count - 1));
    }

    [Test]
    public async Task Delete_RemovesItemAndItsAudioFile()
    {
        var summary = await CreateScriptAsync();
        var file = Path.Combine(_audioService.GetAudioDirectory(Profile), "beitrag.wav");
        await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3 });
        summary.AudioFileName = "beitrag.wav";

        await _viewModel.DeleteAsync(summary.Id);

        Assert.That(File.Exists(file), Is.False);
        Assert.That(_viewModel.Items, Is.Empty);
        Assert.That(_viewModel.IsFormOpen, Is.True);
        Assert.That((await _audioService.LoadLibraryAsync(Profile)).Items, Is.Empty);
    }

    [Test]
    public async Task Rename_ChangesTitle()
    {
        var summary = await CreateScriptAsync();
        Assert.That(await _viewModel.RenameAsync(summary.Id, "Neuer Titel"), Is.True);
        Assert.That(await _viewModel.RenameAsync(summary.Id, " "), Is.False);
        Assert.That((await _audioService.LoadLibraryAsync(Profile)).Items.Single().Title, Is.EqualTo("Neuer Titel"));
    }

    [Test]
    public async Task ExportScript_WritesTextAndMarkdown()
    {
        await CreateScriptAsync(AudioScriptFormat.Podcast);
        var txt = Path.Combine(_tempDir, "skript.txt");
        var md = Path.Combine(_tempDir, "skript.md");

        Assert.That(await _viewModel.ExportScriptAsync(AudioScriptExportFormat.Text, txt), Is.True);
        Assert.That(await _viewModel.ExportScriptAsync(AudioScriptExportFormat.Markdown, md), Is.True);

        Assert.That(await File.ReadAllTextAsync(txt), Does.Contain("Moderatorin: "));
        Assert.That(await File.ReadAllTextAsync(md), Does.Contain("**Moderatorin:**"));
        Assert.That(_viewModel.SuggestFileName("md"), Does.EndWith(".md"));
    }

    // ---------- Sprachausgabe: Auswahl und Verfügbarkeit ----------

    private static IEnumerable<TestCaseData> AvailabilityCombinations()
    {
        // Chat-KI, Chat-Schlüssel, Sprachausgabe, eigener TTS-Schlüssel → Abspielen, Audiodatei, lokaler Hinweis
        foreach (var chat in new[] { AiProviderType.OllamaLocal, AiProviderType.LmStudioLocal, AiProviderType.GoogleGemini, AiProviderType.OpenAiGpt4 })
        {
            var isLocal = chat is AiProviderType.OllamaLocal or AiProviderType.LmStudioLocal;
            foreach (var chatKey in isLocal ? new[] { false } : new[] { false, true })
            {
                foreach (var ownKey in new[] { false, true })
                {
                    yield return new TestCaseData(chat, chatKey, SpeechProviderType.SystemVoice, ownKey, true, false, isLocal)
                        .SetName($"{chat}_ChatKey{chatKey}_System_OwnKey{ownKey}");

                    var geminiKey = ownKey || (chat == AiProviderType.GoogleGemini && chatKey);
                    yield return new TestCaseData(chat, chatKey, SpeechProviderType.GeminiTts, ownKey, false, geminiKey, isLocal)
                        .SetName($"{chat}_ChatKey{chatKey}_Gemini_OwnKey{ownKey}");
                }
            }
        }
    }

    [TestCaseSource(nameof(AvailabilityCombinations))]
    public async Task Availability_EnablesButtons_ForAllCombinations(
        AiProviderType chat, bool chatKey, SpeechProviderType provider, bool ownKey,
        bool expectPlay, bool expectGenerateAudio, bool expectLocalHint)
    {
        await _storage.SaveAiConfigForProfileAsync(Profile, new AiConfig { SelectedProvider = chat, ApiKey = chatKey ? DummyKey : string.Empty, ModelName = "testmodell" });
        await _storage.SaveSpeechSettingsAsync(Profile, new SpeechSettings { Provider = provider, GeminiApiKey = ownKey ? DummyKey : string.Empty });

        await CreateScriptAsync();

        Assert.That(_viewModel.SelectedProvider, Is.EqualTo(provider));
        Assert.That(_viewModel.CanPlay, Is.EqualTo(expectPlay), _viewModel.PlayDisabledReason);
        Assert.That(_viewModel.CanGenerateAudio, Is.EqualTo(expectGenerateAudio), _viewModel.GenerateAudioDisabledReason);
        Assert.That(_viewModel.IsLocalChatAi, Is.EqualTo(expectLocalHint));
        Assert.That(_viewModel.ShowPrivacyHint, Is.EqualTo(provider == SpeechProviderType.GeminiTts));
        Assert.That(_viewModel.CanGenerateScript, Is.True, "das Skript schreibt immer die Chat-KI");

        // Deaktivierte Knöpfe haben immer eine Begründung
        if (expectPlay)
            Assert.That(_viewModel.PlayDisabledReason, Is.Null);
        else
            Assert.That(_viewModel.PlayDisabledReason, Is.Not.Empty);
        if (!expectGenerateAudio)
        {
            var reason = _viewModel.GenerateAudioDisabledReason;
            Assert.That(reason, Is.Not.Empty);
            Assert.That(reason, Does.Contain(provider == SpeechProviderType.SystemVoice ? "kein Dateiexport" : "Schlüssel"));
        }

        var geminiAvailable = _viewModel.AvailabilityOf(SpeechProviderType.GeminiTts).IsAvailable;
        Assert.That(geminiAvailable, Is.EqualTo(ownKey || (chat == AiProviderType.GoogleGemini && chatKey)));
    }

    [Test]
    public async Task SystemVoiceUnavailable_DisablesPlay_WithReason_AndCannotBeSelected()
    {
        _live.Availability = SpeechAvailability.Unavailable("Ihre Systemumgebung stellt keine Sprachausgabe bereit.", "Nutzen Sie Gemini TTS.");
        await _storage.SaveSpeechSettingsAsync(Profile, new SpeechSettings { Provider = SpeechProviderType.SystemVoice });
        await CreateScriptAsync();

        Assert.That(_viewModel.CanPlay, Is.False);
        Assert.That(_viewModel.PlayDisabledReason, Does.Contain("keine Sprachausgabe").And.Contain("Gemini TTS"));
        Assert.That(_viewModel.CanSelectProvider(SpeechProviderType.SystemVoice), Is.True, "die aktuelle Auswahl bleibt wählbar");

        // Ohne Gemini-Schlüssel ist Gemini ausgegraut
        Assert.That(_viewModel.CanSelectProvider(SpeechProviderType.GeminiTts), Is.False);
    }

    [Test]
    public async Task SystemVoiceStillChecking_DisablesPlay_WithCheckingReason()
    {
        _live.Availability = SpeechAvailability.Checking();
        await CreateScriptAsync();

        Assert.That(_viewModel.CanPlay, Is.False);
        Assert.That(_viewModel.PlayDisabledReason, Does.Contain("geprüft"));
        Assert.That(_viewModel.CanSelectProvider(SpeechProviderType.SystemVoice), Is.True);
    }

    [Test]
    public async Task SetProvider_SavesPerProfile_AndIgnoresUnavailable()
    {
        await CreateScriptAsync();

        await _viewModel.SetProviderAsync(SpeechProviderType.GeminiTts); // ohne Schlüssel nicht wählbar
        Assert.That(_viewModel.SelectedProvider, Is.EqualTo(SpeechProviderType.SystemVoice));

        await _storage.SaveSpeechSettingsAsync(Profile, new SpeechSettings { GeminiApiKey = DummyKey });
        await _viewModel.EnsureLoadedForProfileAsync(Profile);
        await _viewModel.SetProviderAsync(SpeechProviderType.GeminiTts);

        Assert.That(_viewModel.SelectedProvider, Is.EqualTo(SpeechProviderType.GeminiTts));
        Assert.That((await _storage.LoadSpeechSettingsAsync(Profile)).Provider, Is.EqualTo(SpeechProviderType.GeminiTts));
        Assert.That((await _storage.LoadSpeechSettingsAsync("Anderes Testprofil")).Provider, Is.EqualTo(SpeechProviderType.GeminiTts), "global als Rückfall");
    }

    [Test]
    public async Task SetRate_IsClampedAndSaved()
    {
        await CreateScriptAsync();
        await _viewModel.SetRateAsync(5);

        Assert.That(_viewModel.Rate, Is.EqualTo(SpeechSettings.MaxRate));
        Assert.That((await _storage.LoadSpeechSettingsAsync(Profile)).Rate, Is.EqualTo(SpeechSettings.MaxRate));
    }

    // ---------- Player ----------

    [Test]
    public async Task Play_SpeaksAllSegmentsInOrder_ThenResets()
    {
        var summary = await CreateScriptAsync();

        await _viewModel.PlayAsync();

        var expected = summary.Segments.Select(s => s.Text).ToList();
        Assert.That(_live.Spoken.Select(s => s.Text), Is.EqualTo(expected));
        Assert.That(_viewModel.IsPlaying, Is.False);
        Assert.That(_viewModel.CurrentSegmentIndex, Is.EqualTo(-1), "am Ende ohne Hervorhebung");
    }

    [Test]
    public async Task Play_Podcast_UsesDifferentVoicesPerSpeaker_AndRate()
    {
        _live.Voices = new[]
        {
            new SpeechVoice("de-1", "Katja", "de-DE"),
            new SpeechVoice("de-2", "Conrad", "de-DE"),
            new SpeechVoice("en-1", "Zira", "en-US")
        };
        var summary = await CreateScriptAsync(AudioScriptFormat.Podcast);
        await _viewModel.SetRateAsync(1.25);

        await _viewModel.PlayAsync();

        var hostVoices = _live.Spoken.Where((_, i) => summary.Segments[i].Speaker == "Moderatorin").Select(s => s.Voice).Distinct().ToList();
        var expertVoices = _live.Spoken.Where((_, i) => summary.Segments[i].Speaker == "Experte").Select(s => s.Voice).Distinct().ToList();
        Assert.That(hostVoices, Is.EqualTo(new[] { "de-1" }), "Moderatorin: weibliche deutsche Stimme (Katja)");
        Assert.That(expertVoices, Is.EqualTo(new[] { "de-2" }), "Experte: männliche deutsche Stimme (Conrad)");
        Assert.That(_live.Spoken.All(s => Math.Abs(s.Rate - 1.25) < 0.001), Is.True);
    }

    [Test]
    public async Task Podcast_WindowsVoices_ExpertGetsStefan_WithoutHint()
    {
        _live.Voices = new[]
        {
            new SpeechVoice("hedda", "Microsoft Hedda - German (Germany)", "de-DE"),
            new SpeechVoice("katja", "Microsoft Katja - German (Germany)", "de-DE"),
            new SpeechVoice("stefan", "Microsoft Stefan - German (Germany)", "de-DE")
        };
        await CreateScriptAsync(AudioScriptFormat.Podcast);

        Assert.That(_viewModel.SpeakerRoleName(0), Is.EqualTo("Moderatorin"));
        Assert.That(_viewModel.SpeakerRoleName(1), Is.EqualTo("Experte"));
        Assert.That(_viewModel.VoiceForSpeakerIndex(0), Is.EqualTo("hedda"));
        Assert.That(_viewModel.VoiceForSpeakerIndex(1), Is.EqualTo("stefan"));
        Assert.That(_viewModel.VoiceAssignmentHint, Is.Null);
    }

    [Test]
    public async Task Podcast_NoMaleGermanVoice_UsesOtherGermanVoice_WithHint()
    {
        _live.Voices = new[]
        {
            new SpeechVoice("hedda", "Microsoft Hedda - German (Germany)", "de-DE"),
            new SpeechVoice("katja", "Microsoft Katja - German (Germany)", "de-DE"),
            new SpeechVoice("david", "Microsoft David - English (United States)", "en-US")
        };
        await CreateScriptAsync(AudioScriptFormat.Podcast);

        Assert.That(_viewModel.VoiceForSpeakerIndex(1), Is.EqualTo("katja"), "andere deutsche Stimme statt englischer");
        Assert.That(_viewModel.VoiceAssignmentHint, Does.Contain("Keine männliche deutsche Stimme für „Experte“").And.Contain("„Katja“"));
        if (!OperatingSystem.IsLinux())
            Assert.That(_viewModel.VoiceAssignmentHint, Does.Contain("Stefan").And.Contain("Sprachoptionen"));

        // Manuelle Auswahl bleibt möglich und blendet den Hinweis aus
        await _viewModel.SetSystemVoiceAsync(1, "david");
        Assert.That(_viewModel.VoiceForSpeakerIndex(1), Is.EqualTo("david"));
        Assert.That(_viewModel.VoiceAssignmentHint, Is.Null);
    }

    [Test]
    public async Task Podcast_OnlyOneGermanVoice_HintSaysBothSoundAlike()
    {
        _live.Voices = new[] { new SpeechVoice("katja", "Microsoft Katja - German (Germany)", "de-DE") };
        await CreateScriptAsync(AudioScriptFormat.Podcast);

        Assert.That(_viewModel.VoiceForSpeakerIndex(1), Is.EqualTo("katja"));
        Assert.That(_viewModel.VoiceAssignmentHint, Does.Contain("nur eine deutsche Stimme"));
    }

    [Test]
    public async Task Summary_NoVoiceHint_AndNarratorPrefersFemaleVoice()
    {
        _live.Voices = new[]
        {
            new SpeechVoice("conrad", "Conrad", "de-DE"),
            new SpeechVoice("stefan", "Stefan", "de-DE"),
            new SpeechVoice("katja", "Katja", "de-DE")
        };
        await CreateScriptAsync();

        Assert.That(_viewModel.SpeakerRoleName(0), Is.EqualTo("Sprecherin"));
        Assert.That(_viewModel.VoiceForSpeakerIndex(0), Is.EqualTo("katja"));
        Assert.That(_viewModel.VoiceAssignmentHint, Is.Null);
    }

    [Test]
    public async Task NowPlayingTitle_ShownWhilePlaying_AndProfileChangeStops()
    {
        var summary = await CreateScriptAsync();
        _live.Block = true;

        var playing = _viewModel.PlayAsync();
        await _live.WaitForSpeakingAsync();
        Assert.That(_viewModel.NowPlayingTitle, Is.EqualTo(summary.Title));

        await _viewModel.HandleProfileChangedAsync(Profile);
        Assert.That(_viewModel.IsPlaying, Is.True, "gleiches Profil: Wiedergabe läuft weiter");

        await _viewModel.HandleProfileChangedAsync("Anderes Testprofil");
        await playing;
        Assert.That(_viewModel.IsPlaying, Is.False);
        Assert.That(_viewModel.NowPlayingTitle, Is.Null);
        Assert.That(_live.StopCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task Play_ChosenVoice_IsUsed()
    {
        _live.Voices = new[] { new SpeechVoice("de-1", "Katja", "de-DE"), new SpeechVoice("en-1", "Zira", "en-US") };
        await CreateScriptAsync();
        await _viewModel.SetSystemVoiceAsync(0, "en-1");

        await _viewModel.PlayAsync();

        Assert.That(_live.Spoken.Select(s => s.Voice).Distinct(), Is.EqualTo(new[] { "en-1" }));
    }

    [Test]
    public async Task PauseResumeNextPreviousStop_ControlPlayback()
    {
        var summary = await CreateScriptAsync();
        Assert.That(summary.Segments.Count, Is.GreaterThanOrEqualTo(3));
        _live.Block = true;

        var playing = _viewModel.PlayAsync();
        await _live.WaitForSpeakingAsync();
        Assert.That(_viewModel.IsPlaying, Is.True);
        Assert.That(_viewModel.CurrentSegmentIndex, Is.EqualTo(0));
        Assert.That(_viewModel.CanGoPrevious, Is.False);

        await _viewModel.PauseAsync();
        Assert.That(_viewModel.IsPaused, Is.True);
        Assert.That(_live.PauseCount, Is.EqualTo(1));

        await _viewModel.PlayAsync(); // fortsetzen
        Assert.That(_viewModel.IsPaused, Is.False);
        Assert.That(_live.ResumeCount, Is.EqualTo(1));

        var next = _viewModel.NextAsync();
        await _live.WaitForSpeakingAsync();
        Assert.That(_viewModel.CurrentSegmentIndex, Is.EqualTo(1));
        Assert.That(_live.Spoken.Last().Text, Is.EqualTo(summary.Segments[1].Text));

        var previous = _viewModel.PreviousAsync();
        await _live.WaitForSpeakingAsync();
        Assert.That(_viewModel.CurrentSegmentIndex, Is.EqualTo(0));

        await _viewModel.StopAsync();
        await Task.WhenAll(playing, next, previous);
        Assert.That(_viewModel.IsPlaying, Is.False);
        Assert.That(_viewModel.CurrentSegmentIndex, Is.EqualTo(0), "Stopp behält die Position");
        Assert.That(_live.StopCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task GoToSegment_WhenStopped_OnlyMovesPosition()
    {
        await CreateScriptAsync();

        await _viewModel.GoToSegmentAsync(2);

        Assert.That(_viewModel.CurrentSegmentIndex, Is.EqualTo(2));
        Assert.That(_live.Spoken, Is.Empty);
        Assert.That(_viewModel.Progress, Is.GreaterThan(0).And.LessThan(1));
        Assert.That(_viewModel.ProgressText, Does.StartWith("Abschnitt 3 von"));
    }

    [Test]
    public async Task Play_SpeechError_ShowsGermanMessage()
    {
        await CreateScriptAsync();
        _live.ThrowOnSpeak = new InvalidOperationException("Die gewählte Stimme ist nicht verfügbar.");

        await _viewModel.PlayAsync();

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("Stimme ist nicht verfügbar"));
        Assert.That(_viewModel.IsPlaying, Is.False);
    }

    [Test]
    public async Task Play_WhenNotPossible_ShowsReasonInsteadOfSpeaking()
    {
        await _storage.SaveSpeechSettingsAsync(Profile, new SpeechSettings { Provider = SpeechProviderType.GeminiTts, GeminiApiKey = DummyKey });
        await CreateScriptAsync();

        await _viewModel.PlayAsync();

        Assert.That(_live.Spoken, Is.Empty);
        Assert.That(_viewModel.StatusMessage, Does.Contain("liest nicht direkt vor"));
    }

    // ---------- Hilfsklassen ----------

    private sealed class FakeLiveSpeech : ILiveSpeechService
    {
        private TaskCompletionSource<bool>? _current;
        private TaskCompletionSource _speaking = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SpeechAvailability Availability { get; set; } = SpeechAvailability.Ready("Bereit – 1 deutsche Stimme verfügbar.");
        public IReadOnlyList<SpeechVoice> Voices { get; set; } = new[] { new SpeechVoice("de-1", "Katja", "de-DE") };
        public List<(string Text, string Voice, double Rate)> Spoken { get; } = new();
        public bool Block { get; set; }
        public Exception? ThrowOnSpeak { get; set; }
        public int PauseCount { get; private set; }
        public int ResumeCount { get; private set; }
        public int StopCount { get; private set; }

        public SpeechProviderType Provider => SpeechProviderType.SystemVoice;
        public string DisplayName => "Systemstimme (kostenlos, offline)";
        public bool SupportsLivePlayback => true;
        public bool SupportsFileExport => false;

        public Task<SpeechAvailability> GetAvailabilityAsync(SpeechContext context, CancellationToken cancellationToken = default) => Task.FromResult(Availability);
        public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Voices);

        public Task<bool> SpeakAsync(string text, string voiceId, double rate, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSpeak != null) throw ThrowOnSpeak;
            Spoken.Add((text, voiceId, rate));
            _current?.TrySetResult(false);
            if (!Block) return Task.FromResult(true);

            _current = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _speaking.TrySetResult();
            return _current.Task;
        }

        /// <summary>Wartet, bis eine (neue) Äußerung begonnen hat.</summary>
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

    private sealed class FakeDocumentService : IDocumentService
    {
        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(1);

        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250) =>
            Task.FromResult(new List<string>
            {
                "Die Französische Revolution begann 1789 mit dem Sturm auf die Bastille. Napoleon krönte sich 1804 zum Kaiser. " +
                "Die Erklärung der Menschenrechte wurde im August 1789 verabschiedet."
            });
    }
}
