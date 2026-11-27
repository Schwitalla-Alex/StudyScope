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

    /// <summary>Sprache der KI-Ergebnisse: Profil → global (<c>UiSettings.OutputLanguage</c>) → „ui“ (bereinigt).</summary>
    Task<string> LoadOutputLanguageAsync(string profileName);

    /// <summary>Speichert die Sprache der KI-Ergebnisse im Profil (maßgeblich) und global (Rückfall).</summary>
    Task SaveOutputLanguageAsync(string profileName, string setting);

    /// <summary>Globale Export-Einstellungen (ffmpeg-Pfad) aus <c>config.json</c>; fehlt der Eintrag, Standardwerte.</summary>
    Task<ExportSettings> LoadExportSettingsAsync();

    /// <summary>Speichert die Export-Einstellungen in <c>config.json</c>, ohne KI-Konfiguration und Sprachausgabe zu verändern.</summary>
    Task SaveExportSettingsAsync(ExportSettings settings);

    /// <summary>Globale Einstellungen der Oberfläche (z. B. Seitenleiste eingeklappt) aus <c>config.json</c>; fehlt der Eintrag, Standardwerte.</summary>
    Task<UiSettings> LoadUiSettingsAsync();

    /// <summary>Speichert die Oberflächen-Einstellungen in <c>config.json</c>, ohne die übrigen Einträge zu verändern.</summary>
    Task SaveUiSettingsAsync(UiSettings settings);

    /// <summary>Einstellungen der Websuche (global in <c>config.json</c>; Schlüssel im Speicher Klartext, gespeichert verschlüsselt).</summary>
    Task<WebSearchSettings> LoadWebSearchSettingsAsync();

    /// <summary>Speichert die Einstellungen der Websuche, ohne andere Einträge in <c>config.json</c> zu verändern.</summary>
    Task SaveWebSearchSettingsAsync(WebSearchSettings settings);

    /// <summary>
    /// Websuche des Profils (eingeschaltet, Hinweis bestätigt). Ohne Eintrag: aus. Es gibt bewusst keinen globalen
    /// Rückfall – ein neues Profil sucht nie im Internet, bevor es dort selbst eingeschaltet wurde.
    /// </summary>
    Task<WebSearchProfileState> LoadWebSearchStateAsync(string profileName);

    /// <summary>Speichert die Websuche des Profils sofort (nur im Profil).</summary>
    Task SaveWebSearchStateAsync(string profileName, WebSearchProfileState state);

    /// <summary>
    /// Entfernt einen gespeicherten Schlüssel, der hier nicht lesbar ist (anderer Computer oder anderes Konto), im
    /// Profil und global. Lesbare Schlüssel bleiben unverändert. Ohne diesen Aufruf bleibt ein nicht lesbarer Wert
    /// erhalten, bis ein neuer Schlüssel eingetragen wird.
    /// </summary>
    Task DiscardUnreadableKeyAsync(StoredKeyKind kind, string profileName);
}

/// <summary>Gespeicherte API-Schlüssel.</summary>
public enum StoredKeyKind
{
    /// <summary>Schlüssel der Chat-KI (Profil und global).</summary>
    AiApiKey,

    /// <summary>Eigener Gemini-Schlüssel der Sprachausgabe (Profil und global).</summary>
    GeminiTtsKey,

    /// <summary>Tavily-Schlüssel der Websuche (nur global).</summary>
    TavilyKey
}