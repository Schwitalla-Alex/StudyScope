using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using StudyScope.Core.Common;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Schreibt eine Datentabelle als Excel-Arbeitsmappe (.xlsx, Office Open XML) ohne Zusatzbibliothek:
/// ein Tabellenblatt mit fetter, fixierter Kopfzeile, Autofilter, geschätzten Spaltenbreiten und Zeilenumbruch.
/// Texte stehen als Inline-Strings in den Zellen (keine sharedStrings nötig), eindeutige Zahlen als Zahlenzellen.
/// Kopf der Quellspalte, Blattname ohne Titel und <c>dc:language</c> folgen der übergebenen Sprache.
/// Prüfbar mit openpyxl bzw. LibreOffice Calc.
/// </summary>
public static class XlsxWriter
{
    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string CtBase = "application/vnd.openxmlformats-officedocument.spreadsheetml";

    /// <summary>Excel erlaubt höchstens 32 767 Zeichen je Zelle.</summary>
    private const int MaxCellChars = 32767;

    public static void Write(DataTableDocument table, IReadOnlyList<DataTableRow> rows, CultureInfo culture, Stream output)
    {
        using var scope = CultureScope.Use(culture);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        Add(zip, "[Content_Types].xml",
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            $"<Override PartName=\"/xl/workbook.xml\" ContentType=\"{CtBase}.sheet.main+xml\"/>" +
            $"<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"{CtBase}.worksheet+xml\"/>" +
            $"<Override PartName=\"/xl/styles.xml\" ContentType=\"{CtBase}.styles+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
            "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
            "</Types>");

        Add(zip, "_rels/.rels", Relationships(
            ("rId1", $"{RelBase}/officeDocument", "xl/workbook.xml"),
            ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
            ("rId3", $"{RelBase}/extended-properties", "docProps/app.xml")));

        Add(zip, "xl/_rels/workbook.xml.rels", Relationships(
            ("rId1", $"{RelBase}/worksheet", "worksheets/sheet1.xml"),
            ("rId2", $"{RelBase}/styles", "styles.xml")));

        var created = table.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        Add(zip, "docProps/core.xml",
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            $"<dc:title>{Esc(table.Title)}</dc:title><dc:creator>StudyScope</dc:creator><dc:language>{CultureScope.SpecificLanguageTag(culture)}</dc:language>" +
            $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{created}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{created}</dcterms:modified>" +
            "</cp:coreProperties>");
        Add(zip, "docProps/app.xml",
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>StudyScope</Application></Properties>");

        var sheetName = SheetName(table.Title);
        var header = DataTableExporter.Header(table);
        var lastRef = $"{ColumnLetter(header.Count - 1)}{rows.Count + 1}";

        Add(zip, "xl/workbook.xml",
            $"<workbook xmlns=\"{NsMain}\" xmlns:r=\"{NsR}\">" +
            $"<sheets><sheet name=\"{Esc(sheetName)}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            $"<definedNames><definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"0\" hidden=\"1\">{Esc(QuoteSheet(sheetName))}!$A$1:${ColumnLetter(header.Count - 1)}${rows.Count + 1}</definedName></definedNames>" +
            "</workbook>");

        Add(zip, "xl/styles.xml", Styles());
        Add(zip, "xl/worksheets/sheet1.xml", Sheet(table, rows, header, lastRef));
    }

    // ---------------- Tabellenblatt ----------------

    private static string Sheet(DataTableDocument table, IReadOnlyList<DataTableRow> rows, IReadOnlyList<string> header, string lastRef)
    {
        var sb = new StringBuilder();
        sb.Append($"<worksheet xmlns=\"{NsMain}\" xmlns:r=\"{NsR}\">");

        // Kopfzeile fixieren
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>" +
                  "<selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/></sheetView></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");

        // Spaltenbreiten aus dem längsten Inhalt geschätzt (8 bis 60 Zeichen)
        sb.Append("<cols>");
        var allRows = rows.Select(r => DataTableExporter.Values(table, r)).ToList();
        for (var c = 0; c < header.Count; c++)
        {
            var longest = allRows.Select(v => LongestLine(v[c])).Append(header[c].Length + 2).Max();
            var width = Math.Clamp(longest + 2, 8, 60);
            sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        }
        sb.Append("</cols>");

        sb.Append("<sheetData>");
        AppendRow(sb, 1, header, headerStyle: true);
        for (var r = 0; r < allRows.Count; r++)
            AppendRow(sb, r + 2, allRows[r], headerStyle: false);
        sb.Append("</sheetData>");

        sb.Append($"<autoFilter ref=\"A1:{lastRef}\"/>");
        sb.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("<pageSetup orientation=\"landscape\" paperSize=\"9\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, int rowNumber, IReadOnlyList<string> values, bool headerStyle)
    {
        sb.Append($"<row r=\"{rowNumber}\">");
        for (var c = 0; c < values.Count; c++)
        {
            var cellRef = $"{ColumnLetter(c)}{rowNumber}";
            var value = values[c] ?? string.Empty;
            var style = headerStyle ? 1 : 2;

            if (!headerStyle && DataTableValues.TryParseStrictNumber(value, out var number))
            {
                sb.Append($"<c r=\"{cellRef}\" s=\"3\"><v>{number.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
            }
            else if (value.Length > 0)
            {
                var text = value.Length > MaxCellChars ? value[..MaxCellChars] : value;
                sb.Append($"<c r=\"{cellRef}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(text)}</t></is></c>");
            }
        }
        sb.Append("</row>");
    }

    private static string Styles() =>
        $"<styleSheet xmlns=\"{NsMain}\">" +
        "<fonts count=\"2\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" +
        "</fonts>" +
        "<fills count=\"3\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF007BFF\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "</fills>" +
        "<borders count=\"2\">" +
        "<border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left style=\"thin\"><color rgb=\"FFDEE2E6\"/></left><right style=\"thin\"><color rgb=\"FFDEE2E6\"/></right>" +
        "<top style=\"thin\"><color rgb=\"FFDEE2E6\"/></top><bottom style=\"thin\"><color rgb=\"FFDEE2E6\"/></bottom><diagonal/></border>" +
        "</borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"4\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"top\"/></xf>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    // ---------------- Hilfen ----------------

    /// <summary>Spaltenbuchstaben: 0 → A, 25 → Z, 26 → AA.</summary>
    public static string ColumnLetter(int index)
    {
        var result = string.Empty;
        var n = index + 1;
        while (n > 0)
        {
            var rem = (n - 1) % 26;
            result = (char)('A' + rem) + result;
            n = (n - 1) / 26;
        }
        return result;
    }

    /// <summary>Gültiger Blattname: höchstens 31 Zeichen, ohne <c>[]:*?/\</c>, nicht leer.</summary>
    public static string SheetName(string? title)
    {
        var name = new string((title ?? string.Empty).Where(c => "[]:*?/\\".IndexOf(c) < 0 && !char.IsControl(c)).ToArray()).Trim().Trim('\'');
        if (name.Length == 0) name = ExportStrings.Xlsx_DefaultSheetName;
        return name.Length > 31 ? name[..31].TrimEnd() : name;
    }

    private static string QuoteSheet(string name) => "'" + name.Replace("'", "''") + "'";

    private static int LongestLine(string text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Max(l => l.Length);

    private static string Relationships(params (string Id, string Type, string Target)[] rels)
    {
        var sb = new StringBuilder("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var (id, type, target) in rels)
            sb.Append($"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"{target}\"/>");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        writer.Write(xml);
    }

    /// <summary>XML-Maskierung; Steuerzeichen (außer Tab/Zeilenumbruch), die XML nicht erlaubt, werden entfernt.</summary>
    private static string Esc(string? text)
    {
        var cleaned = new string((text ?? string.Empty).Where(c => c == '\t' || c == '\n' || c == '\r' || !char.IsControl(c)).ToArray());
        return SecurityElement.Escape(cleaned) ?? string.Empty;
    }
}
