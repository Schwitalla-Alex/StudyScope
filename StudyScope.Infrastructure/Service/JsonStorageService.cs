using System.Text.Json;
using System.Text.Json.Nodes;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

public class JsonStorageService : IStorageService
{
    private readonly string _baseDirectory;
    private readonly ISecretProtector _protector;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Laufende Anwendung: Benutzerdaten-Ordner und Schutz des Betriebssystems (Windows DPAPI, sonst Rückfall).</summary>
    public JsonStorageService(): this(null, SecretProtector.ForCurrentPlatform())
    {
    }

    /// <summary>
    /// Eigener Ordner (Tests) bzw. Komposition in <c>Program.cs</c>. Ohne <paramref name="protector"/> gilt bewusst das
    /// Rückfall-Verfahren – Tests berühren so nie einen Schutz oder Speicher des Betriebssystems.
    /// </summary>
    public JsonStorageService(string? baseDirectory, ISecretProtector? protector = null)
    {
        _protector = protector ?? SecretProtector.Fallback();

        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppPaths.UserDataDirectory
            : baseDirectory;

        Directory.CreateDirectory(_baseDirectory);
        Directory.CreateDirectory(GetProfilesDirectory());
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

                // Schlüssel für die UI/ViewModels transparent lesen (im Speicher immer Klartext; nicht lesbar → leer + Kennzeichen)
                if (profile.AiSettings != null)
                {
                    profile.AiSettings.ApiKey = ReadSecret(profile.AiSettings.ApiKey, out var unreadable);
                    profile.AiSettings.ApiKeyUnreadable = unreadable;
                }

                if (profile.SpeechSettings != null)
                {
                    profile.SpeechSettings.GeminiApiKey = ReadSecret(profile.SpeechSettings.GeminiApiKey, out var unreadable);
                    profile.SpeechSettings.GeminiApiKeyUnreadable = unreadable;
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
    /// Schreibt ein Profil auf die Platte. Die Schlüssel werden dabei immer geschützt;
    /// das übergebene Objekt (im Speicher mit Klartext-Key) bleibt unverändert. Ein gespeicherter, hier nicht lesbarer
    /// Schlüssel bleibt erhalten, solange kein neuer eingetragen ist.
    /// </summary>
    private async Task WriteProfileFileAsync(string profileName, UserProfile profile)
    {
        var filePath = GetProfileFilePath(profileName);
        var existing = await ReadJsonObjectAsync(filePath);

        var toWrite = new UserProfile
        {
            Name = profile.Name,
            CreatedAt = profile.CreatedAt,
            Documents = profile.Documents ?? new List<DocumentItem>(),
            AiSettings = ProtectedCopy(profile.AiSettings ?? new AiConfig(), StoredSecret(existing, ProfileAiKeyPath)),
            SpeechSettings = profile.SpeechSettings == null ? null : ProtectedCopy(profile.SpeechSettings, StoredSecret(existing, ProfileGeminiKeyPath)),
            // Neue Profilfelder hier ergänzen – sonst gehen sie beim nächsten Speichern (z. B. der Dokumente) verloren
            OutputLanguage = profile.OutputLanguage,
            WebSearchEnabled = profile.WebSearchEnabled,
            WebSearchNoticeAcceptedFor = profile.WebSearchNoticeAcceptedFor
        };

        var json = JsonSerializer.Serialize(toWrite, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>Kopie der Konfiguration mit geschütztem ApiKey (das Original bleibt Klartext).</summary>
    private AiConfig ProtectedCopy(AiConfig config, string? existingStored) => new()
    {
        SelectedProvider = config.SelectedProvider,
        ApiKey = StoreSecret(config.ApiKey, existingStored),
        ModelName = config.ModelName,
        LocalEndpointUrl = config.LocalEndpointUrl,
        SelectedModelName = config.SelectedModelName
    };

    /// <summary>Kopie der Sprachausgabe mit geschütztem Gemini-Schlüssel (das Original bleibt Klartext).</summary>
    private SpeechSettings ProtectedCopy(SpeechSettings settings, string? existingStored)
    {
        var copy = settings.Clone();
        copy.GeminiApiKey = StoreSecret(settings.GeminiApiKey, existingStored);
        return copy;
    }

    // ---------------- Schutz der Schlüssel ----------------

    /// <summary>Wie Schlüssel geschützt werden (für Hinweise in den Einstellungen).</summary>
    public IKeyProtectionInfo KeyProtection => _protector;

    // Orte der Schlüssel in den Dateien (Eigenschaftsnamen ohne Groß-/Kleinschreibung, wie beim Einlesen)
    internal static readonly string[] ProfileAiKeyPath = { "AiSettings", "ApiKey" };
    internal static readonly string[] ProfileGeminiKeyPath = { SpeechSettingsProperty, "GeminiApiKey" };
    internal static readonly string[] ConfigAiKeyPath = { "ApiKey" };
    internal static readonly string[] ConfigGeminiKeyPath = { SpeechSettingsProperty, "GeminiApiKey" };
    internal static readonly string[] ConfigTavilyKeyPath = { WebSearchSettingsProperty, "TavilyApiKey" };

    /// <summary>
    /// Liest einen gespeicherten Schlüssel (alle Formate seit 1.0). Nicht lesbar → leer und <paramref name="unreadable"/>;
    /// der gespeicherte Wert wird nie als Klartext weitergegeben.
    /// </summary>
    private string ReadSecret(string? stored, out bool unreadable)
    {
        var result = _protector.Unprotect(stored);
        unreadable = result.Status == SecretReadStatus.Unreadable;
        return result.Value;
    }

    /// <summary>
    /// Wert zum Speichern: ein eingetragener Schlüssel wird geschützt. Ohne Schlüssel bleibt ein gespeicherter, hier
    /// nicht lesbarer Wert byte-gleich erhalten (z. B. vom alten Computer) – entfernt wird er nur über
    /// <see cref="DiscardUnreadableKeyAsync"/>.
    /// </summary>
    private string StoreSecret(string? plainText, string? existingStored)
    {
        if (!string.IsNullOrWhiteSpace(plainText))
            return _protector.Protect(plainText);

        return !string.IsNullOrWhiteSpace(existingStored) && _protector.Unprotect(existingStored).Status == SecretReadStatus.Unreadable
            ? existingStored
            : string.Empty;
    }

    private static async Task<JsonObject?> ReadJsonObjectAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(filePath)) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Gespeicherter Text an einem Ort (Eigenschaften ohne Groß-/Kleinschreibung); null, wenn er fehlt.</summary>
    internal static string? StoredSecret(JsonNode? root, IReadOnlyList<string> path)
    {
        var (parent, name) = FindProperty(root, path);
        return parent != null && name != null && parent[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>Elternobjekt und tatsächlicher Eigenschaftsname des letzten Pfadteils (ohne Groß-/Kleinschreibung).</summary>
    internal static (JsonObject? Parent, string? Name) FindProperty(JsonNode? root, IReadOnlyList<string> path)
    {
        var node = root;
        for (var i = 0; i < path.Count; i++)
        {
            if (node is not JsonObject obj)
                return (null, null);

            var match = obj.FirstOrDefault(p => string.Equals(p.Key, path[i], StringComparison.OrdinalIgnoreCase));
            if (match.Key == null)
                return (null, null);
            if (i == path.Count - 1)
                return (obj, match.Key);
            node = match.Value;
        }
        return (null, null);
    }

    public async Task DiscardUnreadableKeyAsync(StoredKeyKind kind, string profileName)
    {
        var (profilePath, configPath) = kind switch
        {
            StoredKeyKind.AiApiKey => (ProfileAiKeyPath, ConfigAiKeyPath),
            StoredKeyKind.GeminiTtsKey => (ProfileGeminiKeyPath, ConfigGeminiKeyPath),
            StoredKeyKind.TavilyKey => ((string[]?)null, ConfigTavilyKeyPath),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        if (profilePath != null)
            await ClearUnreadableAsync(GetProfileFilePath(profileName), profilePath);
        await ClearUnreadableAsync(GetConfigFilePath(), configPath);
    }

    /// <summary>Leert einen gespeicherten Schlüssel nur, wenn er hier nicht lesbar ist; alles andere bleibt unverändert.</summary>
    private async Task ClearUnreadableAsync(string filePath, IReadOnlyList<string> path)
    {
        var root = await ReadJsonObjectAsync(filePath);
        var (parent, name) = FindProperty(root, path);
        if (root == null || parent == null || name == null)
            return;

        if (_protector.Unprotect(StoredSecret(root, path)).Status != SecretReadStatus.Unreadable)
            return;

        parent[name] = string.Empty;
        await File.WriteAllTextAsync(filePath, root.ToJsonString(JsonOptions));
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
            var config = JsonSerializer.Deserialize<AiConfig>(json, JsonOptions) ?? new AiConfig();

            // Gespeichert wird geschützt; ältere Formate (auch Klartext) bleiben lesbar und werden beim nächsten Speichern
            // bzw. beim Start (SecretMigration) neu geschützt.
            config.ApiKey = ReadSecret(config.ApiKey, out var unreadable);
            config.ApiKeyUnreadable = unreadable;
            return config;
        }
        catch
        {
            return new AiConfig();
        }
    }

    public async Task SaveAiConfigAsync(AiConfig config)
    {
        var existing = await ReadConfigNodeAsync();
        var node = JsonSerializer.SerializeToNode(ProtectedCopy(config, StoredSecret(existing, ConfigAiKeyPath)), JsonOptions)!.AsObject();

        // Sprachausgabe, Export-, Oberflächen- und Websuche-Einstellungen stehen in derselben Datei und bleiben erhalten
        foreach (var property in new[] { SpeechSettingsProperty, ExportSettingsProperty, UiSettingsProperty, WebSearchSettingsProperty })
        {
            if (existing?[property] is JsonNode kept)
                node[property] = kept.DeepClone();
        }

        await File.WriteAllTextAsync(GetConfigFilePath(), node.ToJsonString(JsonOptions));
    }

    // ---------------- Export (ffmpeg) ----------------

    private const string ExportSettingsProperty = "ExportSettings";

    public async Task<ExportSettings> LoadExportSettingsAsync()
    {
        try
        {
            if ((await ReadConfigNodeAsync())?[ExportSettingsProperty] is JsonObject node &&
                node.Deserialize<ExportSettings>(JsonOptions) is { } settings)
            {
                settings.FfmpegPath ??= string.Empty;
                return settings;
            }
        }
        catch
        {
            // defekter Eintrag → Standard
        }
        return new ExportSettings();
    }

    public async Task SaveExportSettingsAsync(ExportSettings settings)
    {
        Directory.CreateDirectory(_baseDirectory);
        var node = await ReadConfigNodeAsync() ?? new JsonObject();
        node[ExportSettingsProperty] = JsonSerializer.SerializeToNode(settings.Clone(), JsonOptions);
        await File.WriteAllTextAsync(GetConfigFilePath(), node.ToJsonString(JsonOptions));
    }

    // ---------------- Websuche ----------------

    private const string WebSearchSettingsProperty = "WebSearchSettings";

    public async Task<WebSearchSettings> LoadWebSearchSettingsAsync()
    {
        try
        {
            if ((await ReadConfigNodeAsync())?[WebSearchSettingsProperty] is JsonObject node &&
                node.Deserialize<WebSearchSettings>(JsonOptions) is { } settings)
            {
                settings.TavilyApiKey = ReadSecret(settings.TavilyApiKey, out var unreadable);
                settings.TavilyApiKeyUnreadable = unreadable;
                settings.SearxngUrl ??= string.Empty;
                settings.MaxResults = WebSearchSettings.ClampMaxResults(settings.MaxResults);
                if (!Enum.IsDefined(settings.Provider))
                    settings.Provider = WebSearchProviderType.None;
                return settings;
            }
        }
        catch
        {
            // defekter Eintrag → Standard (kein Suchdienst)
        }
        return new WebSearchSettings();
    }

    public async Task SaveWebSearchSettingsAsync(WebSearchSettings settings)
    {
        Directory.CreateDirectory(_baseDirectory);
        var node = await ReadConfigNodeAsync() ?? new JsonObject();
        node[WebSearchSettingsProperty] = JsonSerializer.SerializeToNode(ProtectedCopy(settings, StoredSecret(node, ConfigTavilyKeyPath)), JsonOptions);
        await File.WriteAllTextAsync(GetConfigFilePath(), node.ToJsonString(JsonOptions));
    }

    /// <summary>Kopie der Websuche-Einstellungen mit geschütztem Tavily-Schlüssel (das Original bleibt Klartext).</summary>
    private WebSearchSettings ProtectedCopy(WebSearchSettings settings, string? existingStored)
    {
        var copy = settings.Clone();
        copy.TavilyApiKey = StoreSecret(settings.TavilyApiKey?.Trim(), existingStored);
        copy.SearxngUrl = (settings.SearxngUrl ?? string.Empty).Trim();
        copy.MaxResults = WebSearchSettings.ClampMaxResults(settings.MaxResults);
        return copy;
    }

    public async Task<WebSearchProfileState> LoadWebSearchStateAsync(string profileName)
    {
        // Bewusst ohne globalen Rückfall: fehlt der Eintrag, ist die Websuche aus
        var profile = await ReadProfileFileAsync(profileName);
        return new WebSearchProfileState(profile.WebSearchEnabled, string.IsNullOrWhiteSpace(profile.WebSearchNoticeAcceptedFor) ? null : profile.WebSearchNoticeAcceptedFor);
    }

    public async Task SaveWebSearchStateAsync(string profileName, WebSearchProfileState state)
    {
        var profile = await ReadProfileFileAsync(profileName);
        profile.WebSearchEnabled = state.Enabled;
        profile.WebSearchNoticeAcceptedFor = state.NoticeAcceptedFor;
        await WriteProfileFileAsync(profileName, profile);
    }

    // ---------------- Oberfläche ----------------

    private const string UiSettingsProperty = "UiSettings";

    public async Task<UiSettings> LoadUiSettingsAsync()
    {
        try
        {
            if ((await ReadConfigNodeAsync())?[UiSettingsProperty] is JsonObject node &&
                node.Deserialize<UiSettings>(JsonOptions) is { } settings)
            {
                return settings;
            }
        }
        catch
        {
            // defekter Eintrag → Standard
        }
        return new UiSettings();
    }

    public async Task SaveUiSettingsAsync(UiSettings settings)
    {
        Directory.CreateDirectory(_baseDirectory);
        var node = await ReadConfigNodeAsync() ?? new JsonObject();
        node[UiSettingsProperty] = JsonSerializer.SerializeToNode(settings.Clone(), JsonOptions);
        await File.WriteAllTextAsync(GetConfigFilePath(), node.ToJsonString(JsonOptions));
    }

    // ---------------- Sprachausgabe ----------------

    private const string SpeechSettingsProperty = "SpeechSettings";

    private async Task<JsonObject?> ReadConfigNodeAsync()
    {
        var filePath = GetConfigFilePath();
        if (!File.Exists(filePath))
            return null;

        try
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(filePath)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    public async Task<SpeechSettings> LoadSpeechSettingsAsync(string profileName)
    {
        var profile = await ReadProfileFileAsync(profileName);
        if (profile.SpeechSettings != null)
            return Normalize(profile.SpeechSettings);

        try
        {
            if ((await ReadConfigNodeAsync())?[SpeechSettingsProperty] is JsonObject node &&
                node.Deserialize<SpeechSettings>(JsonOptions) is { } global)
            {
                global.GeminiApiKey = ReadSecret(global.GeminiApiKey, out var unreadable);
                global.GeminiApiKeyUnreadable = unreadable;
                return Normalize(global);
            }
        }
        catch
        {
            // Fehlerhafte globale Einstellung: Standard verwenden
        }

        return new SpeechSettings();
    }

    public async Task SaveSpeechSettingsAsync(string profileName, SpeechSettings settings)
    {
        // 1. Im Profil (maßgeblich); Verschlüsselung übernimmt WriteProfileFileAsync
        var profile = await ReadProfileFileAsync(profileName);
        profile.SpeechSettings = settings.Clone();
        await WriteProfileFileAsync(profileName, profile);

        // 2. Global als Rückfall, ohne die KI-Konfiguration in config.json zu verändern
        var node = await ReadConfigNodeAsync() ?? JsonSerializer.SerializeToNode(ProtectedCopy(new AiConfig(), null), JsonOptions)!.AsObject();
        node[SpeechSettingsProperty] = JsonSerializer.SerializeToNode(ProtectedCopy(settings, StoredSecret(node, ConfigGeminiKeyPath)), JsonOptions);
        await File.WriteAllTextAsync(GetConfigFilePath(), node.ToJsonString(JsonOptions));
    }

    // ---------------- Sprache der KI-Ergebnisse ----------------

    public async Task<string> LoadOutputLanguageAsync(string profileName)
    {
        var profile = await ReadProfileFileAsync(profileName);
        if (!string.IsNullOrWhiteSpace(profile.OutputLanguage))
            return OutputLanguage.Normalize(profile.OutputLanguage);
        return OutputLanguage.Normalize((await LoadUiSettingsAsync()).OutputLanguage);
    }

    public async Task SaveOutputLanguageAsync(string profileName, string setting)
    {
        var value = OutputLanguage.Normalize(setting);
        var profile = await ReadProfileFileAsync(profileName);
        profile.OutputLanguage = value;
        await WriteProfileFileAsync(profileName, profile);

        // Global als Rückfall; die übrigen Oberflächen-Einstellungen bleiben erhalten
        var ui = await LoadUiSettingsAsync();
        ui.OutputLanguage = value;
        await SaveUiSettingsAsync(ui);
    }

    private static SpeechSettings Normalize(SpeechSettings settings)
    {
        settings.GeminiApiKey ??= string.Empty;
        settings.GeminiModel = string.IsNullOrWhiteSpace(settings.GeminiModel) ? GeminiTtsInfo.DefaultModel : settings.GeminiModel.Trim();
        settings.GeminiVoicePrimary = string.IsNullOrWhiteSpace(settings.GeminiVoicePrimary) ? GeminiTtsInfo.DefaultVoicePrimary : settings.GeminiVoicePrimary;
        settings.GeminiVoiceSecondary = string.IsNullOrWhiteSpace(settings.GeminiVoiceSecondary) ? GeminiTtsInfo.DefaultVoiceSecondary : settings.GeminiVoiceSecondary;
        settings.SystemVoicePrimary ??= string.Empty;
        settings.SystemVoiceSecondary ??= string.Empty;
        settings.SystemVoicesByLanguage = settings.SystemVoicesByLanguage == null
            ? new Dictionary<string, SystemVoiceChoice>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SystemVoiceChoice>(
                settings.SystemVoicesByLanguage.Where(kv => kv.Value != null && kv.Key.Length > 0), StringComparer.OrdinalIgnoreCase);
        settings.Rate = SpeechSettings.ClampRate(settings.Rate == 0 ? 1.0 : settings.Rate);
        return settings;
    }
}