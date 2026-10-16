using System;
using System.Collections.Generic;
using System.Text;

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

    // Globale KI-Konfiguration
    Task<AiConfig> LoadAiConfigAsync();
    Task SaveAiConfigAsync(AiConfig config);
   
}
