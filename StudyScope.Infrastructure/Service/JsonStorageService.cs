using System.Text.Json;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

public class JsonStorageService : IStorageService
{
    private readonly string _baseDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // Standard-Konstruktor für die laufende Anwendung (nutzt AppData)
    public JsonStorageService()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudyScope"))
    {
    }

    // Konstruktor mit 1 Argument für Unit-Tests (nutzt isolierten Temp-Ordner)
    public JsonStorageService(string baseDirectory)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudyScope")
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

    public async Task<List<DocumentItem>> LoadDocumentsAsync(string profileName)
    {
        var filePath = GetProfileFilePath(profileName);
        if (!File.Exists(filePath))
        {
            return new List<DocumentItem>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var docs = JsonSerializer.Deserialize<List<DocumentItem>>(json, JsonOptions) ?? new List<DocumentItem>();

            foreach (var doc in docs)
            {
                doc.IsMissing = !File.Exists(doc.FilePath);
            }

            return docs;
        }
        catch
        {
            return new List<DocumentItem>();
        }
    }

    public async Task SaveDocumentsAsync(string profileName, IEnumerable<DocumentItem> documents)
    {
        var filePath = GetProfileFilePath(profileName);
        var json = JsonSerializer.Serialize(documents, JsonOptions);
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
                var json = JsonSerializer.Serialize(new List<DocumentItem>(), JsonOptions);
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