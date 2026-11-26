using System.Reflection;

namespace StudyScope.Core.Localization;

/// <summary>
/// Sprachabhängige Daten als Dateien statt im Code: <c>StudyScope.Core/Languages/&lt;sprache&gt;/&lt;datei&gt;</c>
/// (z. B. <c>de/stopwords.txt</c>), eingebettet in die Assembly. Eine neue Sprache bekommt einen eigenen Ordner;
/// eine Codeänderung ist nicht nötig. Textdateien: eine Angabe je Zeile, Zeilen mit <c>#</c> sind Kommentare.
/// </summary>
public static class LanguageData
{
    private const string Prefix = "StudyScope.Core.Languages.";
    private static readonly Assembly Assembly = typeof(LanguageData).Assembly;

    /// <summary>Sprachen, für die es die Datei gibt, z. B. <c>Languages("stopwords.txt")</c> → de, en.</summary>
    public static IReadOnlyList<string> Languages(string fileName) =>
        Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            .Select(n => n[Prefix.Length..^(fileName.Length + 1)])
            .Where(lang => lang.Length > 0 && !lang.Contains('.'))
            .OrderBy(lang => lang, StringComparer.Ordinal)
            .ToList();

    /// <summary>Zeilen der Datei einer Sprache ohne Leerzeilen und Kommentare; leer, wenn es die Datei nicht gibt.</summary>
    public static IReadOnlyList<string> ReadLines(string language, string fileName)
    {
        using var stream = Assembly.GetManifestResourceStream(Prefix + language + "." + fileName);
        if (stream == null)
            return Array.Empty<string>();
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                lines.Add(trimmed);
        }
        return lines;
    }
}
