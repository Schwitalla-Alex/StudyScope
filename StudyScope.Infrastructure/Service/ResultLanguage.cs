using System.Globalization;
using StudyScope.Core.Localization;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Sprache der KI-Ergebnisse in den Diensten (siehe <see cref="OutputLanguage"/>): Einstellung auf den ausgewählten
/// Textstellen auflösen, die Sprachzeile in den (deutschen) Prompt setzen und feste Inhalte in dieser Sprache lesen.
/// </summary>
public static class ResultLanguage
{
    /// <summary>Konkrete Ergebnissprache (neutrale Kultur) für die Einstellung und die Textstellen.</summary>
    public static CultureInfo Resolve(string? setting, IEnumerable<string> texts) => OutputLanguage.Resolve(setting, texts);

    /// <summary>Gespeicherter Code der Sprache am Ergebnis („de“, „en“).</summary>
    public static string Code(CultureInfo culture) => culture.TwoLetterISOLanguageName;

    /// <summary>Kultur aus einem gespeicherten Code (null bzw. unbekannt → null).</summary>
    public static CultureInfo? FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        try
        {
            var c = CultureInfo.GetCultureInfo(code.Trim(), predefinedOnly: true);
            return string.IsNullOrEmpty(c.Name) ? null : CultureInfo.GetCultureInfo(c.TwoLetterISOLanguageName);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ersetzt die frühere feste Sprachangabe („Sprache: Deutsch.“, „gesprochenes Deutsch“) durch genau eine Sprachzeile
    /// am Ende des Prompts. Der übrige Prompt bleibt deutsch (Erkennung der Aufträge durch Fake-KI und JSON-Modus).
    /// </summary>
    public static string ApplyToPrompt(string prompt, CultureInfo culture)
    {
        var p = prompt
            .Replace(" Sprache: Deutsch.", string.Empty, StringComparison.Ordinal)
            .Replace("Natürliches, gesprochenes Deutsch", "Natürliche, gesprochene Sprache", StringComparison.Ordinal);
        if (!p.EndsWith('\n')) p += "\n";
        return p + "- " + OutputLanguage.PromptLine(culture) + "\n";
    }

    /// <summary>Liest feste Inhalte (<c>ContentStrings</c>) in der Ergebnissprache statt in der Oberflächensprache.</summary>
    public static string Content(CultureInfo culture, Func<string> read)
    {
        using var scope = CultureScope.Use(culture);
        return read();
    }
}
