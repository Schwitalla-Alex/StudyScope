using System.Globalization;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels;

/// <summary>
/// Sprache der Exporte (feste Texte wie Überschriften, „Quelle“, Bedientexte in HTML, <c>lang</c>-Attribute).
/// Alle ViewModels übergeben diesen Wert ausdrücklich an die Export-Methoden der Dienste (Test
/// <c>ExportLocalizationTests.ViewModels_PassTheExportCulture_ToEveryExport</c>).
/// <para>
/// Seit 3c-2 ist das die Sprache des Ergebnisses (<c>Language</c>, z. B. „en“), sonst – bei älteren Daten ohne
/// Angabe – die Oberflächensprache. Sammlungen mit gemischter Sprache (Karteikarten, Quizfragen) nehmen die Sprache
/// der meisten Einträge, bei Gleichstand oder ganz ohne Angabe die Oberflächensprache. Ein Bericht folgt seiner eigenen
/// Sprache, ältere Berichte ohne Angabe der Mehrheit der eingebetteten Inhalte.
/// </para>
/// </summary>
public static class ExportCulture
{
    /// <summary>Sprache eines einzelnen Ergebnisses (gespeicherter Code oder null → Oberfläche).</summary>
    public static CultureInfo For(string? languageCode)
    {
        var ui = CultureInfo.CurrentUICulture;
        var culture = TryGet(languageCode);
        // Gleiche Sprache wie die Oberfläche: deren Kultur behalten (Region, z. B. de-CH, für Ressourcen und Format)
        if (culture == null || SameLanguage(culture, ui)) return ui;
        return culture;
    }

    /// <summary>Sprache der meisten Einträge (null zählt nicht); Gleichstand oder keine Angabe → Oberfläche.</summary>
    public static CultureInfo ForMajority(IEnumerable<string?> languageCodes)
    {
        var counts = languageCodes
            .Select(TryGet)
            .Where(c => c != null)
            .GroupBy(c => c!.TwoLetterISOLanguageName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Code: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ToList();
        if (counts.Count == 0) return CultureInfo.CurrentUICulture;
        if (counts.Count > 1 && counts[0].Count == counts[1].Count) return CultureInfo.CurrentUICulture;
        return For(counts[0].Code);
    }

    /// <summary>Karteikarten: Mehrheit der exportierten Karten.</summary>
    public static CultureInfo For(IEnumerable<Flashcard> cards) => ForMajority(cards.Select(c => c.Language));

    /// <summary>Bericht: eigene Sprache, sonst Mehrheit der eingebetteten Inhalte (je Inhalt eine Stimme).</summary>
    public static CultureInfo For(Report report, ReportStudioContent studio)
    {
        if (TryGet(report.Language) != null) return For(report.Language);
        var codes = new List<string?>();
        if (studio.Mindmap != null) codes.Add(studio.Mindmap.Language);
        if (studio.Presentation != null) codes.Add(studio.Presentation.Language);
        codes.AddRange(studio.Flashcards.Select(c => c.Language));
        codes.AddRange(studio.QuizQuestions.Select(q => q.Language));
        codes.AddRange(studio.DataTables.Select(t => t.Table.Language));
        codes.AddRange(studio.AudioScripts.Select(a => a.Language));
        codes.AddRange(studio.VideoScripts.Select(v => v.Language));
        return ForMajority(codes);
    }

    private static bool SameLanguage(CultureInfo a, CultureInfo b) =>
        string.Equals(a.TwoLetterISOLanguageName, b.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase);

    private static CultureInfo? TryGet(string? code)
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
