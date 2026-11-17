using System.Globalization;
using StudyScope.Core.Common;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels.DataTable;

/// <summary>
/// Sortierung und Filter einer Datentabelle ohne Oberfläche (testbar).
/// Spalte <see cref="SourceColumn"/> steht für die Quellspalte.
/// </summary>
public static class DataTableSorting
{
    /// <summary>Index der Quellspalte (die Quelle ist keine normale Spalte).</summary>
    public const int SourceColumn = -1;

    private static readonly CompareInfo GermanCompare = CultureInfo.GetCultureInfo("de-DE").CompareInfo;

    /// <summary>Text einer Zelle; <see cref="SourceColumn"/> liefert die Quelle.</summary>
    public static string CellValue(DataTableRow row, int column) =>
        column == SourceColumn
            ? row.Source ?? string.Empty
            : column >= 0 && column < row.Cells.Count ? row.Cells[column] ?? string.Empty : string.Empty;

    /// <summary>
    /// Art einer Spalte: Zahl, wenn alle nicht leeren Zellen Zahlen sind (auch mit Einheit/Prozent);
    /// Datum, wenn alle Datumsangaben sind; sonst Text.
    /// </summary>
    public static DataTableColumnKind DetectKind(IEnumerable<DataTableRow> rows, int column) =>
        column == SourceColumn
            ? DataTableColumnKind.Text
            : DataTableValues.DetectKind(rows.Select(r => CellValue(r, column)));

    /// <summary>
    /// Sortiert stabil nach einer Spalte. Leere Zellen stehen immer am Ende (auch absteigend).
    /// Texte werden nach deutschen Regeln ohne Groß-/Kleinschreibung verglichen.
    /// </summary>
    public static List<DataTableRow> Sort(IEnumerable<DataTableRow> rows, int column, bool descending)
    {
        var list = rows.ToList();
        var kind = DetectKind(list, column);

        var indexed = list.Select((row, index) => (row, index, value: CellValue(row, column).Trim())).ToList();
        var filled = indexed.Where(x => x.value.Length > 0).ToList();
        var empty = indexed.Where(x => x.value.Length == 0).Select(x => x.row);

        IComparer<(DataTableRow row, int index, string value)> comparer = Comparer<(DataTableRow row, int index, string value)>.Create((a, b) =>
        {
            var result = CompareValues(a.value, b.value, kind);
            if (descending) result = -result;
            return result != 0 ? result : a.index.CompareTo(b.index);
        });

        filled.Sort(comparer);
        return filled.Select(x => x.row).Concat(empty).ToList();
    }

    public static int CompareValues(string a, string b, DataTableColumnKind kind)
    {
        switch (kind)
        {
            case DataTableColumnKind.Number when DataTableValues.TryParseSortNumber(a, out var na) && DataTableValues.TryParseSortNumber(b, out var nb):
                return na.CompareTo(nb);
            case DataTableColumnKind.Date when DataTableValues.TryParseDateKey(a, out var da) && DataTableValues.TryParseDateKey(b, out var db):
                return da.CompareTo(db);
            default:
                return GermanCompare.Compare(a, b, CompareOptions.IgnoreCase);
        }
    }

    /// <summary>
    /// Filter: Jedes Wort des Suchtexts muss in irgendeiner Zelle (oder der Quelle) vorkommen, ohne Groß-/Kleinschreibung.
    /// Text in Anführungszeichen gilt als zusammenhängender Ausdruck.
    /// </summary>
    public static List<DataTableRow> Filter(IEnumerable<DataTableRow> rows, string? filterText)
    {
        var terms = SplitTerms(filterText);
        if (terms.Count == 0)
            return rows.ToList();

        return rows.Where(row =>
        {
            var haystack = string.Join("\n", row.Cells.Append(row.Source ?? string.Empty));
            return terms.All(t => haystack.Contains(t, StringComparison.CurrentCultureIgnoreCase));
        }).ToList();
    }

    public static List<string> SplitTerms(string? filterText)
    {
        var text = filterText?.Trim() ?? string.Empty;
        var result = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }

            if (text[i] is '"' or '„')
            {
                var close = text.IndexOfAny(new[] { '"', '“' }, i + 1);
                var end = close < 0 ? text.Length : close;
                var phrase = text[(i + 1)..end].Trim();
                if (phrase.Length > 0) result.Add(phrase);
                i = end + 1;
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            result.Add(text[start..i]);
        }

        return result;
    }

    /// <summary>Filter und Sortierung zusammen (Reihenfolge der Ansicht).</summary>
    public static List<DataTableRow> Apply(IEnumerable<DataTableRow> rows, string? filterText, int? sortColumn, bool descending)
    {
        var filtered = Filter(rows, filterText);
        return sortColumn is { } column ? Sort(filtered, column, descending) : filtered;
    }
}
