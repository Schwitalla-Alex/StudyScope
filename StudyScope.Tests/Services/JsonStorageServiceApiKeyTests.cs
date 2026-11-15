using System.Text.Json;
using NUnit.Framework;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Verschlüsselung der API-Keys in Profilen und config.json: im Speicher Klartext, auf der Platte nie.
/// </summary>
[TestFixture]
public class JsonStorageServiceApiKeyTests
{
    private const string Profile = "Testprofil";
    private const string Key = "DUMMY_TEST_KEY_PROFIL_0001";

    private string _tempDirectory = null!;
    private JsonStorageService _storage = null!;
    private readonly AesEncryptionService _aes = new();

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "StudyScope_ApiKeyTests_" + Guid.NewGuid());
        _storage = new JsonStorageService(_tempDirectory, _aes);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }

    private string ProfileFile => Path.Combine(_tempDirectory, "Profiles", Profile + ".json");
    private string ConfigFile => Path.Combine(_tempDirectory, "config.json");

    private static AiConfig GeminiConfig(string key) => new()
    {
        SelectedProvider = AiProviderType.GoogleGemini,
        ApiKey = key,
        SelectedModelName = "gemini-2.0-flash"
    };

    private static string StoredApiKey(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var root = doc.RootElement;
        var settings = root.TryGetProperty("AiSettings", out var s) ? s : root;
        return settings.GetProperty("ApiKey").GetString() ?? string.Empty;
    }

    private async Task WriteProfileWithStoredKeyAsync(string storedKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProfileFile)!);
        var profile = new UserProfile { Name = Profile, AiSettings = GeminiConfig(storedKey) };
        await File.WriteAllTextAsync(ProfileFile, JsonSerializer.Serialize(profile));
    }

    [Test]
    public async Task ProfilKey_WirdVerschluesseltGespeichertUndBeimLesenEntschluesselt()
    {
        await _storage.SaveAiConfigForProfileAsync(Profile, GeminiConfig(Key));

        Assert.That(File.ReadAllText(ProfileFile), Does.Not.Contain(Key), "Key steht im Klartext in der Profildatei");
        Assert.That(_aes.Decrypt(StoredApiKey(ProfileFile)), Is.EqualTo(Key), "Key ist nicht einfach verschlüsselt");

        var loaded = await _storage.LoadAiConfigForProfileAsync(Profile);
        Assert.That(loaded?.ApiKey, Is.EqualTo(Key));
    }

    [Test]
    public async Task SaveAiConfigForProfile_VeraendertDieUebergebeneKonfigurationNicht()
    {
        var config = GeminiConfig(Key);

        await _storage.SaveAiConfigForProfileAsync(Profile, config);

        Assert.That(config.ApiKey, Is.EqualTo(Key));
    }

    [Test]
    public async Task SaveDocuments_SchreibtDenKeyNichtImKlartext()
    {
        await _storage.SaveAiConfigForProfileAsync(Profile, GeminiConfig(Key));

        await _storage.SaveDocumentsAsync(Profile, new[] { new DocumentItem { FileName = "Skript.pdf" } });

        Assert.That(File.ReadAllText(ProfileFile), Does.Not.Contain(Key), "SaveDocumentsAsync hat den Key im Klartext geschrieben");
        Assert.That(_aes.Decrypt(StoredApiKey(ProfileFile)), Is.EqualTo(Key));
        Assert.That((await _storage.LoadAiConfigForProfileAsync(Profile))?.ApiKey, Is.EqualTo(Key));
        Assert.That((await _storage.LoadDocumentsAsync(Profile)).Select(d => d.FileName), Is.EqualTo(new[] { "Skript.pdf" }));
    }

    [Test]
    public async Task DoppeltVerschluesselterProfilKey_WirdBeimLesenRepariert()
    {
        // So hat ihn die alte Version nach erneutem Speichern der Einstellungen hinterlassen
        await WriteProfileWithStoredKeyAsync(_aes.Encrypt(_aes.Encrypt(Key)));

        var loaded = await _storage.LoadAiConfigForProfileAsync(Profile);
        Assert.That(loaded?.ApiKey, Is.EqualTo(Key));

        // Nach dem nächsten Speichern ist er wieder genau einmal verschlüsselt
        await _storage.SaveDocumentsAsync(Profile, Array.Empty<DocumentItem>());
        Assert.That(_aes.Decrypt(StoredApiKey(ProfileFile)), Is.EqualTo(Key));
    }

    [Test]
    public async Task AlterKlartextKeyImProfil_BleibtLesbar()
    {
        await WriteProfileWithStoredKeyAsync(Key);

        var loaded = await _storage.LoadAiConfigForProfileAsync(Profile);

        Assert.That(loaded?.ApiKey, Is.EqualTo(Key));
    }

    [Test]
    public async Task GlobaleKonfiguration_WirdVerschluesseltGespeichertUndEntschluesseltGelesen()
    {
        var config = GeminiConfig(Key);

        await _storage.SaveAiConfigAsync(config);

        Assert.That(File.ReadAllText(ConfigFile), Does.Not.Contain(Key), "Key steht im Klartext in config.json");
        Assert.That(_aes.Decrypt(StoredApiKey(ConfigFile)), Is.EqualTo(Key));
        Assert.That(config.ApiKey, Is.EqualTo(Key), "übergebene Konfiguration wurde verändert");
        Assert.That((await _storage.LoadAiConfigAsync()).ApiKey, Is.EqualTo(Key));
    }

    [Test]
    public async Task AlterKlartextKeyInConfigJson_BleibtLesbarUndWirdBeimSpeichernVerschluesselt()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(ConfigFile, JsonSerializer.Serialize(GeminiConfig(Key)));

        var loaded = await _storage.LoadAiConfigAsync();
        Assert.That(loaded.ApiKey, Is.EqualTo(Key));

        await _storage.SaveAiConfigAsync(loaded);
        Assert.That(File.ReadAllText(ConfigFile), Does.Not.Contain(Key));
        Assert.That((await _storage.LoadAiConfigAsync()).ApiKey, Is.EqualTo(Key));
    }
}
