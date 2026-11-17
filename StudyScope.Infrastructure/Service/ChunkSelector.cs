namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Auswahl der Textstellen, die an die KI gehen (gemeinsam für Mindmap, Präsentation, Quiz, Berichte und Datentabellen):
/// gleichmäßig über die Dokumente verteilt oder per Stichwort-Treffer zu einem Suchbegriff.
/// Die generischen Fassungen arbeiten auf beliebigen Elementen (z. B. Textstellen mit Quelldokument),
/// bewertet wird jeweils der Text aus <c>getText</c>.
/// </summary>
public static class ChunkSelector
{
    private static readonly char[] KeywordSeparators = { ' ', ',', '.', ';', ':', '(', ')', '-', '–', '/', '„', '“', '"', '?', '!' };

    /// <summary>
    /// Wählt gleichmäßig verteilte Textstellen aus, damit das ganze Dokument abgedeckt wird
    /// und nicht nur der Anfang (wie bei einem einfachen Take(n)). Leere Textstellen fallen weg.
    /// </summary>
    public static List<string> SelectRepresentative(IEnumerable<string>? chunks, int maxCount) =>
        SelectRepresentative(chunks, maxCount, c => c);

    public static List<T> SelectRepresentative<T>(IEnumerable<T>? items, int maxCount, Func<T, string> getText)
    {
        var list = items?.Where(i => !string.IsNullOrWhiteSpace(getText(i))).ToList() ?? new List<T>();
        if (maxCount <= 0)
            return new List<T>();
        if (list.Count <= maxCount)
            return list;

        var result = new List<T>(maxCount);
        for (var i = 0; i < maxCount; i++)
        {
            // Indizes von 0 bis Count-1 gleichmäßig verteilt (erste und letzte Textstelle sind dabei)
            var index = maxCount == 1 ? 0 : (int)Math.Round(i * (list.Count - 1) / (double)(maxCount - 1));
            result.Add(list[index]);
        }

        return result;
    }

    /// <summary>
    /// Wählt die zum Suchbegriff passenden Textstellen (Stichwort-Treffer, Wörter ab 4 Buchstaben) und wechselt
    /// je <paramref name="batchIndex"/> den Ausschnitt, damit nicht immer dieselben Stellen abgefragt werden.
    /// Gibt es zu wenige Treffer, wird mit gleichmäßig verteilten Textstellen aufgefüllt.
    /// </summary>
    public static List<string> SelectByKeywords(IReadOnlyList<string> chunks, string searchText, int batchIndex, int count) =>
        SelectByKeywords(chunks, searchText, batchIndex, count, c => c);

    public static List<T> SelectByKeywords<T>(IReadOnlyList<T> items, string searchText, int batchIndex, int count, Func<T, string> getText)
    {
        var all = items.Where(i => !string.IsNullOrWhiteSpace(getText(i))).ToList();
        if (count <= 0)
            return new List<T>();
        if (all.Count <= count)
            return all;

        var keywords = Keywords(searchText);
        var ranked = all
            .Select((item, index) => (item, index, score: Score(getText(item), keywords)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Take(count * 3)
            .Select(x => x.item)
            .ToList();

        // Zu wenige Treffer: mit verteilten Textstellen auffüllen
        if (ranked.Count < count)
        {
            foreach (var item in SelectRepresentative(all, count * 3, getText))
            {
                if (!ranked.Contains(item))
                    ranked.Add(item);
            }
        }

        var start = (Math.Max(0, batchIndex) * (count / 2)) % ranked.Count;
        return Enumerable.Range(0, Math.Min(count, ranked.Count))
            .Select(i => ranked[(start + i) % ranked.Count])
            .ToList();
    }

    /// <summary>Stichwörter eines Suchtexts: kleingeschrieben, ab 4 Zeichen, ohne Satzzeichen.</summary>
    public static HashSet<string> Keywords(string? text) =>
        (text ?? string.Empty).ToLowerInvariant()
            .Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4)
            .ToHashSet();

    /// <summary>Anzahl der Stichwort-Vorkommen in einer Textstelle.</summary>
    public static int Score(string chunk, IReadOnlyCollection<string> keywords)
    {
        if (keywords.Count == 0)
            return 0;

        var lower = chunk.ToLowerInvariant();
        return keywords.Sum(k => CountOccurrences(lower, k));
    }

    private static int CountOccurrences(string text, string word)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += word.Length;
        }
        return count;
    }
}
