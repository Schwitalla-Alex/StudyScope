using System.Text.Json.Nodes;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Speicherung der Websuche: globale Einstellungen in config.json (Schlüssel verschlüsselt, Knoten bleibt bei allen
/// anderen Speicherfunktionen erhalten) und Zustand je Profil (standardmäßig aus, kein globaler Rückfall, bleibt beim
/// Speichern anderer Profilfelder erhalten).
/// </summary>
[TestFixture]
public class WebSearchStorageTests
{
    private const string DummyKey = "DUMMY_TEST_KEY_TAVILY_456";
    private const string DummyAiKey = "DUMMY_TEST_KEY_AI_789";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeWebSearch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string ConfigText() => File.ReadAllText(Path.Combine(_tempDir, "config.json"));

    private static WebSearchSettings Sample() => new()
    {
        Provider = WebSearchProviderType.Tavily,
        TavilyApiKey = DummyKey,
        SearxngUrl = "http://localhost:8888",
        MaxResults = 7
    };

    [Test]
    public async Task Load_WithoutConfig_IsNotConfigured()
    {
        var settings = await new JsonStorageService(_tempDir).LoadWebSearchSettingsAsync();
        Assert.That(settings.Provider, Is.EqualTo(WebSearchProviderType.None));
        Assert.That(settings.MaxResults, Is.EqualTo(WebSearchSettings.DefaultMaxResults));
    }

    [Test]
    public async Task Save_Load_Roundtrip_KeyEncryptedOnDisk()
    {
        await new JsonStorageService(_tempDir).SaveWebSearchSettingsAsync(Sample());

        var loaded = await new JsonStorageService(_tempDir).LoadWebSearchSettingsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Provider, Is.EqualTo(WebSearchProviderType.Tavily));
            Assert.That(loaded.TavilyApiKey, Is.EqualTo(DummyKey), "im Speicher Klartext");
            Assert.That(loaded.SearxngUrl, Is.EqualTo("http://localhost:8888"));
            Assert.That(loaded.MaxResults, Is.EqualTo(7));
        });

        var text = ConfigText();
        Assert.That(text, Does.Not.Contain(DummyKey), "auf der Platte verschlüsselt");
        Assert.That(text, Does.Contain("\"Provider\": \"Tavily\""), "Anbieter als Text gespeichert");
    }

    [Test]
    public async Task Save_DoesNotChangeOriginalObject()
    {
        var settings = Sample();
        await new JsonStorageService(_tempDir).SaveWebSearchSettingsAsync(settings);
        Assert.That(settings.TavilyApiKey, Is.EqualTo(DummyKey));
    }

    [Test]
    public async Task SavingTwice_DoesNotEncryptTwice()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveWebSearchSettingsAsync(Sample());
        var loaded = await storage.LoadWebSearchSettingsAsync();
        await storage.SaveWebSearchSettingsAsync(loaded);

        Assert.That((await new JsonStorageService(_tempDir).LoadWebSearchSettingsAsync()).TavilyApiKey, Is.EqualTo(DummyKey));
    }

    [Test]
    public async Task Load_ClampsInvalidValues()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"),
            """{ "WebSearchSettings": { "Provider": "Searxng", "SearxngUrl": "http://localhost:8888", "MaxResults": 99 } }""");
        var loaded = await new JsonStorageService(_tempDir).LoadWebSearchSettingsAsync();
        Assert.That(loaded.MaxResults, Is.EqualTo(WebSearchSettings.MaxMaxResults));
        Assert.That(loaded.Provider, Is.EqualTo(WebSearchProviderType.Searxng));
    }

    [Test]
    public async Task Load_BrokenEntry_FallsBackToDefault()
    {
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), """{ "WebSearchSettings": { "Provider": "Unbekannt" } }""");
        var loaded = await new JsonStorageService(_tempDir).LoadWebSearchSettingsAsync();
        Assert.That(loaded.Provider, Is.EqualTo(WebSearchProviderType.None));
    }

    [Test]
    public async Task OtherSaveFunctions_KeepWebSearchSettings()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveWebSearchSettingsAsync(Sample());

        await storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.MistralAi, ApiKey = DummyAiKey });
        await storage.SaveExportSettingsAsync(new ExportSettings { FfmpegPath = "C:\\ffmpeg" });
        await storage.SaveUiSettingsAsync(new UiSettings { Theme = ThemeMode.Dark });
        await storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings());
        await storage.SaveOutputLanguageAsync("Testprofil", "en");
        await storage.SaveAiConfigForProfileAsync("Testprofil", new AiConfig());

        var loaded = await new JsonStorageService(_tempDir).LoadWebSearchSettingsAsync();
        Assert.That(loaded.Provider, Is.EqualTo(WebSearchProviderType.Tavily));
        Assert.That(loaded.TavilyApiKey, Is.EqualTo(DummyKey));
        Assert.That(loaded.MaxResults, Is.EqualTo(7));
    }

    [Test]
    public async Task SaveWebSearchSettings_KeepsAiAndOtherSettings()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveAiConfigAsync(new AiConfig { SelectedProvider = AiProviderType.MistralAi, ApiKey = DummyAiKey });
        await storage.SaveExportSettingsAsync(new ExportSettings { FfmpegPath = "C:\\ffmpeg" });
        await storage.SaveUiSettingsAsync(new UiSettings { Theme = ThemeMode.Dark });

        await storage.SaveWebSearchSettingsAsync(Sample());

        var fresh = new JsonStorageService(_tempDir);
        var ai = await fresh.LoadAiConfigAsync();
        Assert.That(ai.SelectedProvider, Is.EqualTo(AiProviderType.MistralAi));
        Assert.That(ai.ApiKey, Is.EqualTo(DummyAiKey));
        Assert.That((await fresh.LoadExportSettingsAsync()).FfmpegPath, Is.EqualTo("C:\\ffmpeg"));
        Assert.That((await fresh.LoadUiSettingsAsync()).Theme, Is.EqualTo(ThemeMode.Dark));
    }

    // ---------------- Zustand je Profil ----------------

    [Test]
    public async Task ProfileState_DefaultIsOff()
    {
        var state = await new JsonStorageService(_tempDir).LoadWebSearchStateAsync("Testprofil");
        Assert.That(state, Is.EqualTo(WebSearchProfileState.Off));
    }

    [Test]
    public async Task ProfileState_Roundtrip_OnlyThisProfile_NoGlobalFallback()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveWebSearchStateAsync("Testprofil", new WebSearchProfileState(true, "Tavily"));

        var fresh = new JsonStorageService(_tempDir);
        Assert.That(await fresh.LoadWebSearchStateAsync("Testprofil"), Is.EqualTo(new WebSearchProfileState(true, "Tavily")));
        Assert.That(await fresh.LoadWebSearchStateAsync("Anderes Profil"), Is.EqualTo(WebSearchProfileState.Off),
            "ein anderes bzw. neues Profil sucht nie im Internet, bevor es dort eingeschaltet wurde");

        var configPath = Path.Combine(_tempDir, "config.json");
        if (File.Exists(configPath))
            Assert.That(File.ReadAllText(configPath), Does.Not.Contain("WebSearchEnabled"), "kein globaler Wert");
    }

    [Test]
    public async Task ProfileState_SurvivesOtherProfileSaves()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveWebSearchStateAsync("Testprofil", new WebSearchProfileState(true, "SearXNG"));

        await storage.SaveDocumentsAsync("Testprofil", new[] { new DocumentItem { FileName = "a.txt", FilePath = "a.txt" } });
        await storage.SaveSpeechSettingsAsync("Testprofil", new SpeechSettings { Rate = 1.5 });
        await storage.SaveOutputLanguageAsync("Testprofil", "en");
        await storage.SaveAiConfigForProfileAsync("Testprofil", new AiConfig { SelectedProvider = AiProviderType.MistralAi, ApiKey = DummyAiKey });

        Assert.That(await new JsonStorageService(_tempDir).LoadWebSearchStateAsync("Testprofil"), Is.EqualTo(new WebSearchProfileState(true, "SearXNG")));
    }

    [Test]
    public async Task ProfileState_Save_KeepsOtherProfileFields()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveDocumentsAsync("Testprofil", new[] { new DocumentItem { FileName = "a.txt", FilePath = "a.txt" } });
        await storage.SaveOutputLanguageAsync("Testprofil", "en");

        await storage.SaveWebSearchStateAsync("Testprofil", new WebSearchProfileState(false, "Tavily"));

        var fresh = new JsonStorageService(_tempDir);
        Assert.That((await fresh.LoadDocumentsAsync("Testprofil")).Select(d => d.FileName), Is.EqualTo(new[] { "a.txt" }));
        Assert.That(await fresh.LoadOutputLanguageAsync("Testprofil"), Is.EqualTo("en"));
        Assert.That(await fresh.LoadWebSearchStateAsync("Testprofil"), Is.EqualTo(new WebSearchProfileState(false, "Tavily")));
    }

    [Test]
    public async Task ProfileFile_ContainsBothFields()
    {
        var storage = new JsonStorageService(_tempDir);
        await storage.SaveWebSearchStateAsync("Testprofil", new WebSearchProfileState(true, "Tavily"));

        var file = Directory.GetFiles(Path.Combine(_tempDir, "Profiles"), "*.json").Single();
        var node = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.That(node["WebSearchEnabled"]!.GetValue<bool>(), Is.True);
        Assert.That(node["WebSearchNoticeAcceptedFor"]!.GetValue<string>(), Is.EqualTo("Tavily"));
    }
}
