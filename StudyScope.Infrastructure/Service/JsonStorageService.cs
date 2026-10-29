using System.Text.Json;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

public class JsonStorageService : IStorageService
{
    private readonly string _baseDirectory;
    private readonly IEncryptionService _encryptionService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Standard-Konstruktor für die laufende Anwendung (nutzt AppData und Standard-AesEncryptionService)
    public JsonStorageService(): this(null, null)
    {
    }

    // Konstruktor für Unit-Tests & Dependency Injection
    // Unterstützt sowohl new JsonStorageService(tempDir) als auch die Übergabe eines IEncryptionService
    public JsonStorageService(string? baseDirectory, IEncryptionService? encryptionService = null)
    {
        _encryptionService = encryptionService ?? new AesEncryptionService();

        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudyScope")
            : baseDirectory;

        Directory.CreateDirectory(_baseDirectory);
        Directory.CreateDirectory(GetProfilesDirectory());
    }

    // Optionaler Bequemlichkeits-Überladungs-Konstruktor, falls Tests nur den Service übergeben wollen
    public JsonStorageService(IEncryptionService encryptionService)
        : this(null, encryptionService)
    {
    }

    private string GetProfilesDirectory()
    {
        var dir = Path.Combine(_baseDirectory, "Profiles");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    private string GetProfileFilePath(string profileName)
    {
        var safeName = string.IsNullOrWhiteSpace(profileName) ? "Standard" : profileName.Trim();
        return Path.Combine(GetProfilesDirectory(), $"{safeName}.json");
    }

    private string GetConfigFilePath()
    {
        return Path.Combine(_baseDirectory, "config.json");
    }

    private string GetLastProfileFilePath()
    {
        return Path.Combine(_baseDirectory, "last_profile.txt");
    }

    // Hilfsmethode: Liest das komplette UserProfile-Objekt aus der Profil-JSON
    private async Task<UserProfile> ReadProfileFileAsync(string profileName)
    {
        var filePath = GetProfileFilePath(profileName);
        if (!File.Exists(filePath))
        {
            return new UserProfile { Name = profileName };
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new UserProfile { Name = profileName };
            }

            // Falls die Datei als reines Array vorliegt, in ein UserProfile überführen:
            if (json.TrimStart().StartsWith("["))
            {
                var oldDocs = JsonSerializer.Deserialize<List<DocumentItem>>(json, JsonOptions);
                return new UserProfile
                {
                    Name = profileName,
                    Documents = oldDocs ?? new List<DocumentItem>()
                };
            }

            var profile = JsonSerializer.Deserialize<UserProfile>(json, JsonOptions);
            if (profile != null)
            {
                profile.Name = profileName;
                profile.Documents ??= new List<DocumentItem>();
                return profile;
            }

            // Direkt nach dem Deserialisieren des UserProfile-Objekts:
            if (profile?.AiSettings != null && !string.IsNullOrWhiteSpace(profile.AiSettings.ApiKey))
            {
                // ApiKey für die UI/ViewModels wieder transparent entschlüsseln
                profile.AiSettings.ApiKey = _encryptionService.Decrypt(profile.AiSettings.ApiKey);
            }
        }
        catch
        {
            // Fallback bei Lesefehlern
        }

        return new UserProfile { Name = profileName };
    }
   

    public async Task<List<DocumentItem>> LoadDocumentsAsync(string profileName)
    {
        var profile = await ReadProfileFileAsync(profileName);

        // Physische Existenz für alle Dateitypen (.pdf, .txt, .csv) prüfen
        foreach (var doc in profile.Documents)
        {
            doc.IsMissing = string.IsNullOrWhiteSpace(doc.FilePath) || !File.Exists(doc.FilePath);
        }

        return profile.Documents;
    }

    public async Task SaveDocumentsAsync(string profileName, IEnumerable<DocumentItem> documents)
    {
        // 1. Das bestehende Profil einlesen (damit Name, CreatedAt und AiSettings erhalten bleiben!)
        var profile = await ReadProfileFileAsync(profileName);

        // 2. Nur die Dokumente aktualisieren
        profile.Documents = documents?.ToList() ?? new List<DocumentItem>();

        // 3. Das VOLLSTÄNDIGE Profil-Objekt speichern (NICHT nur die Dokumentenliste!)
        var filePath = GetProfileFilePath(profileName);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }


    public async Task<AiConfig?> LoadAiConfigForProfileAsync(string profileName)
    {
        var profile = await ReadProfileFileAsync(profileName);

        // Wenn keine Einstellungen da sind oder kein Modell/Provider gewählt wurde -> null zurückgeben
        if (profile.AiSettings == null ||
            (string.IsNullOrWhiteSpace(profile.AiSettings.SelectedModelName) && string.IsNullOrWhiteSpace(profile.AiSettings.ModelName)))
        {
            return null;
        }

        return profile.AiSettings;
    }

    public async Task SaveAiConfigForProfileAsync(string profileName, AiConfig config)
    {
        var profile = await ReadProfileFileAsync(profileName);

        // Kopie erstellen, damit das Original im UI-Speicher unberührt bleibt
        var encryptedConfig = new AiConfig
        {
            SelectedProvider = config.SelectedProvider,
            ApiKey = _encryptionService.Encrypt(config.ApiKey), // Hier verschlüsseln!
            ModelName = config.ModelName,
            LocalEndpointUrl = config.LocalEndpointUrl,
            SelectedModelName = config.SelectedModelName
        };

        profile.AiSettings = encryptedConfig;

        var filePath = GetProfileFilePath(profileName);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }


    public async Task<string> GetLastActiveProfileNameAsync()
    {
        var filePath = GetLastProfileFilePath();
        if (!File.Exists(filePath))
            return "Standard";

        var name = (await File.ReadAllTextAsync(filePath)).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Standard" : name;
    }

    public async Task SetLastActiveProfileNameAsync(string profileName)
    {
        var filePath = GetLastProfileFilePath();
        await File.WriteAllTextAsync(filePath, profileName ?? "Standard");
    }

    public Task<List<string>> GetAllProfileNamesAsync()
    {
        return Task.Run(() =>
        {
            var baseDir = GetProfilesDirectory();
            var files = Directory.GetFiles(baseDir, "*.json");
            var profileNames = files
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name)
                .ToList();

            if (!profileNames.Any())
            {
                profileNames.Add("Standard");
            }

            return profileNames;
        });
    }

    public Task DeleteProfileAsync(string profileName)
    {
        return Task.Run(() =>
        {
            if (string.Equals(profileName, "Standard", StringComparison.OrdinalIgnoreCase))
                return;

            var filePath = GetProfileFilePath(profileName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        });
    }

    public Task<List<UserProfile>> LoadProfilesAsync()
    {
        return Task.Run(async () =>
        {
            var names = await GetAllProfileNamesAsync();
            return names.Select(n => new UserProfile { Name = n }).ToList();
        });
    }

    public async Task SaveProfilesAsync(IEnumerable<UserProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            var filePath = GetProfileFilePath(profile.Name);
            if (!File.Exists(filePath))
            {
                var newProfile = new UserProfile { Name = profile.Name };
                var json = JsonSerializer.Serialize(newProfile, JsonOptions);
                await File.WriteAllTextAsync(filePath, json);
            }
        }
    }

    public async Task<AiConfig> LoadAiConfigAsync()
    {
        var filePath = GetConfigFilePath();
        if (!File.Exists(filePath))
            return new AiConfig();

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<AiConfig>(json, JsonOptions) ?? new AiConfig();
        }
        catch
        {
            return new AiConfig();
        }
    }

    public async Task SaveAiConfigAsync(AiConfig config)
    {
        var filePath = GetConfigFilePath();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }
}