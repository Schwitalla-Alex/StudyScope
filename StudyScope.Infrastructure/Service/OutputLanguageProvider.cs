using StudyScope.Core.Interfaces;

namespace StudyScope.Infrastructure.Services;

/// <summary>Einstellung „Sprache der KI-Ergebnisse“ aus dem Speicher (Profil → global → „ui“).</summary>
public sealed class OutputLanguageProvider : IOutputLanguageProvider
{
    private readonly IStorageService _storage;

    public OutputLanguageProvider(IStorageService storage) => _storage = storage;

    public Task<string> GetSettingAsync(string profileName) => _storage.LoadOutputLanguageAsync(profileName);
}
