using System.Globalization;
using System.Text;
using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Core.Localization;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Datentabellen per KI nach Vorlage (Begriffe, Vergleich, Zeitleiste, Personen, Formeln, eigene Vorgabe),
/// Ergänzen weiterer Zeilen, Speicherung aller Tabellen eines Profils unter <c>DataTables/&lt;Profil&gt;.json</c> und Exporte.
/// </summary>
public class DataTableService : IDataTableService
{
    /// <summary>Höchstzahl an Textstellen je KI-Aufruf (Kontextgröße lokaler Modelle).</summary>
    public const int MaxChunks = 16;

    /// <summary>Höchstzahl an Spalten, die aus einer KI-Antwort übernommen werden.</summary>
    public const int MaxColumns = 8;

    /// <summary>Höchstzahl an Zeilen, die aus einer KI-Antwort übernommen werden.</summary>
    public const int MaxRows = 60;

    /// <summary>So viele vorhandene Zeilen werden der KI beim Ergänzen genannt.</summary>
    public const int MaxExistingRowsInPrompt = 40;

    public const int MaxCellLength = 500;
    public const int MaxColumnNameLength = 60;
    private const int MaxTitleLength = 200;

    private static readonly string[] TitleKeys = { "title", "titel", "name", "tabellenname", "überschrift", "ueberschrift" };
    private static readonly string[] ColumnsKeys = { "columns", "spalten", "headers", "header", "kopfzeile", "felder", "fields" };
    private static readonly string[] RowsKeys = { "rows", "zeilen", "data", "daten", "eintraege", "einträge", "entries", "records", "datensaetze", "datensätze" };
    private static readonly string[] CellsKeys = { "cells", "zellen", "values", "werte", "felder" };
    private static readonly string[] SourceKeys = { "source", "quelle", "quelldokument", "dokument", "document", "datei", "file", "fundstelle" };
    private static readonly string[] ColumnNameKeys = { "name", "title", "titel", "label", "header", "spalte", "column" };

    private readonly ILlmService _llmService;
    private readonly string _baseDirectory;

    /// <param name="baseDirectory">Basisordner für die Speicherung (Tests); Standard ist <see cref="AppPaths.UserDataDirectory"/>.</param>
    public DataTableService(ILlmService llmService, string? baseDirectory = null)
    {
        _llmService = llmService;
        _baseDirectory = baseDirectory ?? AppPaths.UserDataDirectory;
    }

    // ---------------- Erzeugung ----------------

    public async Task<DataTableGenerationResult> GenerateAsync(
        IEnumerable<SourceChunk> chunks,
        DataTableTemplate template,
        string customInstruction,
        IReadOnlyList<string> customColumns,
        string topic,
        string fallbackTitle,
        string? outputLanguage = null,
        CancellationToken cancellationToken = default)
    {
        var all = chunks.ToList();
        var selected = SelectChunks(all, topic, batchIndex: 0);
        if (selected.Count == 0)
            return new DataTableGenerationResult { FailureReason = MindmapFailureReason.NoContent };

        var language = ResultLanguage.Resolve(outputLanguage, selected.Select(c => c.Text));
        var columns = NormalizeColumnNames(customColumns);
        var prompt = ResultLanguage.ApplyToPrompt(BuildPrompt(template, customInstruction, columns, topic), language);
        var raw = await _llmService.AskQuestionAsync(TaskPrompt.Mark(TaskKind.DataTable, prompt), selected.Select(LabelChunk), cancellationToken);

        var sources = all.Select(c => c.SourceDocument).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var fallback = string.IsNullOrWhiteSpace(fallbackTitle) ? ResultLanguage.Content(language, () => ContentStrings.Content_DataTableTitle) : fallbackTitle;
        var parsed = AnalyzeResponse(raw, fallback, sources, columns.Count > 0 ? columns : null);

        if (parsed.Value != null)
        {
            var table = parsed.Value;
            table.Language = ResultLanguage.Code(language);
            table.Template = template;
            table.CustomInstruction = template == DataTableTemplate.Custom ? customInstruction.Trim() : string.Empty;
            table.Topic = topic?.Trim() ?? string.Empty;
            table.Rows = RemoveDuplicates(table, table.Rows, Enumerable.Empty<DataTableRow>(), out _);
            if (table.Rows.Count == 0)
                return new DataTableGenerationResult { RawResponse = raw ?? string.Empty, FailureReason = MindmapFailureReason.NoTopics };
        }

        return new DataTableGenerationResult
        {
            Table = parsed.Value,
            RawResponse = raw ?? string.Empty,
            WasRepaired = parsed.WasRepaired,
            FailureReason = parsed.FailureReason
        };
    }

    public async Task<DataTableRowsResult> GenerateMoreRowsAsync(
        DataTableDocument table,
        IEnumerable<SourceChunk> chunks,
        int maxNewRows,
        string? outputLanguage = null,
        CancellationToken cancellationToken = default)
    {
        var all = chunks.ToList();
        var selected = SelectChunks(all, table.Topic, batchIndex: table.AppendCount + 1);
        if (selected.Count == 0 || table.Columns.Count == 0)
            return new DataTableRowsResult { FailureReason = MindmapFailureReason.NoContent };

        // Neue Zeilen in der Sprache der vorhandenen Tabelle (ältere Tabellen ohne Angabe: Einstellung)
        var language = ResultLanguage.FromCode(table.Language) ?? ResultLanguage.Resolve(outputLanguage, selected.Select(c => c.Text));
        var prompt = ResultLanguage.ApplyToPrompt(BuildMoreRowsPrompt(table), language);
        var raw = await _llmService.AskQuestionAsync(TaskPrompt.Mark(TaskKind.DataTable, prompt), selected.Select(LabelChunk), cancellationToken);

        var sources = all.Select(c => c.SourceDocument).Concat(table.SourceDocuments).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var parsed = AnalyzeResponse(raw, table.Title, sources, table.Columns);
        if (parsed.Value == null)
        {
            return new DataTableRowsResult
            {
                RawResponse = raw ?? string.Empty,
                WasRepaired = parsed.WasRepaired,
                FailureReason = parsed.FailureReason
            };
        }

        var rows = RemoveDuplicates(table, parsed.Value.Rows, table.Rows, out var duplicates);
        return new DataTableRowsResult
        {
            Rows = rows.Take(Math.Max(0, maxNewRows)).ToList(),
            DuplicatesSkipped = duplicates,
            RawResponse = raw ?? string.Empty,
            WasRepaired = parsed.WasRepaired,
            FailureReason = MindmapFailureReason.None
        };
    }

    /// <summary>
    /// Textstellen für einen Aufruf: mit Thema per Stichwort-Treffer, sonst gleichmäßig verteilt.
    /// Beim Ergänzen (<paramref name="batchIndex"/> &gt; 0) wechselt der Ausschnitt, damit die KI neue Stellen sieht.
    /// </summary>
    public static List<SourceChunk> SelectChunks(IReadOnlyList<SourceChunk> chunks, string? topic, int batchIndex)
    {
        if (string.IsNullOrWhiteSpace(topic) && batchIndex == 0)
            return ChunkSelector.SelectRepresentative(chunks, MaxChunks, c => c.Text);

        return ChunkSelector.SelectByKeywords(chunks, topic ?? string.Empty, batchIndex, MaxChunks, c => c.Text);
    }

    /// <summary>Textstelle mit vorangestellter Quellenangabe, damit die KI jeder Zeile ihr Dokument zuordnen kann.</summary>
    public static string LabelChunk(SourceChunk chunk) => $"[Quelle: {chunk.SourceDocument}]\n{chunk.Text}";

    /// <summary>
    /// Prompt für eine neue Tabelle. Beginnt mit „Erstelle eine Datentabelle“ (Erkennung der Fake-KI) und verlangt ein
    /// JSON-Array mit genau einem Objekt (der Dispatcher gibt bei „JSON-Array“ für einige Provider den JSON-Modus vor).
    /// </summary>
    public static string BuildPrompt(DataTableTemplate template, string customInstruction, IReadOnlyList<string> customColumns, string? topic)
    {
        var sb = new StringBuilder();
        sb.Append("Erstelle eine Datentabelle aus den bereitgestellten Textauszügen.\n");
        sb.Append("Art der Tabelle: ").Append(TemplateInstruction(template, customInstruction)).Append('\n');
        AppendTopic(sb, topic);

        if (customColumns.Count > 0)
        {
            sb.Append("Spalten (verbindlich, genau diese in dieser Reihenfolge): ").Append(string.Join(" | ", customColumns)).Append('\n');
        }
        else
        {
            var suggested = DataTableTemplates.SuggestedColumns(template);
            if (suggested.Count > 0)
                sb.Append("Spalten (Vorschlag – passe sie an den Inhalt an, 2 bis 6 Spalten): ").Append(string.Join(" | ", suggested)).Append('\n');
            else
                sb.Append("Spalten: Wähle 2 bis 6 passende Spalten.\n");
        }

        AppendFormatRules(sb);
        sb.Append("- Höchstens 30 Zeilen, jede Zeile nur einmal.\n");
        return sb.ToString();
    }

    /// <summary>Prompt für „Weitere Zeilen ergänzen“: feste Spalten, vorhandene Zeilen werden genannt.</summary>
    public static string BuildMoreRowsPrompt(DataTableDocument table)
    {
        var sb = new StringBuilder();
        sb.Append("Erstelle eine Datentabelle: Ergänze weitere Zeilen für die vorhandene Tabelle „").Append(table.Title).Append("“ aus den bereitgestellten Textauszügen.\n");
        sb.Append("Art der Tabelle: ").Append(TemplateInstruction(table.Template, table.CustomInstruction)).Append('\n');
        AppendTopic(sb, table.Topic);
        sb.Append("Spalten (verbindlich, genau diese in dieser Reihenfolge): ").Append(string.Join(" | ", table.Columns)).Append('\n');
        AppendFormatRules(sb);
        sb.Append("- Höchstens 20 neue Zeilen.\n");

        var existing = table.Rows
            .Select(r => RowLabel(table, r.Cells))
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(MaxExistingRowsInPrompt)
            .ToList();
        if (existing.Count > 0)
        {
            sb.Append("Diese Zeilen gibt es bereits – nenne nur neue, andere Einträge:\n");
            foreach (var label in existing)
                sb.Append("- ").Append(label).Append('\n');
        }

        return sb.ToString();
    }

    private static void AppendTopic(StringBuilder sb, string? topic)
    {
        if (!string.IsNullOrWhiteSpace(topic))
            sb.Append("Thema: Nimm nur Angaben zum Thema „").Append(topic.Trim()).Append("“ auf.\n");
    }

    private static void AppendFormatRules(StringBuilder sb)
    {
        sb.Append("Antworte AUSSCHLIESSLICH als gültiges JSON-Array mit genau EINEM Objekt, ohne Einleitung, ohne Markdown und ohne Schlusssatz.\n");
        sb.Append("Schema:\n");
        sb.Append("[{\"title\": \"Titel der Tabelle\", \"columns\": [\"Spalte 1\", \"Spalte 2\"], ");
        sb.Append("\"rows\": [{\"cells\": [\"Wert zu Spalte 1\", \"Wert zu Spalte 2\"], \"source\": \"Dateiname\"}]}]\n");
        sb.Append("Regeln:\n");
        sb.Append("- Jeder Textauszug beginnt mit [Quelle: Dateiname]. Gib in \"source\" den Dateinamen des Auszugs an, aus dem die Zeile stammt.\n");
        sb.Append("- \"cells\" hat genau so viele Einträge wie \"columns\", in derselben Reihenfolge. Keine eigene Spalte für die Quelle.\n");
        sb.Append("- Verwende NUR Angaben, die in den Auszügen stehen. Steht eine Angabe nicht im Text, lass die Zelle leer (\"\") – nichts erfinden, nichts schätzen.\n");
        sb.Append("- Zellen kurz und sachlich (Stichworte oder ein Satz). Sprache: Deutsch.\n");
    }

    public static string TemplateInstruction(DataTableTemplate template, string customInstruction) => template switch
    {
        DataTableTemplate.Terms => "Begriffe & Definitionen – die wichtigen Fachbegriffe mit kurzer Definition (und einem Beispiel, falls im Text vorhanden).",
        DataTableTemplate.Comparison => "Vergleich – stelle die im Text behandelten Theorien, Verfahren oder Modelle mit ihren Merkmalen gegenüber (eine Zeile je Gegenstand).",
        DataTableTemplate.Timeline => "Zeitleiste – Ereignisse mit Datum und Bedeutung, in zeitlicher Reihenfolge.",
        DataTableTemplate.People => "Personen & Beiträge – im Text genannte Personen mit ihren Beiträgen, Werken und Lebensdaten.",
        DataTableTemplate.Formulas => "Formeln & Kennzahlen – Formeln, Kennzahlen und Größen mit Bedeutung und Einheit (Formeln als Text, z. B. a² + b² = c²).",
        _ => string.IsNullOrWhiteSpace(customInstruction)
            ? "Tabelle nach freier Wahl der wichtigsten Angaben."
            : $"Nach folgender Vorgabe des Benutzers: „{customInstruction.Trim()}“"
    };

    // ---------------- Auswertung ----------------

    /// <summary>
    /// Wertet die KI-Antwort aus (nachsichtig, inkl. Reparatur abgeschnittener Antworten). Zeilen dürfen Arrays,
    /// Objekte mit „cells“ oder Objekte mit Spaltennamen als Schlüsseln sein; deutsche Schlüssel werden erkannt.
    /// Ist <paramref name="targetColumns"/> gesetzt, werden die Zellen diesen Spalten zugeordnet (über den Namen,
    /// sonst über die Position).
    /// </summary>
    public static LenientJson.Result<DataTableDocument> AnalyzeResponse(
        string? rawResponse,
        string fallbackTitle,
        IReadOnlyList<string>? knownSources = null,
        IReadOnlyList<string>? targetColumns = null) =>
        LenientJson.Analyze(rawResponse, element => BuildTable(element, fallbackTitle, knownSources ?? Array.Empty<string>(), targetColumns));

    private static bool IsTable(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && LenientJson.GetProperty(element, RowsKeys) is { ValueKind: JsonValueKind.Array };

    private static bool IsRowObject(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && !IsTable(element) &&
        element.EnumerateObject().Any(p => p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Array);

    private static DataTableDocument? BuildTable(JsonElement element, string fallbackTitle, IReadOnlyList<string> knownSources, IReadOnlyList<string>? targetColumns)
    {
        // Hülle aus dem JSON-Modus (z. B. {"tabelle": {...}} oder {"items": [...]}) auspacken
        element = LenientJson.Unwrap(element, e => IsTable(e) || LenientJson.IsArrayOf(e, IsTable));

        string title = string.Empty;
        JsonElement? columnsElement = null;
        JsonElement rowsElement;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var tableObject = element.EnumerateArray().FirstOrDefault(IsTable);
            if (tableObject.ValueKind == JsonValueKind.Object)
            {
                element = tableObject;
            }
            else
            {
                // Direkt eine Liste von Zeilen (Objekte oder Arrays)
                rowsElement = element;
                return Finish(title, null, rowsElement, fallbackTitle, knownSources, targetColumns);
            }
        }

        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (LenientJson.GetProperty(element, RowsKeys) is not { ValueKind: JsonValueKind.Array } rows)
        {
            // Hülle ohne bekannten Schlüssel, z. B. {"begriffe": [{...}, {...}]}: erste Liste von Zeilen verwenden
            var list = element.EnumerateObject()
                .Select(p => p.Value)
                .FirstOrDefault(v => v.ValueKind == JsonValueKind.Array && v.EnumerateArray().Any(r => IsRowObject(r) || r.ValueKind == JsonValueKind.Array));
            return list.ValueKind == JsonValueKind.Array
                ? Finish(string.Empty, null, list, fallbackTitle, knownSources, targetColumns)
                : null;
        }

        title = LenientJson.GetString(element, TitleKeys);
        columnsElement = LenientJson.GetProperty(element, ColumnsKeys);
        rowsElement = rows;
        return Finish(title, columnsElement, rowsElement, fallbackTitle, knownSources, targetColumns);
    }

    private static DataTableDocument? Finish(
        string title,
        JsonElement? columnsElement,
        JsonElement rowsElement,
        string fallbackTitle,
        IReadOnlyList<string> knownSources,
        IReadOnlyList<string>? targetColumns)
    {
        var columns = ReadColumnNames(columnsElement);

        // Spaltennamen aus den Schlüsseln der Zeilenobjekte ableiten, falls die KI keine genannt hat
        if (columns.Count == 0)
        {
            foreach (var row in rowsElement.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object))
            {
                var cells = LenientJson.GetProperty(row, CellsKeys);
                if (cells is { ValueKind: JsonValueKind.Object } cellMap)
                    AddKeys(columns, cellMap);
                else if (cells is not { ValueKind: JsonValueKind.Array })
                    AddKeys(columns, row);
            }
        }

        // Quelle als eigene Spalte? Herausnehmen und als Quelle der Zeile verwenden
        var sourceColumn = columns.FindIndex(c => SourceKeys.Any(k => string.Equals(Normalize(c), Normalize(k), StringComparison.Ordinal)));

        var rawRows = new List<(List<string> Cells, string Source)>();
        foreach (var row in rowsElement.EnumerateArray())
        {
            var parsed = ReadRow(row, columns);
            if (parsed == null)
                continue;

            var (cells, source) = parsed.Value;
            if (sourceColumn >= 0 && sourceColumn < cells.Count)
            {
                if (string.IsNullOrWhiteSpace(source)) source = cells[sourceColumn];
                cells.RemoveAt(sourceColumn);
            }

            rawRows.Add((cells, source));
            if (rawRows.Count >= MaxRows)
                break;
        }

        if (sourceColumn >= 0)
            columns.RemoveAt(sourceColumn);

        // Arrays ohne Spaltennamen: Spalten nach der längsten Zeile benennen
        var width = Math.Max(columns.Count, rawRows.Count == 0 ? 0 : rawRows.Max(r => r.Cells.Count));
        while (columns.Count < width)
            columns.Add($"Spalte {columns.Count + 1}");

        columns = NormalizeColumnNames(columns.Take(Math.Max(MaxColumns, targetColumns?.Count ?? 0)).ToList());

        List<DataTableRow> rows;
        if (targetColumns is { Count: > 0 })
        {
            var map = MapColumns(columns, targetColumns);
            rows = rawRows.Select(r => new DataTableRow
            {
                Cells = map.Select(i => i >= 0 && i < r.Cells.Count ? r.Cells[i] : string.Empty).ToList(),
                Source = ResolveSource(r.Source, knownSources)
            }).ToList();
            columns = targetColumns.ToList();
        }
        else
        {
            rows = rawRows.Select(r => new DataTableRow
            {
                Cells = Enumerable.Range(0, columns.Count).Select(i => i < r.Cells.Count ? r.Cells[i] : string.Empty).ToList(),
                Source = ResolveSource(r.Source, knownSources)
            }).ToList();
        }

        rows = rows.Where(r => r.Cells.Any(c => c.Length > 0)).ToList();
        if (columns.Count == 0 || rows.Count == 0)
            return null;

        return new DataTableDocument
        {
            Title = Clean(title, MaxTitleLength) is { Length: > 0 } t ? t : (string.IsNullOrWhiteSpace(fallbackTitle) ? "Datentabelle" : fallbackTitle.Trim()),
            Columns = columns,
            Rows = rows
        };
    }

    private static void AddKeys(List<string> columns, JsonElement obj)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            // Die Quelle wird mit aufgenommen und in Finish als Quelle der Zeile herausgezogen
            if (!columns.Any(c => string.Equals(c, prop.Name, StringComparison.OrdinalIgnoreCase)))
                columns.Add(prop.Name);
        }
    }

    private static List<string> ReadColumnNames(JsonElement? element)
    {
        var result = new List<string>();
        if (element is not { ValueKind: JsonValueKind.Array } array)
            return result;

        foreach (var item in array.EnumerateArray())
        {
            var name = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object => LenientJson.GetString(item, ColumnNameKeys),
                _ => null
            };
            result.Add(name ?? string.Empty);
        }

        return result;
    }

    /// <summary>Liest eine Zeile: Array, Objekt mit „cells“ (Array oder Objekt) oder Objekt mit Spaltennamen als Schlüsseln.</summary>
    private static (List<string> Cells, string Source)? ReadRow(JsonElement row, IReadOnlyList<string> columns)
    {
        switch (row.ValueKind)
        {
            case JsonValueKind.Array:
                return (row.EnumerateArray().Select(CellText).ToList(), string.Empty);

            case JsonValueKind.Object:
                var source = CellText(LenientJson.GetProperty(row, SourceKeys) ?? default);
                var cells = LenientJson.GetProperty(row, CellsKeys);
                if (cells is { ValueKind: JsonValueKind.Array } cellArray)
                    return (cellArray.EnumerateArray().Select(CellText).ToList(), source);

                var map = cells is { ValueKind: JsonValueKind.Object } cellMap ? cellMap : row;
                var values = columns.Select(c => CellText(GetByColumnName(map, c) ?? default)).ToList();
                if (values.All(v => v.Length == 0))
                {
                    // Schlüssel passen nicht zu den genannten Spalten (z. B. englisch statt deutsch): Reihenfolge nutzen
                    values = map.EnumerateObject()
                        .Where(p => !SourceKeys.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                        .Select(p => CellText(p.Value))
                        .ToList();
                }
                return (values, source);

            case JsonValueKind.String:
                // Zeile als Text „Wert 1 | Wert 2“
                var text = row.GetString() ?? string.Empty;
                return text.Contains('|')
                    ? (text.Split('|').Select(p => Clean(p, MaxCellLength)).ToList(), string.Empty)
                    : (new List<string> { Clean(text, MaxCellLength) }, string.Empty);

            default:
                return null;
        }
    }

    private static JsonElement? GetByColumnName(JsonElement obj, string column)
    {
        var normalized = Normalize(column);
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, column, StringComparison.OrdinalIgnoreCase) || Normalize(prop.Name) == normalized)
                return prop.Value;
        }

        return null;
    }

    private static string CellText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => Clean(value.GetString(), MaxCellLength),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "ja",
        JsonValueKind.False => "nein",
        JsonValueKind.Array => Clean(string.Join(", ", value.EnumerateArray().Select(CellText).Where(s => s.Length > 0)), MaxCellLength),
        _ => string.Empty
    };

    /// <summary>
    /// Ordnet gelieferte Spalten den Zielspalten zu: über den Namen (ohne Groß-/Kleinschreibung und Sonderzeichen),
    /// sonst – bei gleicher Spaltenzahl – über die Position. Liefert je Zielspalte den Index der gelieferten Spalte oder -1.
    /// </summary>
    public static int[] MapColumns(IReadOnlyList<string> delivered, IReadOnlyList<string> target)
    {
        var map = target.Select(t => delivered.ToList().FindIndex(d => Normalize(d) == Normalize(t))).ToArray();
        var matched = map.Count(i => i >= 0);

        if (matched < target.Count)
        {
            // Nicht zugeordnete Spalten der Reihe nach mit den übrigen gelieferten Spalten auffüllen,
            // wenn die Anzahl passt (die KI hat die Spalten nur anders benannt)
            var unused = Enumerable.Range(0, delivered.Count).Where(i => !map.Contains(i)).ToList();
            var missing = Enumerable.Range(0, target.Count).Where(i => map[i] < 0).ToList();
            if (delivered.Count == target.Count || unused.Count == missing.Count)
            {
                for (var k = 0; k < missing.Count && k < unused.Count; k++)
                    map[missing[k]] = unused[k];
            }
        }

        return map;
    }

    /// <summary>
    /// Ordnet die Quellenangabe der KI einem bekannten Dateinamen zu (auch ohne Endung oder als Teil eines längeren Textes).
    /// Ohne Treffer: bei genau einem Dokument dieses, sonst leer.
    /// </summary>
    public static string ResolveSource(string? source, IReadOnlyList<string> knownSources)
    {
        var s = (source ?? string.Empty).Trim().Trim('[', ']').Trim();
        if (s.StartsWith("Quelle:", StringComparison.OrdinalIgnoreCase))
            s = s["Quelle:".Length..].Trim();

        if (s.Length > 0)
        {
            var exact = knownSources.FirstOrDefault(k => string.Equals(k, s, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var byName = knownSources.FirstOrDefault(k => string.Equals(Path.GetFileNameWithoutExtension(k), Path.GetFileNameWithoutExtension(s), StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;

            var contained = knownSources.FirstOrDefault(k => s.Contains(Path.GetFileNameWithoutExtension(k), StringComparison.OrdinalIgnoreCase));
            if (contained != null) return contained;
        }

        return knownSources.Count == 1 ? knownSources[0] : string.Empty;
    }

    // ---------------- Wiederholungen ----------------

    /// <summary>
    /// Schlüssel einer Zeile für den Vergleich auf Wiederholungen: die erste Spalte, bei der Zeitleiste Datum und Ereignis
    /// (nur Buchstaben und Ziffern, ohne Groß-/Kleinschreibung). Ist die erste Spalte leer, zählen alle Zellen.
    /// </summary>
    public static string RowKey(DataTableDocument table, IReadOnlyList<string> cells)
    {
        if (cells.Count == 0)
            return string.Empty;

        var keyCells = table.Template == DataTableTemplate.Timeline ? cells.Take(2) : cells.Take(1);
        var key = Normalize(string.Join(" ", keyCells));
        return key.Length > 0 ? key : Normalize(string.Join(" ", cells));
    }

    private static string RowLabel(DataTableDocument table, IReadOnlyList<string> cells)
    {
        var keyCells = table.Template == DataTableTemplate.Timeline ? cells.Take(2) : cells.Take(1);
        var label = string.Join(" – ", keyCells.Where(c => !string.IsNullOrWhiteSpace(c)));
        return label.Length > 120 ? label[..119] + "…" : label;
    }

    /// <summary>Entfernt Zeilen, deren Schlüssel schon vorhanden ist (in <paramref name="existing"/> oder weiter oben).</summary>
    public static List<DataTableRow> RemoveDuplicates(DataTableDocument table, IEnumerable<DataTableRow> rows, IEnumerable<DataTableRow> existing, out int duplicates)
    {
        var seen = new HashSet<string>(existing.Select(r => RowKey(table, r.Cells)).Where(k => k.Length > 0));
        var result = new List<DataTableRow>();
        duplicates = 0;

        foreach (var row in rows)
        {
            var key = RowKey(table, row.Cells);
            if (key.Length == 0 || !seen.Add(key))
            {
                duplicates++;
                continue;
            }

            result.Add(row);
        }

        return result;
    }

    // ---------------- Hilfen ----------------

    /// <summary>Spaltennamen bereinigen: kürzen, leere benennen, doppelte durchnummerieren, höchstens <see cref="DataTableDocument.MaxColumns"/>.</summary>
    public static List<string> NormalizeColumnNames(IEnumerable<string>? names)
    {
        var result = new List<string>();
        foreach (var raw in names ?? Enumerable.Empty<string>())
        {
            var name = Clean(raw, MaxColumnNameLength);
            if (name.Length == 0)
                name = $"Spalte {result.Count + 1}";

            var unique = name;
            for (var n = 2; result.Contains(unique, StringComparer.OrdinalIgnoreCase); n++)
                unique = $"{name} {n}";

            result.Add(unique);
            if (result.Count >= DataTableDocument.MaxColumns)
                break;
        }

        return result;
    }

    public static string Normalize(string? text) =>
        new((text ?? string.Empty).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var t = text.Replace("\r", string.Empty).Trim();
        return t.Length <= maxLength ? t : t[..(maxLength - 1)].TrimEnd() + "…";
    }

    // ---------------- Speicherung ----------------

    public async Task<DataTableLibrary> LoadLibraryAsync(string profileName)
    {
        var path = GetFilePath(profileName);
        if (!File.Exists(path))
            return new DataTableLibrary();

        try
        {
            await using var stream = File.OpenRead(path);
            var library = await JsonSerializer.DeserializeAsync<DataTableLibrary>(stream) ?? new DataTableLibrary();
            library.Tables ??= new List<DataTableDocument>();
            library.Tables.RemoveAll(t => t == null);

            foreach (var table in library.Tables)
            {
                table.Columns ??= new List<string>();
                table.Rows ??= new List<DataTableRow>();
                table.SourceDocuments ??= new List<string>();
                table.Rows.RemoveAll(r => r == null);
                foreach (var row in table.Rows)
                {
                    row.Cells ??= new List<string>();
                    row.Source ??= string.Empty;
                    for (var i = 0; i < row.Cells.Count; i++)
                        row.Cells[i] ??= string.Empty;

                    // Zellen an die Spaltenzahl anpassen (z. B. nach einer von Hand bearbeiteten Datei)
                    while (row.Cells.Count < table.Columns.Count) row.Cells.Add(string.Empty);
                    if (row.Cells.Count > table.Columns.Count) row.Cells.RemoveRange(table.Columns.Count, row.Cells.Count - table.Columns.Count);
                }
            }

            return library;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new DataTableLibrary();
        }
    }

    public async Task SaveLibraryAsync(string profileName, DataTableLibrary library)
    {
        var path = GetFilePath(profileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, library, new JsonSerializerOptions { WriteIndented = true });
        }

        File.Move(temp, path, overwrite: true);
    }

    private string GetFilePath(string profileName) =>
        Path.Combine(_baseDirectory, "DataTables", $"{JsonStorageService.ToSafeProfileFileName(profileName)}.json");

    // ---------------- Exporte ----------------

    /// <summary>Trennzeichen nach dem regionalen Format des Benutzers (vor dem Umschalten auf die Exportsprache).</summary>
    public byte[] ExportToCsv(DataTableDocument table, IReadOnlyList<DataTableRow> rows, CultureInfo culture) =>
        DataTableExporter.ToCsvBytes(table, rows, culture, DataTableExporter.CsvSeparator(CultureInfo.CurrentCulture));

    public async Task ExportToXlsxAsync(DataTableDocument table, IReadOnlyList<DataTableRow> rows, string filePath, CultureInfo culture)
    {
        using var buffer = new MemoryStream();
        XlsxWriter.Write(table, rows, culture, buffer);
        buffer.Position = 0;

        await using var file = File.Create(filePath);
        await buffer.CopyToAsync(file);
    }

    public string ExportToMarkdown(DataTableDocument table, IReadOnlyList<DataTableRow> rows, CultureInfo culture) =>
        DataTableExporter.ToMarkdown(table, rows, culture);
}
