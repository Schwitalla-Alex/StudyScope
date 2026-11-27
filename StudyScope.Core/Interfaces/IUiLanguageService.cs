using System.Globalization;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Sprache der Oberfläche zur Laufzeit: welche Sprachen es gibt, welche Kultur zu einer Einstellung gehört und
/// das Umschalten. Umgesetzt in StudyScope.UI (kennt den Programmordner mit den Satellitenassemblies).
/// </summary>
public interface IUiLanguageService
{
    /// <summary>Vorhandene Sprachen (Kulturnamen, z. B. „de“, „en“), die neutrale Sprache immer dabei.</summary>
    IReadOnlyList<string> AvailableCultures { get; }

    /// <summary>Durch <c>STUDYSCOPE_UI_CULTURE</c> erzwungene Sprache (für Prüfungen) oder null.</summary>
    string? OverrideCulture { get; }

    /// <summary>Kultur zu einer Einstellung („system“ oder Kulturname); eine erzwungene Sprache hat Vorrang.</summary>
    CultureInfo Resolve(string? setting);

    /// <summary>Oberflächen- und Formatkultur für die ganze App umstellen.</summary>
    void Apply(CultureInfo culture);
}
