using StudyScope.Core.Localization;

namespace StudyScope.Core.Common;

/// <summary>
/// Füllwörter für die Stichwortsuche (Chat). Die Listen stehen je Sprache in <c>Languages/&lt;sprache&gt;/stopwords.txt</c>
/// und gelten alle gemeinsam – unabhängig von der Oberflächensprache, weil Dokumente und Fragen gemischt sein können.
/// </summary>
public static class StopWords
{
    public const string FileName = "stopwords.txt";

    private static readonly Lazy<HashSet<string>> Words = new(() =>
        LanguageData.Languages(FileName)
            .SelectMany(lang => LanguageData.ReadLines(lang, FileName))
            .Select(w => w.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlySet<string> All => Words.Value;

    public static bool Contains(string word) => Words.Value.Contains(word);
}
