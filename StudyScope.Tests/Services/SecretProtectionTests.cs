using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moq;
using NUnit.Framework;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.Services;

/// <summary>
/// Schutz der API-Schlüssel (ab 1.3.0): Format „ss2:…“, Windows DPAPI, Rückfall im Format von 1.2.0, Lesen aller alten
/// Formate, nicht lesbare Werte (anderer Computer/anderes Konto) und Migration beim Start. Nur erfundene Schlüssel;
/// DPAPI verändert nur die Testdaten, einen Speicher des Betriebssystems gibt es dabei nicht.
/// </summary>
[TestFixture]
public class SecretProtectionTests
{
    private const string Key = "DUMMY_TEST_KEY_SCHUTZ_0001";
    private const string Profile = "Testprofil";

    /// <summary>Auf jedem System nicht lesbar: DPAPI-Präfix mit ungültigem Inhalt (wie von einem anderen Konto).</summary>
    private const string ForeignValue = "ss2:dpapi:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyScope_SecretTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private string ProfileFile => Path.Combine(_dir, "Profiles", Profile + ".json");
    private string ConfigFile => Path.Combine(_dir, "config.json");

    private static ISecretProtector Platform() => SecretProtector.ForCurrentPlatform();

    private static string? Stored(string file, params string[] path)
    {
        var root = JsonNode.Parse(File.ReadAllText(file));
        foreach (var name in path)
            root = root?[name];
        return root?.GetValue<string>();
    }

    private void WriteProfile(string storedAiKey, string? storedGeminiKey = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfileFile)!);
        var node = new JsonObject
        {
            ["Name"] = Profile,
            ["Documents"] = new JsonArray(),
            ["AiSettings"] = new JsonObject
            {
                ["SelectedProvider"] = (int)AiProviderType.GoogleGemini,
                ["ApiKey"] = storedAiKey,
                ["SelectedModelName"] = "gemini-2.0-flash"
            }
        };
        if (storedGeminiKey != null)
            node["SpeechSettings"] = new JsonObject { ["GeminiApiKey"] = storedGeminiKey };
        File.WriteAllText(ProfileFile, node.ToJsonString());
    }

    private void WriteConfig(string aiKey, string? geminiKey = null, string? tavilyKey = null)
    {
        var node = new JsonObject
        {
            ["SelectedProvider"] = (int)AiProviderType.GoogleGemini,
            ["ApiKey"] = aiKey,
            ["SelectedModelName"] = "gemini-2.0-flash",
            ["UiSettings"] = new JsonObject { ["Theme"] = "Dark" }
        };
        if (geminiKey != null)
            node["SpeechSettings"] = new JsonObject { ["GeminiApiKey"] = geminiKey };
        if (tavilyKey != null)
            node["WebSearchSettings"] = new JsonObject { ["Provider"] = "Tavily", ["TavilyApiKey"] = tavilyKey, ["MaxResults"] = 5 };
        File.WriteAllText(ConfigFile, node.ToJsonString());
    }

    // ---------------- Format: Rückfall ----------------

    [Test]
    public void Fallback_SchreibtDasFormatVon120_DamitDieAlteVersionEsLesenKann()
    {
        var protector = SecretProtector.Fallback();

        var stored = protector.Protect(Key);

        Assert.That(stored, Does.Not.StartWith(SecretProtector.FormatPrefix));
        Assert.That(stored, Does.Not.Contain(Key));
        Assert.That(new AesEncryptionService().Decrypt(stored), Is.EqualTo(Key), "1.2.0 könnte den Wert nicht lesen");
        Assert.That(protector.Unprotect(stored), Is.EqualTo(new SecretReadResult(SecretReadStatus.Ok, Key)));
    }

    [Test]
    public void AlleAltenFormate_BleibenLesbar_UndWerdenZumNeuSchuetzenMarkiert()
    {
        var aes = new AesEncryptionService();
        var protector = Platform();

        Assert.Multiple(() =>
        {
            Assert.That(protector.Unprotect(Key), Is.EqualTo(new SecretReadResult(SecretReadStatus.NeedsUpgrade, Key)), "Klartext");
            Assert.That(protector.Unprotect(aes.Encrypt(aes.Encrypt(Key))), Is.EqualTo(new SecretReadResult(SecretReadStatus.NeedsUpgrade, Key)), "doppelt verschlüsselt");
            Assert.That(protector.Unprotect(""), Is.EqualTo(SecretReadResult.Empty));
            Assert.That(protector.Unprotect(null), Is.EqualTo(SecretReadResult.Empty));
        });

        var once = protector.Unprotect(aes.Encrypt(Key));
        Assert.That(once.Value, Is.EqualTo(Key));
        Assert.That(once.Status, Is.EqualTo(protector.Method == SecretProtectionMethod.Fallback ? SecretReadStatus.Ok : SecretReadStatus.NeedsUpgrade));
    }

    [Test]
    public void KurzerKlartextImBase64Format_WirdNichtMehrZuLeer()
    {
        // Früher machte Decrypt aus kurzem gültigem Base64 (z. B. „abcd“) einen leeren Schlüssel
        Assert.That(SecretProtector.Fallback().Unprotect("abcd").Value, Is.EqualTo("abcd"));
    }

    [TestCase("ss2:kr:abc:AAAA")]
    [TestCase("ss2:zukunft:AAAA")]
    [TestCase(ForeignValue)]
    public void UnbekannteOderFremdeWerte_SindNichtLesbar_UndKommenNieAlsKlartextZurueck(string stored)
    {
        foreach (var protector in new[] { SecretProtector.Fallback(), Platform() })
            Assert.That(protector.Unprotect(stored), Is.EqualTo(SecretReadResult.Unreadable));
    }

    // ---------------- Format: Windows DPAPI ----------------

    [Test, Platform("Win")]
    public void Dpapi_SchuetztMitPraefix_UndLiestZurueck()
    {
        var protector = SecretProtector.ForCurrentPlatform();
        Assert.That(protector.Method, Is.EqualTo(SecretProtectionMethod.WindowsAccount));

        var stored = protector.Protect(Key);

        Assert.That(stored, Does.StartWith(SecretProtector.DpapiPrefix));
        Assert.That(stored, Does.Not.Contain(Key));
        Assert.That(protector.Unprotect(stored), Is.EqualTo(new SecretReadResult(SecretReadStatus.Ok, Key)));
    }

    [Test, Platform("Win")]
    public void Dpapi_WertEinesAnderenKontos_IstNichtLesbar()
    {
        // Nachgestellt über andere Entropie: DPAPI lehnt ab wie bei einem fremden Konto bzw. Computer
#pragma warning disable CA1416
        var foreign = ProtectedData.Protect(Encoding.UTF8.GetBytes(Key), Encoding.UTF8.GetBytes("andere-App"), DataProtectionScope.CurrentUser);
#pragma warning restore CA1416
        var stored = SecretProtector.DpapiPrefix + Convert.ToBase64String(foreign);

        Assert.That(SecretProtector.ForCurrentPlatform().Unprotect(stored), Is.EqualTo(SecretReadResult.Unreadable));
    }

    [Test, Platform("Win")]
    public void RueckwegAuf120_AlteVersionVerschluesseltNeuenWertNochmal_NeueVersionLiestIhnWieder()
    {
        var protector = SecretProtector.ForCurrentPlatform();
        var stored = protector.Protect(Key);

        // 1.2.0 kann „ss2:…“ nicht entschlüsseln, hält es für Klartext (kein Absturz) und verschlüsselt es beim Speichern erneut
        var aes = new AesEncryptionService();
        Assert.That(aes.Decrypt(stored), Is.EqualTo(stored));
        var after120 = aes.Encrypt(aes.Decrypt(stored));

        Assert.That(protector.Unprotect(after120), Is.EqualTo(new SecretReadResult(SecretReadStatus.NeedsUpgrade, Key)));
    }

    // ---------------- Speicherung: nicht lesbare Werte ----------------

    [Test]
    public void OhneAngabe_NutztDerSpeicherMitEigenemOrdnerDenRueckfall()
    {
        Assert.That(new JsonStorageService(_dir).KeyProtection.Method, Is.EqualTo(SecretProtectionMethod.Fallback));
    }

    [Test]
    public async Task NichtLesbarerProfilSchluessel_IstLeerMitKennzeichen()
    {
        WriteProfile(ForeignValue);
        var storage = new JsonStorageService(_dir, Platform());

        var config = await storage.LoadAiConfigForProfileAsync(Profile);

        Assert.That(config!.ApiKey, Is.Empty);
        Assert.That(config.ApiKeyUnreadable, Is.True);
        Assert.That(config.ApiKeyNeedsReentry, Is.True);
    }

    [Test]
    public async Task NichtLesbarerSchluessel_BleibtBeimSpeichernAndererFelderByteGleich()
    {
        WriteProfile(ForeignValue, ForeignValue);
        WriteConfig(ForeignValue, ForeignValue, ForeignValue);
        var storage = new JsonStorageService(_dir, Platform());

        await storage.SaveDocumentsAsync(Profile, new[] { new DocumentItem { FileName = "Skript.pdf" } });
        var config = (await storage.LoadAiConfigForProfileAsync(Profile))!;
        config.SelectedProvider = AiProviderType.OllamaLocal; // anderer Anbieter, kein neuer Schlüssel
        await storage.SaveAiConfigForProfileAsync(Profile, config);
        await storage.SaveAiConfigAsync(config);
        var speech = await storage.LoadSpeechSettingsAsync(Profile);
        speech.Rate = 1.5;
        await storage.SaveSpeechSettingsAsync(Profile, speech);
        var web = await storage.LoadWebSearchSettingsAsync();
        web.MaxResults = 3;
        await storage.SaveWebSearchSettingsAsync(web);

        Assert.Multiple(() =>
        {
            Assert.That(Stored(ProfileFile, "AiSettings", "ApiKey"), Is.EqualTo(ForeignValue));
            Assert.That(Stored(ProfileFile, "SpeechSettings", "GeminiApiKey"), Is.EqualTo(ForeignValue));
            Assert.That(Stored(ConfigFile, "ApiKey"), Is.EqualTo(ForeignValue));
            Assert.That(Stored(ConfigFile, "SpeechSettings", "GeminiApiKey"), Is.EqualTo(ForeignValue));
            Assert.That(Stored(ConfigFile, "WebSearchSettings", "TavilyApiKey"), Is.EqualTo(ForeignValue));
        });
    }

    [Test]
    public async Task NeuEingetragenerSchluessel_ErsetztDenNichtLesbaren()
    {
        WriteProfile(ForeignValue);
        var storage = new JsonStorageService(_dir, Platform());

        var config = (await storage.LoadAiConfigForProfileAsync(Profile))!;
        config.ApiKey = Key;
        await storage.SaveAiConfigForProfileAsync(Profile, config);

        var loaded = (await storage.LoadAiConfigForProfileAsync(Profile))!;
        Assert.That(loaded.ApiKey, Is.EqualTo(Key));
        Assert.That(loaded.ApiKeyUnreadable, Is.False);
        Assert.That(File.ReadAllText(ProfileFile), Does.Not.Contain(Key));
    }

    [Test]
    public async Task LeererSchluessel_LoeschtEinenLesbarenWieBisher()
    {
        var storage = new JsonStorageService(_dir, Platform());
        await storage.SaveAiConfigForProfileAsync(Profile, new AiConfig { SelectedProvider = AiProviderType.GoogleGemini, ApiKey = Key, SelectedModelName = "gemini-2.0-flash" });

        await storage.SaveAiConfigForProfileAsync(Profile, new AiConfig { SelectedProvider = AiProviderType.GoogleGemini, ApiKey = "", SelectedModelName = "gemini-2.0-flash" });

        Assert.That(Stored(ProfileFile, "AiSettings", "ApiKey"), Is.Empty);
    }

    [Test]
    public async Task SchluesselEntfernen_LeertNurNichtLesbareWerte()
    {
        var storage = new JsonStorageService(_dir, Platform());
        await storage.SaveAiConfigForProfileAsync(Profile, new AiConfig { SelectedProvider = AiProviderType.GoogleGemini, ApiKey = Key, SelectedModelName = "gemini-2.0-flash" });
        var readable = Stored(ProfileFile, "AiSettings", "ApiKey");
        WriteConfig(ForeignValue, tavilyKey: ForeignValue);

        await storage.DiscardUnreadableKeyAsync(StoredKeyKind.AiApiKey, Profile);

        Assert.That(Stored(ProfileFile, "AiSettings", "ApiKey"), Is.EqualTo(readable), "lesbarer Profilschlüssel wurde verändert");
        Assert.That(Stored(ConfigFile, "ApiKey"), Is.Empty);
        Assert.That(Stored(ConfigFile, "WebSearchSettings", "TavilyApiKey"), Is.EqualTo(ForeignValue), "anderer Schlüssel wurde mit entfernt");
        Assert.That(Stored(ConfigFile, "UiSettings", "Theme"), Is.EqualTo("Dark"), "übrige Einstellungen gingen verloren");

        await storage.DiscardUnreadableKeyAsync(StoredKeyKind.TavilyKey, Profile);
        Assert.That(Stored(ConfigFile, "WebSearchSettings", "TavilyApiKey"), Is.Empty);
    }

    [Test]
    public async Task NichtLesbareSchluessel_SetzenDieKennzeichenInAllenEinstellungen()
    {
        WriteProfile(Key, ForeignValue);
        WriteConfig(ForeignValue, ForeignValue, ForeignValue);
        var storage = new JsonStorageService(_dir, Platform());

        Assert.That((await storage.LoadSpeechSettingsAsync(Profile)).GeminiApiKeyNeedsReentry, Is.True);
        Assert.That((await storage.LoadAiConfigAsync()).ApiKeyNeedsReentry, Is.True);
        Assert.That((await storage.LoadWebSearchSettingsAsync()).TavilyApiKeyNeedsReentry, Is.True);
        Assert.That((await storage.LoadAiConfigForProfileAsync(Profile))!.ApiKeyUnreadable, Is.False);
    }

    [Test]
    public void Kennzeichen_WerdenNichtGespeichert()
    {
        var json = JsonSerializer.Serialize(new AiConfig { ApiKeyUnreadable = true })
                   + JsonSerializer.Serialize(new SpeechSettings { GeminiApiKeyUnreadable = true })
                   + JsonSerializer.Serialize(new WebSearchSettings { TavilyApiKeyUnreadable = true });

        Assert.That(json, Does.Not.Contain("Unreadable").And.Not.Contain("Reentry"));
    }

    // ---------------- Migration beim Start ----------------

    [Test]
    public void Migration_SchuetztAlteWerteNeu_MitSicherung()
    {
        var aes = new AesEncryptionService();
        WriteProfile(aes.Encrypt(aes.Encrypt(Key)), Key);
        WriteConfig(Key, aes.Encrypt(Key), Key);
        var originalProfile = File.ReadAllText(ProfileFile);
        var protector = Platform();

        var result = SecretMigration.Run(_dir, protector);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.BackupPath, Does.Exist);
        Assert.That(Path.GetFileName(result.BackupPath), Does.StartWith("keys-1.2-"));
        Assert.That(File.ReadAllText(ProfileFile), Does.Not.Contain(Key));
        Assert.That(File.ReadAllText(ConfigFile), Does.Not.Contain(Key));
        foreach (var value in new[]
                 {
                     Stored(ProfileFile, "AiSettings", "ApiKey"), Stored(ProfileFile, "SpeechSettings", "GeminiApiKey"),
                     Stored(ConfigFile, "ApiKey"), Stored(ConfigFile, "SpeechSettings", "GeminiApiKey"), Stored(ConfigFile, "WebSearchSettings", "TavilyApiKey")
                 })
        {
            Assert.That(protector.Unprotect(value), Is.EqualTo(new SecretReadResult(SecretReadStatus.Ok, Key)));
        }
        Assert.That(Stored(ConfigFile, "UiSettings", "Theme"), Is.EqualTo("Dark"), "übrige Einstellungen gingen verloren");

        using var zip = ZipFile.OpenRead(result.BackupPath!);
        Assert.That(zip.Entries.Select(e => e.FullName), Is.EquivalentTo(new[] { "config.json", "Profiles/" + Profile + ".json" }));
        using var reader = new StreamReader(zip.GetEntry("Profiles/" + Profile + ".json")!.Open());
        Assert.That(reader.ReadToEnd(), Is.EqualTo(originalProfile), "Sicherung enthält nicht den alten Stand");
    }

    [Test]
    public void Migration_OhneAlteWerte_AendertNichtsUndLegtKeineSicherungAn()
    {
        var protector = Platform();
        WriteProfile(protector.Protect(Key), ForeignValue);
        var before = File.ReadAllText(ProfileFile);

        var result = SecretMigration.Run(_dir, protector);

        Assert.That(result.MigratedKeys, Is.Zero);
        Assert.That(result.BackupPath, Is.Null);
        Assert.That(Directory.Exists(Path.Combine(_dir, SecretMigration.BackupFolderName)), Is.False);
        Assert.That(File.ReadAllText(ProfileFile), Is.EqualTo(before), "nicht lesbarer Wert wurde angefasst");
    }

    [Test]
    public void Migration_ZweiterLauf_HatNichtsMehrZuTun()
    {
        WriteConfig(Key);
        SecretMigration.Run(_dir, Platform());

        var second = SecretMigration.Run(_dir, Platform());

        Assert.That(second.MigratedKeys, Is.Zero);
        Assert.That(Directory.GetFiles(Path.Combine(_dir, SecretMigration.BackupFolderName)), Has.Length.EqualTo(1));
    }

    [Test]
    public void Migration_DefekteDatei_BrichtNichtAb()
    {
        WriteConfig(Key);
        Directory.CreateDirectory(Path.GetDirectoryName(ProfileFile)!);
        File.WriteAllText(ProfileFile, "{ kaputt");

        var result = SecretMigration.Run(_dir, Platform());

        Assert.That(result.MigratedKeys, Is.EqualTo(1));
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(File.ReadAllText(ProfileFile), Is.EqualTo("{ kaputt"));
    }

    [Test]
    public void Sicherungen_WerdenNach30TagenGeloescht()
    {
        var folder = Path.Combine(_dir, SecretMigration.BackupFolderName);
        Directory.CreateDirectory(folder);
        var now = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 27)));
        string Backup(int daysAgo) => Path.Combine(folder,
            "keys-1.2-" + now.AddDays(-daysAgo).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip");
        var old = Backup(31);
        var exactly30 = Backup(30);
        var recent = Backup(29);
        var other = Path.Combine(folder, "anderes.zip");
        foreach (var file in new[] { old, exactly30, recent, other })
            File.WriteAllText(file, "x");

        var deleted = SecretMigration.DeleteExpiredBackups(_dir, now);

        Assert.That(deleted, Is.EquivalentTo(new[] { old, exactly30 }));
        Assert.That(recent, Does.Exist);
        Assert.That(other, Does.Exist, "fremde Dateien im Ordner werden nicht gelöscht");
    }

    // ---------------- Meldungen der Dienste ----------------

    [Test]
    public void Dispatcher_MeldetNichtLesbarenSchluessel_StattOhneSchluesselAnzufragen()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync(Profile);
        storage.Setup(s => s.LoadAiConfigForProfileAsync(Profile)).ReturnsAsync(new AiConfig
        {
            SelectedProvider = AiProviderType.OpenAiGpt4, SelectedModelName = "gpt-4o-mini", ApiKeyUnreadable = true
        });
        var http = new HttpClient(new FailingHandler());
        var dispatcher = new LlmServiceDispatcher(storage.Object, http);

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => dispatcher.AskQuestionAsync("Frage", Array.Empty<string>()));

        Assert.That(ex!.Message, Does.Contain("OpenAI").And.Contain("neu ein"));
    }

    [Test]
    public void Dispatcher_LokalerDienst_IgnoriertNichtLesbarenSchluessel()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync(Profile);
        storage.Setup(s => s.LoadAiConfigForProfileAsync(Profile)).ReturnsAsync(new AiConfig
        {
            SelectedProvider = AiProviderType.FakeAi, ApiKeyUnreadable = true
        });

        Assert.DoesNotThrowAsync(() => new LlmServiceDispatcher(storage.Object, new HttpClient(new FailingHandler()))
            .AskQuestionAsync("Frage", Array.Empty<string>()));
    }

    [Test]
    public void Websuche_MeldetNichtLesbarenTavilySchluessel()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.LoadWebSearchSettingsAsync()).ReturnsAsync(new WebSearchSettings
        {
            Provider = WebSearchProviderType.Tavily, TavilyApiKeyUnreadable = true
        });
        var dispatcher = new WebSearchDispatcher(storage.Object, new HttpClient(new FailingHandler()), _ => null);

        var ex = Assert.ThrowsAsync<WebSearchException>(() =>
            dispatcher.SearchAsync(WebSearchQuery.FromUserQuestion("Frage"), CultureInfo.GetCultureInfo("de")));

        Assert.That(ex!.Kind, Is.EqualTo(WebSearchErrorKind.NotConfigured));
        Assert.That(ex.SettingsCanHelp, Is.True);
        Assert.That(ex.Message, Does.Contain("Tavily").And.Contain("neu ein"));
    }

    [Test]
    public async Task GeminiTts_MeldetNichtLesbarenSchluessel()
    {
        var context = new SpeechContext(new SpeechSettings { GeminiApiKeyUnreadable = true }, null);

        var availability = await new GeminiTextToSpeechService().GetAvailabilityAsync(context);

        Assert.That(availability.Status, Is.EqualTo(SpeechAvailabilityStatus.Unavailable));
        Assert.That(availability.Message, Does.Contain("Gemini TTS").And.Contain("neu ein"));
        Assert.That(GeminiTextToSpeechService.UnreadableKeyMessage(new SpeechContext(new SpeechSettings(), null)), Is.Null);
    }

    // ---------------- Einstellungen ----------------

    [Test]
    public async Task Einstellungen_ZeigenNeuEintragen_BisEinSchluesselEingetragenIst()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync(Profile);
        storage.Setup(s => s.LoadOutputLanguageAsync(Profile)).ReturnsAsync("ui");
        storage.Setup(s => s.LoadAiConfigForProfileAsync(Profile)).ReturnsAsync(new AiConfig
        {
            SelectedProvider = AiProviderType.OpenAiGpt4, SelectedModelName = "gpt-4o-mini", ApiKeyUnreadable = true
        });
        var vm = new SettingsViewModel(storage.Object, Mock.Of<ILlmService>(), Mock.Of<ILocalLlmDiscoveryService>());

        await vm.InitializeAsync();
        Assert.That(vm.ApiKeyNeedsReentry, Is.True);

        vm.ApiKey = Key;
        Assert.That(vm.ApiKeyNeedsReentry, Is.False);

        vm.ApiKey = "";
        await vm.DiscardUnreadableApiKeyAsync();
        Assert.That(vm.ApiKeyNeedsReentry, Is.False);
        storage.Verify(s => s.DiscardUnreadableKeyAsync(StoredKeyKind.AiApiKey, Profile), Times.Once);
    }

    [Test]
    public async Task NichtLesbarerSchluessel_ZeigtNurDenHinweisNeuEintragen_KeinenZweitenHinweis()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync(Profile);
        storage.Setup(s => s.LoadOutputLanguageAsync(Profile)).ReturnsAsync("ui");
        storage.Setup(s => s.LoadSpeechSettingsAsync(Profile)).ReturnsAsync(new SpeechSettings { GeminiApiKeyUnreadable = true });
        storage.Setup(s => s.LoadAiConfigForProfileAsync(Profile)).ReturnsAsync(new AiConfig
        {
            SelectedProvider = AiProviderType.OpenAiGpt4, SelectedModelName = "gpt-4o-mini", ApiKeyUnreadable = true
        });
        storage.Setup(s => s.LoadWebSearchSettingsAsync()).ReturnsAsync(new WebSearchSettings
        {
            Provider = WebSearchProviderType.Tavily, TavilyApiKeyUnreadable = true
        });

        // Sprachausgabe: „Noch kein Schlüssel – Gemini TTS ist ausgegraut …“ entfällt, nur der Hinweis „neu eintragen“
        var speech = new SpeechSettingsViewModel(storage.Object, Array.Empty<ITextToSpeechService>());
        await speech.InitializeAsync();
        Assert.That(speech.GeminiKeyNeedsReentry, Is.True);
        Assert.That(speech.ShowsKeySourceText, Is.False);
        speech.GeminiApiKey = Key;
        Assert.That(speech.ShowsKeySourceText, Is.True, "nach dem Eintragen gilt wieder die normale Zeile");

        // KI-Modell: keine Statusmeldung zusätzlich zum Hinweis
        var ai = new SettingsViewModel(storage.Object, Mock.Of<ILlmService>(), Mock.Of<ILocalLlmDiscoveryService>());
        await ai.InitializeAsync();
        Assert.That(ai.ApiKeyNeedsReentry, Is.True);
        Assert.That(ai.ConnectionStatusMessage, Is.Empty);

        // Websuche: weder Statusmeldung noch Formatwarnung zusätzlich zum Hinweis
        var web = new WebSearchSettingsViewModel(storage.Object, Mock.Of<IWebSearchService>());
        await web.LoadAsync();
        web.CheckKeyFormat();
        Assert.That(web.TavilyKeyNeedsReentry, Is.True);
        Assert.That(web.StatusMessage, Is.Null);
        Assert.That(web.KeyFormatWarning, Is.Null);
    }

    [Test]
    public async Task Sprachausgabe_SchluesselAusDemChat_ZeigtDieZeileTrotzNichtLesbaremEigenemSchluessel()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync(Profile);
        storage.Setup(s => s.LoadSpeechSettingsAsync(Profile)).ReturnsAsync(new SpeechSettings { GeminiApiKeyUnreadable = true });
        storage.Setup(s => s.LoadAiConfigForProfileAsync(Profile)).ReturnsAsync(new AiConfig
        {
            SelectedProvider = AiProviderType.GoogleGemini, ApiKey = Key, SelectedModelName = "gemini-2.0-flash"
        });

        var speech = new SpeechSettingsViewModel(storage.Object, Array.Empty<ITextToSpeechService>());
        await speech.InitializeAsync();

        Assert.That(speech.ShowsKeySourceText, Is.True);
    }

    [Test]
    public void SchutzHinweis_VersprichtNichtMehrAlsDieBindungAnsKonto()
    {
        var windows = KeyProtectionTexts.Line(Mock.Of<IKeyProtectionInfo>(i => i.Method == SecretProtectionMethod.WindowsAccount));
        var fallback = KeyProtectionTexts.Line(Mock.Of<IKeyProtectionInfo>(i => i.Method == SecretProtectionMethod.Fallback));

        Assert.That(windows, Does.Contain("Windows-Konto"));
        Assert.That(windows, Does.Not.Contain("sicher").IgnoreCase.And.Not.Contain("Schadsoftware"));
        Assert.That(fallback, Does.Contain("verschleiert"));
        Assert.That(KeyProtectionTexts.IsFallback(Mock.Of<IKeyProtectionInfo>(i => i.Method == SecretProtectionMethod.Fallback)), Is.True);
        Assert.That(KeyProtectionTexts.Line(null), Is.Null);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new AssertionException("Es darf keine Anfrage ins Netz gehen: " + request.RequestUri);
    }
}
