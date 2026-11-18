using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Audiodatei im AudioViewModel: Kostenschätzung, Erzeugen mit Fortschritt (Abbruch/Fehler ohne halbe Datei, alte
/// Datei bleibt), Abspielen der Datei mit Hervorhebung nach Zeitmarken, WAV-Export, Löschen, „veraltet“ nach
/// Bearbeiten, Verbindung testen – sowie der Einstellungsabschnitt „Sprachausgabe“.
/// </summary>
[TestFixture]
public class AudioFileViewModelTests
{
    private const string Profile = "Testprofil";
    private const string DummyKey = "DUMMY_TEST_KEY_TTS_456";
    private string _tempDir = null!;
    private JsonStorageService _storage = null!;
    private AudioScriptService _audioService = null!;
    private FakeFileSpeech _gemini = null!;
    private FakePlayer _player = null!;
    private AudioViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeAudioFile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storage = new JsonStorageService(_tempDir);
        _audioService = new AudioScriptService(new FakeLlmService(), _tempDir);
        _gemini = new FakeFileSpeech();
        _player = new FakePlayer();
        _viewModel = new AudioViewModel(_audioService, new FakeDocuments(), _storage, new ITextToSpeechService[] { _gemini }, _player);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private async Task<AudioSummary> CreateScriptAsync(AudioScriptFormat format = AudioScriptFormat.Podcast, bool withKey = true)
    {
        await _storage.SaveSpeechSettingsAsync(Profile, new SpeechSettings { Provider = SpeechProviderType.GeminiTts, GeminiApiKey = withKey ? DummyKey : string.Empty });
        await _viewModel.LoadForProfileAsync(Profile);
        _viewModel.Format = format;
        await _viewModel.GenerateAsync(new[] { new DocumentItem { FileName = "skript.txt", FilePath = "skript.txt", IsSelected = true } });
        Assert.That(_viewModel.IsError, Is.False, _viewModel.StatusMessage);
        return _viewModel.SelectedSummary!;
    }

    [Test]
    public async Task Mp3Export_OnlyWithAudioFileAndFfmpeg()
    {
        await CreateScriptAsync();
        Assert.That(_viewModel.CanExportMp3, Is.False);
        Assert.That(_viewModel.Mp3DisabledReason, Does.Contain("Audiodatei erzeugen"));
        await _viewModel.GenerateAudioAsync();
        Assert.That(_viewModel.Mp3DisabledReason, Does.Contain("nicht verfügbar"), "ohne Transcoder");

        var missing = new AudioViewModel(_audioService, new FakeDocuments(), _storage, new ITextToSpeechService[] { _gemini }, _player,
            transcoder: new Services.VideoHtmlExporterTests.FakeTranscoder { Available = false });
        await missing.LoadForProfileAsync(Profile);
        Assert.That(missing.CanExportMp3, Is.False);
        Assert.That(missing.Mp3DisabledReason, Does.Contain("nicht gefunden"));

        var transcoder = new Services.VideoHtmlExporterTests.FakeTranscoder();
        var vm = new AudioViewModel(_audioService, new FakeDocuments(), _storage, new ITextToSpeechService[] { _gemini }, _player, transcoder: transcoder);
        await vm.LoadForProfileAsync(Profile);
        Assert.That(vm.CanExportMp3, Is.True, vm.Mp3DisabledReason);
        Assert.That(vm.Mp3EstimateText, Does.Contain("MP3, 64 kbit/s"));
        var target = Path.Combine(_tempDir, "Beitrag ä.mp3");

        Assert.That(await vm.ExportMp3Async(target), Is.True, vm.StatusMessage);

        Assert.That(File.ReadAllBytes(target), Is.EqualTo(Services.VideoHtmlExporterTests.FakeTranscoder.Bytes));
        Assert.That(transcoder.Converted.Single(), Is.EqualTo(vm.SelectedAudioFilePath));
        Assert.That(vm.StatusMessage, Does.Contain("MP3 exportiert"));
        Assert.That(vm.IsBusy, Is.False);
    }

    [Test]
    public async Task BeforeGenerating_EstimateShown_PlayExplainsMissingFile()
    {
        await CreateScriptAsync();

        Assert.That(_viewModel.CanGenerateAudio, Is.True, _viewModel.GenerateAudioDisabledReason);
        Assert.That(_viewModel.AudioEstimateText, Does.Contain("MB (WAV)").And.Contain("US-Dollar").And.Contain("kostenlosen Stufe"));
        Assert.That(_viewModel.CanPlay, Is.False);
        Assert.That(_viewModel.PlayDisabledReason, Does.Contain("erzeugen Sie zuerst die Audiodatei"));
        Assert.That(_viewModel.CanExportAudio, Is.False);
    }

    [Test]
    public async Task WithoutKey_GenerateDisabled_WithReason()
    {
        await CreateScriptAsync(withKey: false);

        Assert.That(_viewModel.CanGenerateAudio, Is.False);
        Assert.That(_viewModel.GenerateAudioDisabledReason, Does.Contain("Einstellungen › Sprachausgabe"));
    }

    [Test]
    public async Task GenerateAudio_SavesFileAndTimings_ThenPlaysFileWithHighlight()
    {
        var summary = await CreateScriptAsync();
        var progressSeen = new List<double>();
        _viewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AudioViewModel.AudioProgress)) progressSeen.Add(_viewModel.AudioProgress); };

        await _viewModel.GenerateAudioAsync();

        Assert.That(_viewModel.IsError, Is.False, _viewModel.StatusMessage);
        Assert.That(_viewModel.StatusMessage, Does.StartWith("✓ Audiodatei erzeugt"));
        Assert.That(progressSeen, Does.Contain(0.5));
        Assert.That(summary.HasAudioFile, Is.True);
        Assert.That(summary.AudioFileName, Does.EndWith(".wav").And.StartWith("Testmodus"));
        Assert.That(summary.AudioSegmentStarts.Count, Is.EqualTo(summary.Segments.Count));
        Assert.That(summary.IsAudioOutdated, Is.False);
        Assert.That(File.Exists(_viewModel.SelectedAudioFilePath), Is.True);
        Assert.That(_gemini.LastContext!.Settings.GeminiApiKey, Is.EqualTo(DummyKey));

        // Gespeichert?
        var reloaded = (await _audioService.LoadLibraryAsync(Profile)).Items.Single();
        Assert.That(reloaded.AudioDurationSeconds, Is.EqualTo(summary.AudioDurationSeconds));

        // Abspielen über die Datei
        Assert.That(_viewModel.UsesFilePlayback, Is.True);
        Assert.That(_viewModel.CanPlay, Is.True, _viewModel.PlayDisabledReason);
        await _viewModel.PlayAsync();
        Assert.That(_player.Played.Single().Path, Is.EqualTo(_viewModel.SelectedAudioFilePath));
        Assert.That(_viewModel.NowPlayingTitle, Is.EqualTo(summary.Title));

        _player.Report(summary.AudioSegmentStarts[2] + 0.1, false);
        Assert.That(_viewModel.CurrentSegmentIndex, Is.EqualTo(2));
        Assert.That(_viewModel.ProgressText, Does.Contain("Abschnitt 3 von").And.Contain("Audiodatei"));

        await _viewModel.NextAsync();
        Assert.That(_player.Played.Last().Start, Is.EqualTo(summary.AudioSegmentStarts[3]), "Springen = Abspielen ab Startzeit");

        await _viewModel.PauseAsync();
        Assert.That(_player.Paused, Is.EqualTo(1));
        await _viewModel.PlayAsync();
        Assert.That(_player.Resumed, Is.EqualTo(1));

        await _viewModel.SetRateAsync(1.5);
        Assert.That(_player.Rate, Is.EqualTo(1.5));

        _player.Report(summary.AudioDurationSeconds, true);
        Assert.That(_viewModel.IsPlaying, Is.False);
        Assert.That(_viewModel.CurrentSegmentIndex, Is.EqualTo(-1));
    }

    [Test]
    public async Task GenerateAudio_Error_KeepsExistingFile_AndShowsGermanMessage()
    {
        var summary = await CreateScriptAsync();
        await _viewModel.GenerateAudioAsync();
        var path = _viewModel.SelectedAudioFilePath!;
        var before = await File.ReadAllBytesAsync(path);

        _gemini.Fail = new LlmServiceException("Das Kontingent für Gemini TTS ist erschöpft (HTTP 429).", "Gemini TTS", 429, "RESOURCE_EXHAUSTED");
        await _viewModel.GenerateAudioAsync();

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("Kontingent"));
        Assert.That(_viewModel.StatusTips.Single(), Does.Contain("keine halbe Datei"));
        Assert.That(_viewModel.StatusDetails, Is.EqualTo("RESOURCE_EXHAUSTED"));
        Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(before));
        Assert.That(summary.HasAudioFile, Is.True);
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task GenerateAudio_Cancel_ShowsMessage_NoFile()
    {
        var summary = await CreateScriptAsync();
        _gemini.WaitForCancel = true;

        var running = _viewModel.GenerateAudioAsync();
        Assert.That(_viewModel.IsGeneratingAudio, Is.True);
        _viewModel.CancelAudioGeneration();
        await running;

        Assert.That(_viewModel.StatusMessage, Does.Contain("abgebrochen").And.Contain("keine Datei"));
        Assert.That(summary.HasAudioFile, Is.False);
        Assert.That(_viewModel.IsGeneratingAudio, Is.False);
    }

    [Test]
    public async Task EditAfterGenerating_MarksAudioOutdated()
    {
        var summary = await CreateScriptAsync();
        await _viewModel.GenerateAudioAsync();

        await _viewModel.SetSegmentTextAsync(0, "Geänderter Text.");

        Assert.That(summary.IsAudioOutdated, Is.True);
    }

    [Test]
    public async Task ExportAndDeleteAudio()
    {
        var summary = await CreateScriptAsync();
        await _viewModel.GenerateAudioAsync();
        var source = _viewModel.SelectedAudioFilePath!;
        var target = Path.Combine(_tempDir, "export.wav");

        Assert.That(_viewModel.CanExportAudio, Is.True);
        Assert.That(await _viewModel.ExportAudioAsync(target), Is.True);
        Assert.That(await File.ReadAllBytesAsync(target), Is.EqualTo(await File.ReadAllBytesAsync(source)));

        await _viewModel.DeleteAudioFileAsync();
        Assert.That(File.Exists(source), Is.False);
        Assert.That(summary.HasAudioFile, Is.False);
        Assert.That(summary.Segments, Is.Not.Empty, "Skript bleibt");
        Assert.That((await _audioService.LoadLibraryAsync(Profile)).Items.Single().AudioFileName, Is.Empty);
    }

    [Test]
    public async Task RenameThenRegenerate_RemovesOldFile()
    {
        var summary = await CreateScriptAsync();
        await _viewModel.GenerateAudioAsync();
        var oldPath = _viewModel.SelectedAudioFilePath!;

        await _viewModel.RenameAsync(summary.Id, "Neuer Titel");
        await _viewModel.GenerateAudioAsync();

        Assert.That(File.Exists(oldPath), Is.False);
        Assert.That(summary.AudioFileName, Does.StartWith("Neuer_Titel_"));
        Assert.That(File.Exists(_viewModel.SelectedAudioFilePath), Is.True);
    }

    [Test]
    public async Task TestConnection_ShowsResult()
    {
        await CreateScriptAsync();

        await _viewModel.TestSpeechConnectionAsync();
        Assert.That(_viewModel.StatusMessage, Does.Contain("erfolgreich"));

        _gemini.Fail = new LlmServiceException("Der Gemini-API-Schlüssel wurde abgelehnt.", "Gemini TTS", 400);
        await _viewModel.TestSpeechConnectionAsync();
        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("abgelehnt"));
    }

    // ---------- Einstellungsabschnitt „Sprachausgabe“ ----------

    [Test]
    public async Task SpeechSettings_SaveKeepsProviderAndRate_KeyEncryptedOnDisk()
    {
        await _storage.SetLastActiveProfileNameAsync(Profile);
        await _storage.SaveSpeechSettingsAsync(Profile, new SpeechSettings { Provider = SpeechProviderType.GeminiTts, Rate = 1.25, SystemVoicePrimary = "stimme-a" });
        var vm = new SpeechSettingsViewModel(_storage, new ITextToSpeechService[] { _gemini });
        var saved = false;
        vm.Saved += () => saved = true;

        await vm.InitializeAsync();
        Assert.That(vm.ProfileName, Is.EqualTo(Profile));
        Assert.That(vm.CanTestConnection, Is.False, "ohne Schlüssel");
        vm.GeminiApiKey = DummyKey;
        vm.GeminiModel = "  ";
        vm.VoicePrimary = "Aoede";
        vm.VoiceSecondary = "Charon";
        Assert.That(vm.CanTestConnection, Is.True);
        Assert.That(await vm.SaveAsync(), Is.True);

        Assert.That(saved, Is.True);
        var loaded = await _storage.LoadSpeechSettingsAsync(Profile);
        Assert.That(loaded!.GeminiApiKey, Is.EqualTo(DummyKey));
        Assert.That(loaded.GeminiModel, Is.EqualTo(GeminiTtsInfo.DefaultModel), "leer → Standardmodell");
        Assert.That(loaded.GeminiVoicePrimary, Is.EqualTo("Aoede"));
        Assert.That(loaded.GeminiVoiceSecondary, Is.EqualTo("Charon"));
        Assert.That(loaded.Provider, Is.EqualTo(SpeechProviderType.GeminiTts));
        Assert.That(loaded.Rate, Is.EqualTo(1.25));
        Assert.That(loaded.SystemVoicePrimary, Is.EqualTo("stimme-a"));

        foreach (var file in Directory.GetFiles(_tempDir, "*.json", SearchOption.AllDirectories))
            Assert.That(await File.ReadAllTextAsync(file), Does.Not.Contain(DummyKey), $"Klartext-Schlüssel in {file}");
    }

    [Test]
    public async Task SpeechSettings_ChatGeminiKey_IsUsed_AndTestUsesFormValues()
    {
        await _storage.SetLastActiveProfileNameAsync(Profile);
        await _storage.SaveAiConfigForProfileAsync(Profile, new AiConfig { SelectedProvider = AiProviderType.GoogleGemini, ApiKey = "DUMMY_TEST_KEY_CHAT" });
        var vm = new SpeechSettingsViewModel(_storage, new ITextToSpeechService[] { _gemini });
        await vm.InitializeAsync();

        Assert.That(vm.ChatProvidesGeminiKey, Is.True);
        Assert.That(vm.KeySourceText, Does.Contain("KI-Einstellungen"));
        Assert.That(vm.CanTestConnection, Is.True);

        vm.GeminiModel = "anderes-tts-modell";
        await vm.TestConnectionAsync();

        Assert.That(vm.IsSuccess, Is.True, vm.StatusMessage);
        Assert.That(_gemini.LastContext!.Settings.GeminiModel, Is.EqualTo("anderes-tts-modell"), "noch ungespeicherte Formularwerte");
        Assert.That(_gemini.LastContext.ChatConfig!.ApiKey, Is.EqualTo("DUMMY_TEST_KEY_CHAT"));
        Assert.That((await _storage.LoadSpeechSettingsAsync(Profile))!.GeminiModel, Is.Not.EqualTo("anderes-tts-modell"), "Test speichert nicht");
    }

    [Test]
    public void SpeechSettings_VoiceLabel_ShowsGender() =>
        Assert.That(SpeechSettingsViewModel.VoiceLabel("Kore"), Is.EqualTo("Kore (weiblich)"));

    // ---------- Hilfsklassen ----------

    private sealed class FakeFileSpeech : IAudioFileSpeechService
    {
        public LlmServiceException? Fail { get; set; }
        public bool WaitForCancel { get; set; }
        public SpeechContext? LastContext { get; private set; }

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
            progress?.Report(new SpeechSynthesisProgress(0, 2));
            if (WaitForCancel)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Fail != null)
                throw Fail;
            progress?.Report(new SpeechSynthesisProgress(1, 2));
            progress?.Report(new SpeechSynthesisProgress(2, 2));
            await File.WriteAllBytesAsync(filePath, Enumerable.Range(0, 100).Select(i => (byte)i).ToArray(), cancellationToken);
            var starts = summary.Segments.Select((_, i) => i * 2.0).ToList();
            return new AudioFileResult(summary.Segments.Count * 2.0, starts, "Gemini TTS (Test)");
        }

        public Task<string> TestConnectionAsync(SpeechContext context, CancellationToken cancellationToken = default)
        {
            LastContext = context;
            return Fail != null ? Task.FromException<string>(Fail) : Task.FromResult("✓ Verbindung erfolgreich.");
        }
    }

    private sealed class FakePlayer : IAudioFilePlayer
    {
        public List<(string Path, double Start)> Played { get; } = new();
        public int Paused { get; private set; }
        public int Resumed { get; private set; }
        public double Rate { get; private set; } = 1;
        public bool IsReady => true;
        public event Action<double, bool>? PositionChanged;

        public Task PlayAsync(string filePath, double startSeconds, double rate)
        {
            Played.Add((filePath, startSeconds));
            Rate = rate;
            return Task.CompletedTask;
        }

        public Task PauseAsync() { Paused++; return Task.CompletedTask; }
        public Task ResumeAsync() { Resumed++; return Task.CompletedTask; }
        public Task StopAsync() => Task.CompletedTask;
        public Task SetRateAsync(double rate) { Rate = rate; return Task.CompletedTask; }

        public void Report(double seconds, bool ended) => PositionChanged?.Invoke(seconds, ended);
    }

    private sealed class FakeDocuments : IDocumentService
    {
        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(2);
        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250) =>
            Task.FromResult(new List<string> { "Die Zelle ist die kleinste Einheit des Lebens.", "Mitochondrien erzeugen Energie." });
    }
}
