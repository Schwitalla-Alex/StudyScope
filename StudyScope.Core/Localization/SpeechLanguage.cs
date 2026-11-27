using System.Globalization;
using StudyScope.Core.Common;

namespace StudyScope.Core.Localization;

/// <summary>
/// Sprache, in der ein Beitrag vorgelesen wird (Audio-Skript, Sprechtexte eines Videos): die am Ergebnis gespeicherte
/// Sprache (<c>Language</c>), bei älteren Daten ohne Angabe die am Text erkannte Sprache (<see cref="LanguageGuess"/>),
/// sonst die Einstellung „Sprache der KI-Ergebnisse“ (<see cref="OutputLanguage"/>). Ergebnis ist immer ein
/// zweistelliger Code („de“, „en“).
/// </summary>
public static class SpeechLanguage
{
    /// <summary>Sprache, wenn keine Angabe vorliegt (vor 3c-3 wurde immer deutsch vorgelesen).</summary>
    public const string Default = "de";

    /// <summary>
    /// Vorlesesprache eines Inhalts: gespeicherte Sprache → erkannte Sprache des Textes → Ergebnissprache der
    /// Einstellung <paramref name="outputSetting"/> (ohne Text: „Wie die Dokumente“ ergibt die Oberflächensprache).
    /// </summary>
    public static string For(string? storedLanguage, IEnumerable<string> texts, string? outputSetting)
    {
        if (Code(storedLanguage) is { } stored)
            return stored;
        var list = texts as IReadOnlyCollection<string> ?? texts.ToList();
        return LanguageGuess.Guess(list) ?? OutputLanguage.Resolve(outputSetting, list).TwoLetterISOLanguageName;
    }

    /// <summary>Zweistelliger Code einer Sprachangabe („en-GB“ → „en“); null bei leer oder unbekannt.</summary>
    public static string? Code(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        try
        {
            var culture = CultureInfo.GetCultureInfo(language.Trim().Replace('_', '-'), predefinedOnly: true);
            return string.IsNullOrEmpty(culture.Name) ? null : culture.TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            // Stimmen melden manchmal nur „en-x-…“ o. Ä.: dann der Sprachteil vor dem Bindestrich
            var head = language.Trim().Split('-', '_')[0];
            return head.Length is 2 or 3 && head.All(char.IsAsciiLetter) ? head.ToLowerInvariant() : null;
        }
    }

    /// <summary>Gehört die Sprachangabe einer Stimme („en-GB“, „en_US“) zur Sprache (Code „en“)?</summary>
    public static bool Matches(string? voiceLanguage, string? language)
    {
        var wanted = Code(language);
        return wanted != null && string.Equals(Code(voiceLanguage), wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sprachangabe mit Region für die Web Speech API, wenn keine passende Stimme gefunden wird („en“ → „en-US“,
    /// „de“ → „de-DE“).
    /// </summary>
    public static string SpecificTag(string? language)
    {
        var code = Code(language) ?? Default;
        try
        {
            return CultureInfo.CreateSpecificCulture(code).Name;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}
