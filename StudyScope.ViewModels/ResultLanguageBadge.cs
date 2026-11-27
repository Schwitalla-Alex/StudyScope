using System.Globalization;
using StudyScope.Core.Localization;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Kennzeichen der Ergebnissprache in Listen und Kopfzeilen (z. B. „EN“): nur wenn ein Ergebnis eine andere Sprache
/// hat als die Oberfläche. Ältere Ergebnisse ohne Angabe (<c>Language</c> = null) bekommen keins.
/// </summary>
public static class ResultLanguageBadge
{
    /// <summary>Kurzzeichen („EN“) oder null, wenn die Sprache fehlt, unbekannt ist oder der Oberfläche entspricht.</summary>
    public static string? Text(string? languageCode) =>
        Differs(languageCode) is { } c ? c.TwoLetterISOLanguageName.ToUpperInvariant() : null;

    /// <summary>Hinweis zum Kennzeichen, z. B. „Sprache des Inhalts: English“ (leer ohne Kennzeichen).</summary>
    public static string Tooltip(string? languageCode) =>
        Differs(languageCode) is { } c
            ? Loc.Format(VmStrings.OutputLanguage_BadgeTooltip, ("language", UiLanguage.NativeName(c.Name)))
            : string.Empty;

    /// <summary>Sprache der meisten Einträge (null zählt nicht; Gleichstand → null) – für Stapel und Fragenpools.</summary>
    public static string? Majority(IEnumerable<string?> languageCodes)
    {
        var groups = languageCodes
            .Select(Neutral)
            .Where(c => c != null)
            .GroupBy(c => c!.TwoLetterISOLanguageName)
            .Select(g => (Code: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();
        if (groups.Count == 0 || (groups.Count > 1 && groups[0].Count == groups[1].Count)) return null;
        return groups[0].Code;
    }

    private static CultureInfo? Differs(string? code)
    {
        var c = Neutral(code);
        if (c == null) return null;
        return string.Equals(c.TwoLetterISOLanguageName, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)
            ? null
            : c;
    }

    private static CultureInfo? Neutral(string? code)
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
}
