using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Schreibt einen Bericht als Word-Dokument (.docx, Office Open XML) ohne Zusatzbibliothek.
/// Nutzt die Word-Formatvorlagen „Titel“, „Überschrift 1–3“ und „Aufzählung“, damit Inhaltsverzeichnis
/// und Navigationsbereich in Word funktionieren. Begriffe und Karteikarten stehen in Tabellen.
/// Geprüft mit python-docx und LibreOffice Writer.
/// </summary>
public static class DocxWriter
{
    private const string NsW = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string CtBase = "application/vnd.openxmlformats-officedocument.wordprocessingml";

    private const string Font = "Calibri";
    private const string ColorAccent = "007BFF";
    private const string ColorText = "212529";
    private const string ColorMuted = "6C757D";

    // A4 in Twips, Ränder 2,5 cm
    private const int PageWidth = 11906;
    private const int PageHeight = 16838;
    private const int Margin = 1417;
    private const int TextWidth = PageWidth - 2 * Margin;

    public static void Write(Report report, ReportStudioContent studio, Stream output)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        Add(zip, "[Content_Types].xml",
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            $"<Override PartName=\"/word/document.xml\" ContentType=\"{CtBase}.document.main+xml\"/>" +
            $"<Override PartName=\"/word/styles.xml\" ContentType=\"{CtBase}.styles+xml\"/>" +
            $"<Override PartName=\"/word/numbering.xml\" ContentType=\"{CtBase}.numbering+xml\"/>" +
            $"<Override PartName=\"/word/settings.xml\" ContentType=\"{CtBase}.settings+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
            "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
            "</Types>");

        Add(zip, "_rels/.rels", Relationships(
            ("rId1", $"{RelBase}/officeDocument", "word/document.xml"),
            ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
            ("rId3", $"{RelBase}/extended-properties", "docProps/app.xml")));

        Add(zip, "word/_rels/document.xml.rels", Relationships(
            ("rId1", $"{RelBase}/styles", "styles.xml"),
            ("rId2", $"{RelBase}/numbering", "numbering.xml"),
            ("rId3", $"{RelBase}/settings", "settings.xml")));

        var created = report.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        Add(zip, "docProps/core.xml",
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            $"<dc:title>{Esc(report.Title)}</dc:title><dc:creator>StudyScope</dc:creator>" +
            $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{created}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{created}</dcterms:modified>" +
            "</cp:coreProperties>");
        Add(zip, "docProps/app.xml",
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>StudyScope</Application></Properties>");

        Add(zip, "word/settings.xml", $"<w:settings xmlns:w=\"{NsW}\"><w:defaultTabStop w:val=\"708\"/><w:characterSpacingControl w:val=\"doNotCompress\"/></w:settings>");
        Add(zip, "word/styles.xml", Styles());
        Add(zip, "word/numbering.xml", Numbering());
        Add(zip, "word/document.xml", Document(report, studio));
    }

    // ---------------- Inhalt ----------------

    private static string Document(Report report, ReportStudioContent studio)
    {
        var body = new StringBuilder();

        body.Append(Paragraph("Title", Run(report.Title)));
        body.Append(Paragraph("Subtitle", Run(ReportTextExporter.MetaLine(report))));
        if (!string.IsNullOrWhiteSpace(report.Summary))
            body.Append(Paragraph(null, Text(report.Summary)));

        foreach (var section in report.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading))
                body.Append(Paragraph("Heading1", Run(section.Heading)));

            foreach (var paragraph in section.Paragraphs)
                body.Append(Paragraph(null, Text(paragraph)));

            foreach (var bullet in section.Bullets)
                body.Append(Bullet(bullet, 0));

            if (section.Terms.Count > 0)
            {
                body.Append(Paragraph("Heading3", Run("Begriffe")));
                body.Append(Table(new[] { "Begriff", "Erklärung" }, section.Terms.Select(t => new[] { t.Term, t.Definition }), new[] { 0.3, 0.7 }));
            }

            foreach (var q in section.Questions)
            {
                body.Append(Paragraph("Question", Run(q.Question, bold: true)));
                body.Append(Paragraph(null, Text(q.Answer)));
            }

            if (!string.IsNullOrWhiteSpace(section.Takeaway))
                body.Append(Paragraph("Takeaway", Run("Merksatz: ", bold: true) + Text(section.Takeaway)));
        }

        AppendStudio(body, studio);

        body.Append($"<w:sectPr><w:pgSz w:w=\"{PageWidth}\" w:h=\"{PageHeight}\"/>" +
                    $"<w:pgMar w:top=\"{Margin}\" w:right=\"{Margin}\" w:bottom=\"{Margin}\" w:left=\"{Margin}\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr>");

        return $"<w:document xmlns:w=\"{NsW}\" xmlns:r=\"{NsR}\"><w:body>{body}</w:body></w:document>";
    }

    private static void AppendStudio(StringBuilder body, ReportStudioContent studio)
    {
        if (!studio.HasAny)
            return;

        // Studio-Inhalte als Anhang auf einer neuen Seite
        body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");

        if (studio.Mindmap != null)
        {
            body.Append(Paragraph("Heading1", Run("Mindmap")));
            AppendTree(body, studio.Mindmap.Root, 0);
        }

        if (studio.Flashcards.Count > 0)
        {
            body.Append(Paragraph("Heading1", Run($"Karteikarten ({studio.Flashcards.Count})")));
            body.Append(Table(new[] { "Frage", "Antwort" }, studio.Flashcards.Select(c => new[] { c.Question, c.Answer }), new[] { 0.45, 0.55 }));
        }

        if (studio.QuizQuestions.Count > 0)
        {
            body.Append(Paragraph("Heading1", Run("Selbsttest")));
            for (var i = 0; i < studio.QuizQuestions.Count; i++)
            {
                var q = studio.QuizQuestions[i];
                body.Append(Paragraph("Question", Run($"{i + 1}. {q.Text}", bold: true) + Run(ReportTextExporter.TypeHint(q), color: ColorMuted)));
                for (var o = 0; o < q.Options.Count; o++)
                    body.Append(Paragraph("Option", Run($"{ReportTextExporter.Letter(o)})  {q.Options[o]}")));
            }

            body.Append(Paragraph("Heading3", Run("Lösungen")));
            body.Append(Paragraph(null, Run(string.Join("   ", studio.QuizQuestions.Select((q, i) => $"{i + 1}. {ReportTextExporter.Solution(q)}")))));
        }

        if (studio.Presentation is { Slides.Count: > 0 } presentation)
        {
            body.Append(Paragraph("Heading1", Run($"Präsentation – {presentation.Title}")));
            for (var i = 0; i < presentation.Slides.Count; i++)
            {
                var slide = presentation.Slides[i];
                body.Append(Paragraph("Heading3", Run($"Folie {i + 1}: {slide.Title}")));
                if (!string.IsNullOrWhiteSpace(slide.Subtitle))
                    body.Append(Paragraph(null, Run(slide.Subtitle, color: ColorMuted)));
                foreach (var bullet in slide.Bullets)
                    body.Append(Bullet(bullet, 0));
            }
        }

        var tables = studio.DataTables.Where(t => t.Rows.Count > 0).ToList();
        if (tables.Count > 0)
        {
            body.Append(Paragraph("Heading1", Run(tables.Count == 1 ? "Datentabelle" : "Datentabellen")));
            foreach (var embedded in tables)
            {
                body.Append(Paragraph("Heading2", Run(embedded.Table.Title)));
                body.Append(Paragraph(null, Run(ReportTextExporter.DataTableMeta(embedded), color: ColorMuted)));
                var header = DataTableExporter.Header(embedded.Table).ToArray();
                body.Append(Table(header, embedded.Rows.Select(r => DataTableExporter.Values(embedded.Table, r).ToArray()),
                    DataTableColumnWidths(embedded), firstColumnBold: false, compact: header.Length > 4));
            }
        }

        foreach (var audio in studio.AudioScripts.Where(a => a.Segments.Count > 0))
        {
            body.Append(Paragraph("Heading1", Run($"Audio-Skript – {audio.Title}")));
            body.Append(Paragraph(null, Run(ReportTextExporter.AudioMeta(audio), color: ColorMuted)));
            var speakers = audio.Speakers.Count > 1;
            foreach (var segment in audio.Segments)
            {
                body.Append(speakers
                    ? Paragraph(null, Run($"{segment.Speaker}: ", bold: true) + Run(segment.Text))
                    : Paragraph(null, Run(segment.Text)));
            }
        }

        foreach (var video in studio.VideoScripts.Where(ReportVideoScripts.HasNarration))
        {
            body.Append(Paragraph("Heading1", Run($"Video-Skript – {video.Title}")));
            body.Append(Paragraph(null, Run(ReportTextExporter.VideoMeta(video), color: ColorMuted)));
            for (var i = 0; i < video.Scenes.Count; i++)
            {
                if (!video.Scenes[i].HasNarration) continue;
                body.Append(Paragraph(null, Run(ReportVideoScripts.SceneHeading(video, i), bold: true)));
                body.Append(Paragraph(null, Run(video.Scenes[i].Narration.Trim())));
            }
        }
    }

    /// <summary>
    /// Spaltenbreiten (Anteile) einer Datentabelle: Jede Spalte bekommt zuerst so viel Platz, dass ihr längstes Wort
    /// (höchstens 20 Zeichen, auch in der Überschrift) nicht umbricht; der übrige Platz wird nach der mittleren Textlänge
    /// verteilt. Reicht die Breite nicht einmal dafür, wird im Verhältnis der Mindestbreiten verkleinert.
    /// </summary>
    public static double[] DataTableColumnWidths(ReportDataTable embedded)
    {
        var header = DataTableExporter.Header(embedded.Table);
        var rows = embedded.Rows.Select(r => DataTableExporter.Values(embedded.Table, r)).ToList();
        var charWidth = header.Count > 4 ? 100.0 : 115.0; // Twips je Zeichen (kompakte Schrift ab 5 Spalten)
        const double cellPadding = 260;                   // Zellenränder links/rechts plus Luft

        static int LongestWord(string text) =>
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length).DefaultIfEmpty(0).Max();

        var minimum = header.Select((h, i) =>
            Math.Min(20, Math.Max(LongestWord(h), rows.Select(v => LongestWord(v[i])).DefaultIfEmpty(0).Max())) * charWidth + cellPadding).ToArray();
        var wanted = header.Select((h, i) =>
        {
            var average = rows.Count == 0 ? h.Length : rows.Average(v => Math.Min(v[i].Length, 200));
            return Math.Max(minimum[i], Math.Min(average * charWidth + cellPadding, TextWidth * 0.5));
        }).ToArray();

        double[] widths;
        var minSum = minimum.Sum();
        if (minSum >= TextWidth)
        {
            widths = minimum;
        }
        else
        {
            var growth = wanted.Select((w, i) => w - minimum[i]).ToArray();
            var growthSum = growth.Sum();
            var extra = TextWidth - minSum;
            widths = minimum.Select((m, i) => m + (growthSum > 0 ? extra * growth[i] / growthSum : extra / minimum.Length)).ToArray();
        }

        var sum = widths.Sum();
        return widths.Select(w => w / sum).ToArray();
    }

    private static void AppendTree(StringBuilder body, MindmapNode node, int level)
    {
        var content = Run(node.Title, bold: level <= 1);
        if (!string.IsNullOrWhiteSpace(node.Summary))
            content += Run(" – " + node.Summary, color: ColorMuted);

        body.Append(BulletRaw(content, Math.Min(level, 8)));
        foreach (var child in node.Children)
            AppendTree(body, child, level + 1);
    }

    // ---------------- Bausteine ----------------

    private static string Paragraph(string? style, string runs) =>
        $"<w:p>{(style == null ? string.Empty : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")}{runs}</w:p>";

    private static string Bullet(string text, int level) => BulletRaw(Text(text), level);

    private static string BulletRaw(string runs, int level) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"ListBullet\"/><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>{runs}</w:p>";

    private static string Run(string text, bool bold = false, string? color = null, bool small = false)
    {
        // Reihenfolge in w:rPr nach Schema: b, color, sz, szCs
        var props = (bold ? "<w:b/>" : string.Empty) + (color != null ? $"<w:color w:val=\"{color}\"/>" : string.Empty)
                    + (small ? "<w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/>" : string.Empty);
        return $"<w:r>{(props.Length > 0 ? $"<w:rPr>{props}</w:rPr>" : string.Empty)}<w:t xml:space=\"preserve\">{Esc(text)}</w:t></w:r>";
    }

    /// <summary>Text mit Zeilenumbrüchen als mehrere Läufe mit &lt;w:br/&gt;.</summary>
    private static string Text(string text, bool small = false)
    {
        var lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
        return string.Join("<w:r><w:br/></w:r>", lines.Select(l => Run(l, small: small)));
    }

    private static string Table(string[] headers, IEnumerable<string[]> rows, double[] widths, bool firstColumnBold = true, bool compact = false)
    {
        var columns = widths.Select(w => (int)(TextWidth * w)).ToArray();
        var sb = new StringBuilder();
        sb.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"StudyScopeTable\"/>");
        sb.Append($"<w:tblW w:w=\"{TextWidth}\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>");
        sb.Append("<w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"0\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr>");
        sb.Append("<w:tblGrid>");
        foreach (var width in columns)
            sb.Append($"<w:gridCol w:w=\"{width}\"/>");
        sb.Append("</w:tblGrid>");

        sb.Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>");
        for (var i = 0; i < headers.Length; i++)
            sb.Append(Cell(Run(headers[i], bold: true, color: "FFFFFF", small: compact), columns[i], "343A40"));
        sb.Append("</w:tr>");

        foreach (var row in rows)
        {
            sb.Append("<w:tr><w:trPr><w:cantSplit/></w:trPr>");
            for (var i = 0; i < headers.Length; i++)
            {
                var value = i < row.Length ? row[i] : string.Empty;
                sb.Append(Cell(i == 0 && firstColumnBold ? Run(value, bold: true, small: compact) : Text(value, compact), columns[i], null));
            }
            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl>");
        sb.Append("<w:p/>"); // Abstand nach der Tabelle (Word verlangt ohnehin einen Absatz nach einer Tabelle am Ende)
        return sb.ToString();
    }

    private static string Cell(string runs, int width, string? fill) =>
        $"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/>" +
        (fill != null ? $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/>" : string.Empty) +
        $"</w:tcPr><w:p><w:pPr><w:spacing w:before=\"40\" w:after=\"40\"/></w:pPr>{runs}</w:p></w:tc>";

    // ---------------- Formatvorlagen ----------------

    private static string Styles() =>
        $"<w:styles xmlns:w=\"{NsW}\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr>" +
        $"<w:rFonts w:ascii=\"{Font}\" w:hAnsi=\"{Font}\" w:eastAsia=\"{Font}\" w:cs=\"{Font}\"/><w:color w:val=\"{ColorText}\"/>" +
        "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/><w:lang w:val=\"de-DE\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
        Style("Title", "Title", "<w:spacing w:after=\"60\"/>", "<w:b/><w:sz w:val=\"48\"/><w:szCs w:val=\"48\"/>", outline: null) +
        Style("Subtitle", "Subtitle", "<w:pBdr><w:bottom w:val=\"single\" w:sz=\"12\" w:space=\"6\" w:color=\"" + ColorAccent + "\"/></w:pBdr><w:spacing w:after=\"240\"/>",
            $"<w:color w:val=\"{ColorMuted}\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/>", outline: null) +
        Style("Heading1", "heading 1", "<w:keepNext/><w:spacing w:before=\"360\" w:after=\"120\"/>",
            $"<w:b/><w:color w:val=\"{ColorAccent}\"/><w:sz w:val=\"32\"/><w:szCs w:val=\"32\"/>", outline: 0) +
        Style("Heading2", "heading 2", "<w:keepNext/><w:spacing w:before=\"240\" w:after=\"80\"/>",
            "<w:b/><w:sz w:val=\"26\"/><w:szCs w:val=\"26\"/>", outline: 1) +
        Style("Heading3", "heading 3", "<w:keepNext/><w:spacing w:before=\"200\" w:after=\"60\"/>",
            "<w:b/><w:sz w:val=\"23\"/><w:szCs w:val=\"23\"/>", outline: 2) +
        Style("ListBullet", "List Bullet", "<w:spacing w:after=\"60\"/>", string.Empty, outline: null) +
        Style("Question", "StudyScope Frage", "<w:keepNext/><w:spacing w:before=\"160\" w:after=\"40\"/>", string.Empty, outline: null) +
        Style("Option", "StudyScope Antwortmöglichkeit", "<w:spacing w:after=\"20\"/><w:ind w:left=\"567\"/>", string.Empty, outline: null) +
        Style("Takeaway", "StudyScope Merksatz",
            "<w:pBdr><w:left w:val=\"single\" w:sz=\"24\" w:space=\"8\" w:color=\"" + ColorAccent + "\"/></w:pBdr>" +
            "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EAF3FF\"/><w:spacing w:before=\"120\" w:after=\"200\"/><w:ind w:left=\"170\" w:right=\"170\"/>",
            string.Empty, outline: null) +
        "<w:style w:type=\"table\" w:styleId=\"StudyScopeTable\"><w:name w:val=\"StudyScope Tabelle\"/><w:tblPr>" +
        "<w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:color=\"DEE2E6\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"DEE2E6\"/>" +
        "<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"DEE2E6\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"DEE2E6\"/>" +
        "<w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"DEE2E6\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"DEE2E6\"/></w:tblBorders>" +
        "<w:tblCellMar><w:left w:w=\"100\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>" +
        "</w:styles>";

    private static string Style(string id, string name, string paragraphProps, string runProps, int? outline) =>
        $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/>" +
        $"<w:pPr>{paragraphProps}{(outline != null ? $"<w:outlineLvl w:val=\"{outline}\"/>" : string.Empty)}</w:pPr>" +
        (runProps.Length > 0 ? $"<w:rPr>{runProps}</w:rPr>" : string.Empty) + "</w:style>";

    private static string Numbering()
    {
        var symbols = new[] { "•", "◦", "▪" };
        var levels = new StringBuilder();
        for (var level = 0; level < 9; level++)
        {
            var indent = 360 + level * 360;
            levels.Append($"<w:lvl w:ilvl=\"{level}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"{symbols[level % 3]}\"/><w:lvlJc w:val=\"left\"/>" +
                          $"<w:pPr><w:ind w:left=\"{indent}\" w:hanging=\"283\"/></w:pPr>" +
                          $"<w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:hint=\"default\"/><w:color w:val=\"{ColorAccent}\"/></w:rPr></w:lvl>");
        }

        return $"<w:numbering xmlns:w=\"{NsW}\"><w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"hybridMultilevel\"/>{levels}</w:abstractNum>" +
               "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num></w:numbering>";
    }

    // ---------------- Hilfen ----------------

    private static string Relationships(params (string Id, string Type, string Target)[] relations)
    {
        var sb = new StringBuilder("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var (id, type, target) in relations)
            sb.Append($"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"{target}\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
        writer.Write(xml);
    }

    /// <summary>Maskiert XML-Sonderzeichen und entfernt unzulässige Steuerzeichen (Emoji bleiben erhalten).</summary>
    private static string Esc(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var valid = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                valid.Append(c).Append(text[++i]);
            else if (XmlConvert.IsXmlChar(c))
                valid.Append(c);
        }

        return SecurityElement.Escape(valid.ToString()) ?? string.Empty;
    }
}
