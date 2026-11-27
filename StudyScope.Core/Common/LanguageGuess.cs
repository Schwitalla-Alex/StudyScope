using System.Text.RegularExpressions;
using StudyScope.Core.Localization;

namespace StudyScope.Core.Common;

/// <summary>
/// Einfache Erkennung der Sprache von Textstellen über Füllwörter: Gezählt werden nur Wörter, die allein in der
/// Stoppwortliste einer Sprache stehen (<c>Languages/&lt;sprache&gt;/stopwords.txt</c>; „in“ oder „an“ zählen nicht,
/// weil es sie in mehreren Sprachen gibt). Eine neue Sprache mit eigener Liste wird ohne Codeänderung erkannt.
/// Bei zu wenig Text oder ohne deutlichen Vorsprung gibt es kein Ergebnis – dann entscheidet der Aufrufer
/// (z. B. Oberflächensprache).
/// </summary>
public static partial class LanguageGuess
{
    /// <summary>Mindestzahl eindeutiger Füllwörter für ein Ergebnis.</summary>
    public const int MinimumHits = 5;

    /// <summary>Vorsprung vor der zweithäufigsten Sprache (Faktor).</summary>
    public const double MinimumLead = 1.5;

    private static readonly Lazy<IReadOnlyDictionary<string, HashSet<string>>> Distinct = new(LoadDistinctWords);

    /// <summary>Sprachen, die erkannt werden können (Ordner mit Stoppwortliste), z. B. „de“, „en“.</summary>
    public static IReadOnlyCollection<string> Languages => Distinct.Value.Keys.ToList();

    /// <summary>Sprache der Texte (zweistelliger Code) oder null, wenn sie nicht sicher feststeht.</summary>
    public static string? Guess(IEnumerable<string> texts)
    {
        var hits = Distinct.Value.Keys.ToDictionary(k => k, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            foreach (Match m in Word().Matches(text))
            {
                var w = m.Value.ToLowerInvariant();
                foreach (var (lang, words) in Distinct.Value)
                    if (words.Contains(w)) hits[lang]++;
            }
        }

        var ranked = hits.OrderByDescending(h => h.Value).ToList();
        if (ranked.Count == 0 || ranked[0].Value < MinimumHits) return null;
        if (ranked.Count > 1 && ranked[0].Value < ranked[1].Value * MinimumLead) return null;
        return ranked[0].Key;
    }

    private static IReadOnlyDictionary<string, HashSet<string>> LoadDistinctWords()
    {
        var all = LanguageData.Languages(StopWords.FileName)
            .ToDictionary(l => l, l => LanguageData.ReadLines(l, StopWords.FileName)
                .Select(w => w.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        return all.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(w => !all.Any(o => !string.Equals(o.Key, kv.Key, StringComparison.OrdinalIgnoreCase) && o.Value.Contains(w)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
