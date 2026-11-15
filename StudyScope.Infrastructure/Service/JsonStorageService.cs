using System.Text.Json;
using StudyScope.Core.Common;
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
            ? AppPaths.UserDataDirectory
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
        var profilesDir = Path.GetFullPath(GetProfilesDirectory());
        var fullPath = Path.GetFullPath(Path.Combine(profilesDir, $"{ToSafeProfileFileName(profileName)}.json"));

        // Letzte Absicherung: Die Datei muss direkt im Profiles-Ordner liegen (kein "..", kein absoluter Pfad)
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.Equals(parent, Path.TrimEndingDirectorySeparator(profilesDir), comparison))
            throw new ArgumentException($"Ungültiger Profilname: '{profileName}'", nameof(profileName));

        return fullPath;
    }

    /// <summary>
    /// Macht aus einem Profilnamen einen sicheren Dateinamen (ohne Endung): Pfadtrenner und auf dem
    /// aktuellen System unzulässige Zeichen werden zu '_', reservierte Windows-Namen erhalten unter Windows ein '_' davor.
    /// Bereits gültige Namen bleiben unverändert, damit bestehende Profildateien weiter gefunden werden.
    /// </summary>
    public static string ToSafeProfileFileName(string? profileName)
    {
        var name = string.IsNullOrWhiteSpace(profileName) ? "Standard" : profileName.Trim();

        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '/', '\\' };
        var safe = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());

        if (OperatingSystem.IsWindows())
        {
            // Windows entfernt Punkte/Leerzeichen am Ende stillschweigend → vorher selbst kürzen
            safe = safe.TrimEnd('.', ' ');
            if (safe.Length == 0 || ProfileNameValidator.IsReservedName(safe))
                safe = "_" + safe;
        }

        // "." und ".." allein wären Verzeichnisverweise
        if (safe is "." or "..")
            safe = safe.Replace('.', '_');

        return safe;
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

                // ApiKey für die UI/ViewModels transparent entschlüsseln (im Speicher immer Klartext)
                if (profile.AiSettings != null)
                {
                    profile.AiSettings.ApiKey = DecryptApiKey(profile.AiSettings.ApiKey);
                }

                return profile;
            }
        }
        catch
        {
            // Fallback bei Lesefehlern
        }

        return new UserProfile { Name = profileName };
    }

    /// <summary>
    /// Schreibt ein Profil auf die Platte. Der ApiKey wird dabei immer verschlüsselt;
    /// das übergebene Objekt (im Speicher mit Klartext-Key) bleibt unverändert.
    /// </summary>
    private async Task WriteProfileFileAsync(string profileName, UserProfile profile)
    {
        var toWrite = new UserProfile
        {
            Name = profile.Name,
            CreatedAt = profile.CreatedAt,
            Documents = profile.Documents ?? new List<DocumentItem>(),
            AiSettings = EncryptedCopy(profile.AiSettings ?? new AiConfig())
        };

        var filePath = GetProfileFilePath(profileName);
        var json = JsonSerializer.Serialize(toWrite, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>Kopie der Konfiguration mit verschlüsseltem ApiKey (das Original bleibt Klartext).</summary>
    private AiConfig EncryptedCopy(AiConfig config) => new()
    {
        SelectedProvider = config.SelectedProvider,
        ApiKey = _encryptionService.Encrypt(DecryptApiKey(config.ApiKey)),
        ModelName = config.ModelName,
        LocalEndpointUrl = config.LocalEndpointUrl,
        SelectedModelName = config.SelectedModelName
    };

    /// <summary>
    /// Entschlüsselt einen gespeicherten ApiKey. Wird so lange wiederholt, bis sich der Wert nicht mehr ändert:
    /// Frühere Versionen haben bereits verschlüsselte Keys beim erneuten Speichern noch einmal verschlüsselt.
    /// Alter Klartext bleibt unverändert (Decrypt liefert ihn bei ungültigem Chiffrat zurück).
    /// </summary>
    private string DecryptApiKey(string? storedKey)
    {
        var value = storedKey ?? string.Empty;
        for (var i = 0; i < 10 && !string.IsNullOrWhiteSpace(value); i++)
        {
            var decrypted = _encryptionService.Decrypt(value);
            if (decrypted is null || decrypted == value)
                break;
            value = decrypted;
        }
        return value;
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

        // 3. Das VOLLSTÄNDIGE Profil-Objekt speichern (ApiKey wird dabei wieder verschlüsselt)
        await WriteProfileFileAsync(profileName, profile);
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
        profile.AiSettings = config;

        // Verschlüsselung übernimmt WriteProfileFileAsync (auf einer Kopie, config bleibt Klartext)
        await WriteProfileFileAsync(profileName, profile);
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
                await WriteProfileFileAsync(profile.Name, new UserProfile { Name = profile.Name });
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