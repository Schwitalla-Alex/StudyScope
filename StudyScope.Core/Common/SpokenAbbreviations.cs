using System.Text.RegularExpressions;
using StudyScope.Core.Localization;

namespace StudyScope.Core.Common;

/// <summary>
/// Schreibt Abkürzungen in Sprechtexten aus („z. B.“ → „zum Beispiel“, „e.g.“ → „for example“), damit die
/// Sprachausgabe sie nicht buchstabiert. Die Liste steht je Sprache in <c>Languages/&lt;sprache&gt;/abbreviations.txt</c>
/// (eine Angabe je Zeile: <c>Abkürzung = Ausgeschrieben</c>); eine neue Sprache braucht keine Codeänderung.
/// Leerzeichen innerhalb der Abkürzung sind beliebig („z.B.“, „z. B.“). Endet die Ausschreibung mit einem Punkt
/// (<c>usw. = und so weiter.</c>), kann die Abkürzung einen Satz beenden: Folgt danach ein Großbuchstabe oder das
/// Textende, bleibt der Satzpunkt stehen. Ohne Punkt nie (deutsche Hauptwörter: „z. B. Glukose“).
/// </summary>
public static partial class SpokenAbbreviations
{
    public const string FileName = "abbreviations.txt";

    private static readonly Dictionary<string, IReadOnlyList<(Regex Pattern, string Replacement, bool CanEndSentence)>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>Abkürzungen und Ausschreibungen der Sprache (Code „de“, „en“); leer ohne Datei.</summary>
    public static IReadOnlyList<(string Abbreviation, string Expansion)> Entries(string language) =>
        LanguageData.ReadLines(language, FileName)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Length > 0 && parts[1].Trim().Length > 0)
            .Select(parts => (parts[0].Trim(), parts[1].Trim()))
            .ToList();

    /// <summary>Text mit ausgeschriebenen Abkürzungen der Sprache; ohne Sprache oder Liste unverändert.</summary>
    public static string Expand(string text, string? language)
    {
        if (string.IsNullOrEmpty(text) || SpeechLanguage.Code(language) is not { } code)
            return text;

        foreach (var (pattern, replacement, canEndSentence) in Patterns(code))
        {
            var input = text;
            text = pattern.Replace(input, m =>
            {
                if (!canEndSentence) return replacement;
                // Endet der Satz mit der Abkürzung („… usw. Danach“), bleibt der Satzpunkt erhalten
                var rest = input.AsSpan(m.Index + m.Length);
                var trimmed = rest.TrimStart();
                var sentenceEnds = trimmed.Length == 0 || (trimmed.Length < rest.Length && char.IsUpper(trimmed[0]));
                return sentenceEnds ? replacement + "." : replacement;
            });
        }
        return text;
    }

    private static IReadOnlyList<(Regex Pattern, string Replacement, bool CanEndSentence)> Patterns(string code)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(code, out var cached))
                return cached;

            var list = Entries(code)
                .OrderByDescending(e => e.Abbreviation.Length) // „u. U.“ vor „u.“
                .Select(e => (new Regex(PatternFor(e.Abbreviation), RegexOptions.CultureInvariant),
                    e.Expansion.TrimEnd('.').TrimEnd(), e.Expansion.EndsWith('.')))
                .ToList();
            Cache[code] = list;
            return list;
        }
    }

    /// <summary>„z. B.“ → kein Buchstabe davor, Leerzeichen innen beliebig, kein Buchstabe danach.</summary>
    private static string PatternFor(string abbreviation)
    {
        var parts = WhiteSpace().Split(abbreviation.Replace(".", ". ").Trim());
        var body = string.Join(@"\s*", parts.Select(Regex.Escape));
        var end = abbreviation.EndsWith('.') ? string.Empty : @"(?![\p{L}\p{N}])";
        return @"(?<![\p{L}\p{N}])" + body + end;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
