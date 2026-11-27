using System.Globalization;
using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Datentabellen: strukturierte Angaben per KI aus Dokumentauszügen heraussuchen (nach Vorlage oder eigener Vorgabe),
/// Zeilen ergänzen, Speicherung (mehrere Tabellen je Profil) und Export als CSV, Excel oder Markdown.
/// </summary>
public interface IDataTableService
{
    /// <param name="chunks">Textstellen samt Quelldokument (die KI ordnet jeder Zeile ihre Quelle zu).</param>
    /// <param name="customInstruction">Nur bei <see cref="DataTableTemplate.Custom"/>: Vorgabe des Benutzers.</param>
    /// <param name="customColumns">Gewünschte Spalten (verbindlich); leer = Vorschlag der Vorlage bzw. der KI.</param>
    /// <param name="topic">Optionales Thema/Suchbegriff zur Eingrenzung.</param>
    /// <param name="fallbackTitle">Titel, falls die KI keinen liefert.</param>
    Task<DataTableGenerationResult> GenerateAsync(
        IEnumerable<SourceChunk> chunks,
        DataTableTemplate template,
        string customInstruction,
        IReadOnlyList<string> customColumns,
        string topic,
        string fallbackTitle,
        string? outputLanguage = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lässt die KI weitere Zeilen für die vorhandenen Spalten suchen; Wiederholungen werden verworfen.</summary>
    /// <param name="maxNewRows">Höchstzahl neuer Zeilen (Rest bis zur Grenze der Tabelle).</param>
    Task<DataTableRowsResult> GenerateMoreRowsAsync(
        DataTableDocument table,
        IEnumerable<SourceChunk> chunks,
        int maxNewRows,
        string? outputLanguage = null,
        CancellationToken cancellationToken = default);

    Task<DataTableLibrary> LoadLibraryAsync(string profileName);
    Task SaveLibraryAsync(string profileName, DataTableLibrary library);

    /// <summary>
    /// CSV als UTF-8 mit BOM (Excel zeigt Umlaute richtig). Trennzeichen nach dem regionalen Format des Benutzers
    /// (Semikolon bei Dezimalkomma, sonst Komma), Kopf der Quellspalte in <paramref name="culture"/>.
    /// </summary>
    byte[] ExportToCsv(DataTableDocument table, IReadOnlyList<DataTableRow> rows, CultureInfo culture);

    /// <summary>Excel-Arbeitsmappe (.xlsx) mit fixierter Kopfzeile und Autofilter.</summary>
    Task ExportToXlsxAsync(DataTableDocument table, IReadOnlyList<DataTableRow> rows, string filePath, CultureInfo culture);

    string ExportToMarkdown(DataTableDocument table, IReadOnlyList<DataTableRow> rows, CultureInfo culture);
}
