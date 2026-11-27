namespace StudyScope.Core.Interfaces;

/// <summary>
/// Liefert die Einstellung „Sprache der KI-Ergebnisse“ eines Profils („ui“, „documents“ oder Kulturname, siehe
/// <c>OutputLanguage</c>) – Profil → global → „ui“. Die ViewModels geben sie an die Dienste weiter.
/// </summary>
public interface IOutputLanguageProvider
{
    Task<string> GetSettingAsync(string profileName);
}
