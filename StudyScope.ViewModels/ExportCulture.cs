using System.Globalization;

namespace StudyScope.ViewModels;

/// <summary>
/// Sprache der Exporte (feste Texte wie Überschriften, „Quelle“, Bedientexte in HTML, <c>lang</c>-Attribute).
/// Alle ViewModels übergeben diesen Wert ausdrücklich an die Export-Methoden der Dienste. Bis Phase 3c ist es die
/// Oberflächensprache; 3c stellt hier auf die Sprache der KI-Ergebnisse um.
/// </summary>
public static class ExportCulture
{
    public static CultureInfo Current => CultureInfo.CurrentUICulture;
}
