using System.Globalization;
using System.Text;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Textexporte einer Datentabelle: CSV (für Excel) und Markdown. Die Quelle steht immer als letzte Spalte, ihr Name
/// („Quelle“, „Source“) folgt der Sprache des Exports (<see cref="Header"/> liest die per <see cref="CultureScope"/>
/// gesetzte Sprache).
/// </summary>
public static class DataTableExporter
{
    /// <summary>Kopfzeile: Spalten der Tabelle plus Name der Quellspalte in der aktuellen Sprache.</summary>
    public static List<string> Header(DataTableDocument table) =>
        table.Columns.Append(DataTableDocument.SourceColumnName).ToList();

    /// <summary>Werte einer Zeile passend zu <see cref="Header"/> (fehlende Zellen leer).</summary>
    public static List<string> Values(DataTableDocument table, DataTableRow row) =>
        Enumerable.Range(0, table.Columns.Count)
            .Select(i => i < row.Cells.Count ? row.Cells[i] ?? string.Empty : string.Empty)
            .Append(row.Source ?? string.Empty)
            .ToList();

    // ---------------- CSV ----------------

    /// <summary>
    /// Trennzeichen für Excel: Semikolon bei Formaten mit Dezimalkomma (deutsches Excel erwartet es), sonst Komma.
    /// Maßgeblich ist das regionale Format des Benutzers, nicht die Sprache der Überschriften – Excel liest CSV nach den
    /// Ländereinstellungen des Rechners.
    /// </summary>
    public static char CsvSeparator(CultureInfo formatCulture) =>
        formatCulture.NumberFormat.NumberDecimalSeparator == "," ? ';' : ',';

    /// <summary>CSV mit CRLF-Zeilenenden; Felder mit Trennzeichen, Anführungszeichen oder Umbruch in Anführungszeichen.</summary>
    public static string ToCsv(DataTableDocument table, IReadOnlyList<DataTableRow> rows, CultureInfo culture, char separator)
    {
        using var scope = CultureScope.Use(culture);
        var sb = new StringBuilder();
        AppendCsvLine(sb, Header(table), separator);
        foreach (var row in rows)
            AppendCsvLine(sb, Values(table, row), separator);
        return sb.ToString();
    }

    /// <summary>CSV als UTF-8 mit BOM, damit Excel die Umlaute richtig erkennt.</summary>
    public static byte[] ToCsvBytes(DataTableDocument table, IReadOnlyList<DataTableRow> rows, CultureInfo culture, char separator)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return encoding.GetPreamble().Concat(encoding.GetBytes(ToCsv(table, rows, culture, separator))).ToArray();
    }

    private static void AppendCsvLine(StringBuilder sb, IEnumerable<string> values, char separator)
    {
        sb.Append(string.Join(separator, values.Select(v => EscapeCsv(v, separator))));
        sb.Append("\r\n");
    }

    public static string EscapeCsv(string? value, char separator = ';')
    {
        var v = (value ?? string.Empty).Replace("\r\n", "\n");
        return v.IndexOfAny(new[] { separator, '"', '\n', '\r' }) >= 0 || v.StartsWith(' ') || v.EndsWith(' ')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }

    // ---------------- Markdown ----------------

    public static string ToMarkdown(DataTableDocument table, IReadOnlyList<DataTableRow> rows, CultureInfo culture, bool includeTitle = true)
    {
        using var scope = CultureScope.Use(culture);
        var sb = new StringBuilder();
        if (includeTitle)
        {
            sb.AppendLine($"# {table.Title}");
            sb.AppendLine();
            var parts = new List<string> { ExportNames.Template(table.Template) };
            if (!string.IsNullOrWhiteSpace(table.Topic))
                parts.Add(Loc.Format(ExportStrings.Export_Topic, ("topic", table.Topic)));
            parts.Add(Loc.Format(ExportStrings.Export_RowCount, ("count", rows.Count)));
            sb.AppendLine($"*{string.Join(" · ", parts)}*");
            sb.AppendLine();
        }

        AppendMarkdownTable(sb, table, rows);
        return sb.ToString();
    }

    /// <summary>Nur die Tabelle (für Berichte; Sprache der Quellspalte aus dem <see cref="CultureScope"/> des Aufrufers).</summary>
    public static void AppendMarkdownTable(StringBuilder sb, DataTableDocument table, IReadOnlyList<DataTableRow> rows)
    {
        var header = Header(table);
        sb.AppendLine("| " + string.Join(" | ", header.Select(EscapeMarkdown)) + " |");
        sb.AppendLine("|" + string.Concat(header.Select(_ => " --- |")));
        foreach (var row in rows)
            sb.AppendLine("| " + string.Join(" | ", Values(table, row).Select(EscapeMarkdown)) + " |");
    }

    public static string EscapeMarkdown(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("|", "\\|")
            .Replace("\r\n", "\n")
            .Replace("\n", "<br>")
            .Trim();

    // ---------------- Text ----------------

    /// <summary>Reiner Text: je Zeile „Spalte: Wert“-Blöcke (lesbarer als feste Spaltenbreiten bei langen Zellen).</summary>
    public static void AppendPlainText(StringBuilder sb, DataTableDocument table, IReadOnlyList<DataTableRow> rows)
    {
        var header = Header(table);
        for (var r = 0; r < rows.Count; r++)
        {
            var values = Values(table, rows[r]);
            sb.AppendLine($"{r + 1}.");
            for (var i = 0; i < header.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    sb.AppendLine($"   {header[i]}: {values[i].Replace("\n", " ")}");
            }
        }
    }
}
