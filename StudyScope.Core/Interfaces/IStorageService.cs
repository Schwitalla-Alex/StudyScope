using System.Collections.Generic;
using System.Threading.Tasks;
using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Abstraktion für das Speichern/Laden der Dokumentenliste, Profile und der KI-Einstellungen in JSON-Dateien.
/// </summary>
public interface IStorageService
{
    // Profil-Verwaltung
    Task<List<UserProfile>> LoadProfilesAsync();
    Task SaveProfilesAsync(IEnumerable<UserProfile> profiles);
    Task<string> GetLastActiveProfileNameAsync();
    Task SetLastActiveProfileNameAsync(string profileName);
    Task<List<string>> GetAllProfileNamesAsync();
    Task DeleteProfileAsync(string profileName);

    // Dokumente für ein bestimmtes Profil laden / speichern
    Task<List<DocumentItem>> LoadDocumentsAsync(string profileName);
    Task SaveDocumentsAsync(string profileName, IEnumerable<DocumentItem> documents);

    // Globale KI-Konfiguration (Fallback / Standard)
    Task<AiConfig> LoadAiConfigAsync();
    Task SaveAiConfigAsync(AiConfig config);

    // NEU: Profilspezifische KI-Konfiguration laden / speichern
    Task<AiConfig> LoadAiConfigForProfileAsync(string profileName);
    Task SaveAiConfigForProfileAsync(string profileName, AiConfig config);
}