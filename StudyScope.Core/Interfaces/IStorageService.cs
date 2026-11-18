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
    Task<AiConfig?> LoadAiConfigForProfileAsync(string profileName);
    Task SaveAiConfigForProfileAsync(string profileName, AiConfig config);

    // Sprachausgabe (Audio-Beiträge): je Profil, Rückfall auf die globale config.json, sonst Standard (Systemstimme)
    Task<SpeechSettings> LoadSpeechSettingsAsync(string profileName);

    /// <summary>Speichert die Sprachausgabe im Profil (maßgeblich) und global (Rückfall); der Gemini-Schlüssel wird verschlüsselt.</summary>
    Task SaveSpeechSettingsAsync(string profileName, SpeechSettings settings);

    /// <summary>Globale Export-Einstellungen (ffmpeg-Pfad) aus <c>config.json</c>; fehlt der Eintrag, Standardwerte.</summary>
    Task<ExportSettings> LoadExportSettingsAsync();

    /// <summary>Speichert die Export-Einstellungen in <c>config.json</c>, ohne KI-Konfiguration und Sprachausgabe zu verändern.</summary>
    Task SaveExportSettingsAsync(ExportSettings settings);

    /// <summary>Globale Einstellungen der Oberfläche (z. B. Seitenleiste eingeklappt) aus <c>config.json</c>; fehlt der Eintrag, Standardwerte.</summary>
    Task<UiSettings> LoadUiSettingsAsync();

    /// <summary>Speichert die Oberflächen-Einstellungen in <c>config.json</c>, ohne die übrigen Einträge zu verändern.</summary>
    Task SaveUiSettingsAsync(UiSettings settings);
}