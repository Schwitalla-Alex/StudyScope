using System.Globalization;

namespace StudyScope.Tests.Common;

/// <summary>Sprachen für Export-Tests (Exporte bekommen ihre Sprache ausdrücklich übergeben).</summary>
internal static class TestCultures
{
    /// <summary>Deutsch – Original der Texte; Tests mit deutschen Erwartungen exportieren in dieser Sprache.</summary>
    public static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Englisch als neutrale Kultur, so wie <c>STUDYSCOPE_UI_CULTURE=en</c> sie setzt.</summary>
    public static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");
}
