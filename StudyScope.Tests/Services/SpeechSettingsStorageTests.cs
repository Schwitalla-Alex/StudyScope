using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Tests für die Sprachausgabe-Einstellungen: je Profil (maßgeblich) mit Rückfall auf die globale config.json,
/// Verschlüsselung des eigenen Gemini-Schlüssels und Zusammenspiel mit der KI-Konfiguration in derselben Datei.
/// Außerdem die Schlüsselwahl von Gemini TTS (Profil-Chat-KI zuerst, sonst eigener Schlüssel).
/// </summary>
[TestFixture]
public class SpeechSettingsStorageTests
{
    private const string DummyKey = "DUMMY_TEST_KEY_SPEECH_123";
    private const string DummyChatKey = "DUMMY_TEST_KEY_CHAT_456";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeSpeech_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task Load_WithoutAnything_ReturnsSystemVoiceDefault()
    {
        var settings = await new JsonStorageService(_tempDir).LoadSpeechSettingsAsync("Testprofil");

        Assert.That(settings.Provider, Is.EqualTo(SpeechProviderType.SystemVoice));
        Assert.That(settings.GeminiModel, Is.EqualTo(GeminiTtsInfo.DefaultModel));
        Assert.That(settings.Rate, Is.EqualTo(1.0));
    }

    [Test]
    public async Task Save_StoresPerProfile_EncryptsKey_AndKeepsDocuments()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveDocumentsAsync("Testprofil", new[] { new DocumentItem { FileName = "a.txt", FilePath = "a.txt" } });

        await storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings
        {
            Provider = SpeechProviderType.GeminiTts,
            GeminiApiKey = DummyKey,
            GeminiModel = "gemini-test-tts",
            Rate = 1.25
        });

        var fresh = new JsonStorageService(_tempDir);
        var loaded = await fresh.LoadSpeechSettingsAsync("Testprofil");
        Assert.That(loaded.Provider, Is.EqualTo(SpeechProviderType.GeminiTts));
        Assert.That(loaded.GeminiApiKey, Is.EqualTo(DummyKey), "im Speicher Klartext");
        Assert.That(loaded.GeminiModel, Is.EqualTo("gemini-test-tts"));
        Assert.That(loaded.Rate, Is.EqualTo(1.25));
        Assert.That(await fresh.LoadDocumentsAsync("Testprofil"), Has.Count.EqualTo(1));

        var profileJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "Profiles", "Testprofil.json"));
        var configJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "config.json"));
        Assert.That(profileJson, Does.Not.Contain(DummyKey), "auf der Platte verschlüsselt");
        Assert.That(configJson, Does.Not.Contain(DummyKey));
    }

    [Test]
    public async Task Load_ProfileWithoutSetting_FallsBackToGlobal()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings { Provider = SpeechProviderType.GeminiTts, GeminiApiKey = DummyKey });

        var other = await storage.LoadSpeechSettingsAsync("Zweites Testprofil");

        Assert.That(other.Provider, Is.EqualTo(SpeechProviderType.GeminiTts));
        Assert.That(other.GeminiApiKey, Is.EqualTo(DummyKey));
    }

    [Test]
    public async Task SaveAiConfig_KeepsGlobalSpeechSettings_AndViceVersa()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.OllamaLocal, ModelName = "testmodell" });
        await storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings { Provider = SpeechProviderType.GeminiTts });
        await storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.MistralAi, ApiKey = DummyChatKey });

        var ai = await storage.LoadAiConfigAsync();
        var speech = await storage.LoadSpeechSettingsAsync("Profil ohne Einstellung");

        Assert.That(ai.SelectedProvider, Is.EqualTo(AiProviderType.MistralAi));
        Assert.That(ai.ApiKey, Is.EqualTo(DummyChatKey));
        Assert.That(speech.Provider, Is.EqualTo(SpeechProviderType.GeminiTts), "KI-Speichern darf die Sprachausgabe nicht löschen");
    }

    [Test]
    public async Task SaveSpeech_DoesNotChangeGlobalAiConfig()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.GoogleGemini, ApiKey = DummyChatKey, ModelName = "gemini-test" });

        await storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings { Rate = 1.5 });

        var ai = await storage.LoadAiConfigAsync();
        Assert.That(ai.SelectedProvider, Is.EqualTo(AiProviderType.GoogleGemini));
        Assert.That(ai.ApiKey, Is.EqualTo(DummyChatKey));
        Assert.That(ai.ModelName, Is.EqualTo("gemini-test"));
    }

    [Test]
    public async Task Load_ClampsRateAndFillsMissingModel()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings { Rate = 9, GeminiModel = " " });

        var loaded = await storage.LoadSpeechSettingsAsync("Testprofil");

        Assert.That(loaded.Rate, Is.EqualTo(SpeechSettings.MaxRate));
        Assert.That(loaded.GeminiModel, Is.EqualTo(GeminiTtsInfo.DefaultModel));
    }

    // ---------- Schlüsselwahl Gemini TTS ----------

    [Test]
    public void ResolveApiKey_PrefersGeminiChatKey_ThenOwnKey()
    {
        var own = new SpeechSettings { GeminiApiKey = DummyKey };

        var fromChat = GeminiTextToSpeechService.ResolveApiKey(new SpeechContext(own, new AiConfig { SelectedProvider = AiProviderType.GoogleGemini, ApiKey = DummyChatKey }));
        var fromOwn = GeminiTextToSpeechService.ResolveApiKey(new SpeechContext(own, new AiConfig { SelectedProvider = AiProviderType.OllamaLocal }));
        var otherCloud = GeminiTextToSpeechService.ResolveApiKey(new SpeechContext(new SpeechSettings(), new AiConfig { SelectedProvider = AiProviderType.OpenAiGpt4, ApiKey = DummyChatKey }));

        Assert.That(fromChat, Is.EqualTo((DummyChatKey, GeminiTextToSpeechService.KeySource.ChatSettings)));
        Assert.That(fromOwn, Is.EqualTo((DummyKey, GeminiTextToSpeechService.KeySource.SpeechSettings)));
        Assert.That(otherCloud.Source, Is.EqualTo(GeminiTextToSpeechService.KeySource.None), "Schlüssel anderer Anbieter gelten nicht für Gemini");
    }

    [Test]
    public async Task GeminiAvailability_DependsOnKey()
    {
        var service = new GeminiTextToSpeechService();

        var without = await service.GetAvailabilityAsync(new SpeechContext(new SpeechSettings(), null));
        var with = await service.GetAvailabilityAsync(new SpeechContext(new SpeechSettings { GeminiApiKey = DummyKey }, null));

        Assert.That(without.Status, Is.EqualTo(SpeechAvailabilityStatus.Unavailable));
        Assert.That(without.Hint, Does.Contain("Einstellungen"));
        Assert.That(with.IsAvailable, Is.True);
        Assert.That((await service.GetVoicesAsync()).Select(v => v.Id), Does.Contain(GeminiTtsInfo.DefaultVoicePrimary));
    }
}
