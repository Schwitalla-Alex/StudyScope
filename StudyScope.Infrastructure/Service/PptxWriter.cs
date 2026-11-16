using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Schreibt eine Präsentation als PowerPoint-Datei (.pptx, Office Open XML) ohne Zusatzbibliothek.
/// Aufbau: ein Folienmaster mit leerem Layout, je Folie Textfelder mit fester Formatierung (keine Platzhalter),
/// Sprechernotizen über einen Notizenmaster. Format 16:9 (33,867 × 19,05 cm).
/// Geprüft mit python-pptx und LibreOffice Impress; Struktur entspricht dem Mindestumfang, den PowerPoint erwartet.
/// </summary>
public static class PptxWriter
{
    // Abmessungen in EMU (1 cm = 360000 EMU)
    private const long SlideWidth = 12192000;
    private const long SlideHeight = 6858000;
    private const long Margin = 685800;

    // Farben passend zu StudyScope
    private const string ColorDark = "343A40";
    private const string ColorText = "212529";
    private const string ColorMuted = "6C757D";
    private const string ColorAccent = "007BFF";
    private const string ColorLight = "CED4DA";
    private const string FontName = "Segoe UI";

    private const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsP = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string Namespaces = $"xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\" xmlns:p=\"{NsP}\"";

    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string CtBase = "application/vnd.openxmlformats-officedocument.presentationml";

    public static void Write(PresentationDocument presentation, Stream output)
    {
        var slides = presentation.Slides.Count > 0
            ? presentation.Slides
            : new List<PresentationSlide> { new() { Kind = SlideKind.Title, Title = presentation.Title } };

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        Add(zip, "[Content_Types].xml", ContentTypes(slides.Count));
        Add(zip, "_rels/.rels", Relationships(
            ("rId1", $"{RelBase}/officeDocument", "ppt/presentation.xml"),
            ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
            ("rId3", $"{RelBase}/extended-properties", "docProps/app.xml")));

        Add(zip, "docProps/core.xml", CoreProperties(presentation));
        Add(zip, "docProps/app.xml", AppProperties(slides.Count));

        Add(zip, "ppt/presentation.xml", PresentationPart(slides.Count));
        var presentationRels = new List<(string, string, string)>
        {
            ("rId1", $"{RelBase}/slideMaster", "slideMasters/slideMaster1.xml"),
            ("rId2", $"{RelBase}/notesMaster", "notesMasters/notesMaster1.xml"),
            ("rId3", $"{RelBase}/theme", "theme/theme1.xml"),
            ("rId4", $"{RelBase}/presProps", "presProps.xml"),
            ("rId5", $"{RelBase}/viewProps", "viewProps.xml"),
            ("rId6", $"{RelBase}/tableStyles", "tableStyles.xml")
        };
        for (var i = 1; i <= slides.Count; i++)
            presentationRels.Add(($"rId{9 + i}", $"{RelBase}/slide", $"slides/slide{i}.xml"));
        Add(zip, "ppt/_rels/presentation.xml.rels", Relationships(presentationRels.ToArray()));

        Add(zip, "ppt/presProps.xml", $"<p:presentationPr {Namespaces}/>");
        Add(zip, "ppt/viewProps.xml", $"<p:viewPr {Namespaces}><p:normalViewPr><p:restoredLeft sz=\"15620\"/><p:restoredTop sz=\"94660\"/></p:normalViewPr><p:gridSpacing cx=\"72008\" cy=\"72008\"/></p:viewPr>");
        Add(zip, "ppt/tableStyles.xml", $"<a:tblStyleLst xmlns:a=\"{NsA}\" def=\"{{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}}\"/>");

        Add(zip, "ppt/theme/theme1.xml", Theme());
        Add(zip, "ppt/theme/theme2.xml", Theme());

        Add(zip, "ppt/slideMasters/slideMaster1.xml", SlideMaster());
        Add(zip, "ppt/slideMasters/_rels/slideMaster1.xml.rels", Relationships(
            ("rId1", $"{RelBase}/slideLayout", "../slideLayouts/slideLayout1.xml"),
            ("rId2", $"{RelBase}/theme", "../theme/theme1.xml")));

        Add(zip, "ppt/slideLayouts/slideLayout1.xml", SlideLayout());
        Add(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", Relationships(
            ("rId1", $"{RelBase}/slideMaster", "../slideMasters/slideMaster1.xml")));

        Add(zip, "ppt/notesMasters/notesMaster1.xml", NotesMaster());
        Add(zip, "ppt/notesMasters/_rels/notesMaster1.xml.rels", Relationships(
            ("rId1", $"{RelBase}/theme", "../theme/theme2.xml")));

        for (var i = 0; i < slides.Count; i++)
        {
            var number = i + 1;
            Add(zip, $"ppt/slides/slide{number}.xml", Slide(slides[i], number, slides.Count, presentation.Title));
            Add(zip, $"ppt/slides/_rels/slide{number}.xml.rels", Relationships(
                ("rId1", $"{RelBase}/slideLayout", "../slideLayouts/slideLayout1.xml"),
                ("rId2", $"{RelBase}/notesSlide", $"../notesSlides/notesSlide{number}.xml")));

            Add(zip, $"ppt/notesSlides/notesSlide{number}.xml", NotesSlide(slides[i].Notes));
            Add(zip, $"ppt/notesSlides/_rels/notesSlide{number}.xml.rels", Relationships(
                ("rId1", $"{RelBase}/notesMaster", "../notesMasters/notesMaster1.xml"),
                ("rId2", $"{RelBase}/slide", $"../slides/slide{number}.xml")));
        }
    }

    // ---------------- Paket-Teile ----------------

    private static string ContentTypes(int slideCount)
    {
        var sb = new StringBuilder();
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        Override(sb, "/ppt/presentation.xml", $"{CtBase}.presentation.main+xml");
        Override(sb, "/ppt/presProps.xml", $"{CtBase}.presProps+xml");
        Override(sb, "/ppt/viewProps.xml", $"{CtBase}.viewProps+xml");
        Override(sb, "/ppt/tableStyles.xml", $"{CtBase}.tableStyles+xml");
        Override(sb, "/ppt/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml");
        Override(sb, "/ppt/theme/theme2.xml", "application/vnd.openxmlformats-officedocument.theme+xml");
        Override(sb, "/ppt/slideMasters/slideMaster1.xml", $"{CtBase}.slideMaster+xml");
        Override(sb, "/ppt/slideLayouts/slideLayout1.xml", $"{CtBase}.slideLayout+xml");
        Override(sb, "/ppt/notesMasters/notesMaster1.xml", $"{CtBase}.notesMaster+xml");
        for (var i = 1; i <= slideCount; i++)
        {
            Override(sb, $"/ppt/slides/slide{i}.xml", $"{CtBase}.slide+xml");
            Override(sb, $"/ppt/notesSlides/notesSlide{i}.xml", $"{CtBase}.notesSlide+xml");
        }
        Override(sb, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
        Override(sb, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml");
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static void Override(StringBuilder sb, string part, string type) =>
        sb.Append($"<Override PartName=\"{part}\" ContentType=\"{type}\"/>");

    private static string Relationships(params (string Id, string Type, string Target)[] relations)
    {
        var sb = new StringBuilder("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var (id, type, target) in relations)
            sb.Append($"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"{target}\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    private static string CoreProperties(PresentationDocument presentation)
    {
        var created = presentation.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
               "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
               "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
               $"<dc:title>{Esc(presentation.Title)}</dc:title><dc:creator>StudyScope</dc:creator>" +
               $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{created}</dcterms:created>" +
               $"<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{created}</dcterms:modified>" +
               "</cp:coreProperties>";
    }

    private static string AppProperties(int slideCount) =>
        "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\">" +
        $"<Application>StudyScope</Application><Slides>{slideCount}</Slides><Notes>{slideCount}</Notes></Properties>";

    private static string PresentationPart(int slideCount)
    {
        var sb = new StringBuilder($"<p:presentation {Namespaces} saveSubsetFonts=\"1\">");
        sb.Append("<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>");
        sb.Append("<p:notesMasterIdLst><p:notesMasterId r:id=\"rId2\"/></p:notesMasterIdLst>");
        sb.Append("<p:sldIdLst>");
        for (var i = 1; i <= slideCount; i++)
            sb.Append($"<p:sldId id=\"{255 + i}\" r:id=\"rId{9 + i}\"/>");
        sb.Append("</p:sldIdLst>");
        sb.Append($"<p:sldSz cx=\"{SlideWidth}\" cy=\"{SlideHeight}\"/>");
        sb.Append("<p:notesSz cx=\"6858000\" cy=\"9144000\"/>");
        sb.Append("</p:presentation>");
        return sb.ToString();
    }

    private const string ClrMap =
        "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" " +
        "accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>";

    private const string GroupHeader =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
        "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    private static string SlideMaster() =>
        $"<p:sldMaster {Namespaces}>" +
        "<p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg>" +
        $"<p:spTree>{GroupHeader}</p:spTree></p:cSld>" +
        ClrMap +
        "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>" +
        "<p:txStyles>" +
        "<p:titleStyle><a:lvl1pPr><a:defRPr sz=\"3200\"/></a:lvl1pPr></p:titleStyle>" +
        "<p:bodyStyle><a:lvl1pPr><a:defRPr sz=\"2000\"/></a:lvl1pPr></p:bodyStyle>" +
        "<p:otherStyle><a:lvl1pPr><a:defRPr sz=\"1800\"/></a:lvl1pPr></p:otherStyle>" +
        "</p:txStyles></p:sldMaster>";

    private static string SlideLayout() =>
        $"<p:sldLayout {Namespaces} type=\"blank\" preserve=\"1\">" +
        $"<p:cSld name=\"Leer\"><p:spTree>{GroupHeader}</p:spTree></p:cSld>" +
        "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>";

    private static string NotesMaster() =>
        $"<p:notesMaster {Namespaces}>" +
        "<p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>" + GroupHeader +
        "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Folienbildplatzhalter\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr>" +
        "<p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr>" +
        "<p:spPr><a:xfrm><a:off x=\"381000\" y=\"685800\"/><a:ext cx=\"6096000\" cy=\"3429000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" +
        "<a:noFill/><a:ln w=\"12700\"><a:solidFill><a:prstClr val=\"black\"/></a:solidFill></a:ln></p:spPr></p:sp>" +
        "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notizenplatzhalter\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr>" +
        "<p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr>" +
        "<p:spPr><a:xfrm><a:off x=\"685800\" y=\"4343400\"/><a:ext cx=\"5486400\" cy=\"4114800\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>" +
        "<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"de-DE\"/></a:p></p:txBody></p:sp>" +
        "</p:spTree></p:cSld>" + ClrMap +
        "<p:notesStyle><a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\">" +
        "<a:defRPr sz=\"1200\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill>" +
        "<a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr></p:notesStyle>" +
        "</p:notesMaster>";

    private static string NotesSlide(string notes)
    {
        var paragraphs = string.IsNullOrWhiteSpace(notes)
            ? "<a:p><a:endParaRPr lang=\"de-DE\"/></a:p>"
            : string.Concat(notes.Replace("\r", "").Split('\n').Select(line =>
                $"<a:p><a:r><a:rPr lang=\"de-DE\" dirty=\"0\"/><a:t>{Esc(line)}</a:t></a:r></a:p>"));

        return $"<p:notes {Namespaces}><p:cSld><p:spTree>" + GroupHeader +
               "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Folienbildplatzhalter\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr>" +
               "<p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>" +
               "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notizenplatzhalter\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr>" +
               "<p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/>" +
               $"<p:txBody><a:bodyPr/><a:lstStyle/>{paragraphs}</p:txBody></p:sp>" +
               "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>";
    }

    // ---------------- Folien ----------------

    private static string Slide(PresentationSlide slide, int number, int total, string presentationTitle)
    {
        var isTitle = slide.Kind == SlideKind.Title;
        var shapes = new StringBuilder();
        var id = 2;

        if (isTitle)
        {
            shapes.Append(TextBox(id++, "Titel", Margin, 2057400, SlideWidth - 2 * Margin, 1600200, "b",
                Paragraph(slide.Title, FitTitleSize(slide.Title, 4400, 40), bold: true, color: "FFFFFF")));
            shapes.Append(Rectangle(id++, "Akzent", Margin, 3794760, 1371600, 54864, ColorAccent));
            if (!string.IsNullOrWhiteSpace(slide.Subtitle))
            {
                shapes.Append(TextBox(id++, "Untertitel", Margin, 3977640, SlideWidth - 2 * Margin, 1097280, "t",
                    Paragraph(slide.Subtitle, 2000, bold: false, color: ColorLight)));
            }
        }
        else
        {
            shapes.Append(TextBox(id++, "Titel", Margin, 457200, SlideWidth - 2 * Margin, 868680, "b",
                Paragraph(slide.Title, FitTitleSize(slide.Title, 3200, 55), bold: true, color: ColorText)));
            shapes.Append(Rectangle(id++, "Akzent", Margin, 1417320, 1097280, 45720, ColorAccent));

            var bodySize = FitBulletSize(slide.Bullets);
            var bullets = slide.Bullets.Count == 0
                ? "<a:p><a:endParaRPr lang=\"de-DE\"/></a:p>"
                : string.Concat(slide.Bullets.Select(b => BulletParagraph(b, bodySize)));
            shapes.Append(TextBox(id++, "Inhalt", Margin, 1645920, SlideWidth - 2 * Margin, 4206240, "t", bullets));

            // Fußzeile: Titel der Präsentation links, Foliennummer rechts
            shapes.Append(TextBox(id++, "Fußzeile", Margin, 6217920, 7315200, 320040, "ctr",
                Paragraph(presentationTitle, 1100, bold: false, color: ColorMuted)));
            shapes.Append(TextBox(id++, "Foliennummer", SlideWidth - Margin - 1828800, 6217920, 1828800, 320040, "ctr",
                Paragraph($"{number} / {total}", 1100, bold: false, color: ColorMuted, align: "r")));
        }

        var background = isTitle ? ColorDark : "FFFFFF";
        return $"<p:sld {Namespaces}><p:cSld>" +
               $"<p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"{background}\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>" +
               $"<p:spTree>{GroupHeader}{shapes}</p:spTree></p:cSld>" +
               "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>";
    }

    private static string TextBox(int id, string name, long x, long y, long cx, long cy, string anchor, string paragraphs) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{name}\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr>" +
        $"<p:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>" +
        "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/></p:spPr>" +
        $"<p:txBody><a:bodyPr wrap=\"square\" lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\" anchor=\"{anchor}\"><a:noAutofit/></a:bodyPr>" +
        $"<a:lstStyle/>{paragraphs}</p:txBody></p:sp>";

    private static string Rectangle(int id, string name, long x, long y, long cx, long cy, string color) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{name}\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>" +
        $"<p:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>" +
        $"<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill><a:ln><a:noFill/></a:ln></p:spPr></p:sp>";

    private static string Paragraph(string text, int size, bool bold, string color, string align = "l") =>
        $"<a:p><a:pPr algn=\"{align}\"/>{Run(text, size, bold, color)}</a:p>";

    private static string BulletParagraph(string text, int size) =>
        "<a:p><a:pPr marL=\"342900\" indent=\"-342900\"><a:spcBef><a:spcPts val=\"900\"/></a:spcBef>" +
        $"<a:buClr><a:srgbClr val=\"{ColorAccent}\"/></a:buClr><a:buFont typeface=\"Arial\"/><a:buChar char=\"•\"/></a:pPr>" +
        $"{Run(text, size, bold: false, color: ColorText)}</a:p>";

    private static string Run(string text, int size, bool bold, string color) =>
        $"<a:r><a:rPr lang=\"de-DE\" sz=\"{size}\" b=\"{(bold ? 1 : 0)}\" dirty=\"0\">" +
        $"<a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill><a:latin typeface=\"{FontName}\"/><a:cs typeface=\"{FontName}\"/></a:rPr>" +
        $"<a:t>{Esc(text)}</a:t></a:r>";

    /// <summary>Verkleinert lange Titel, damit sie in höchstens zwei Zeilen passen (Größe in 1/100 pt).</summary>
    private static int FitTitleSize(string title, int size, int charsPerLineAtSize)
    {
        var length = title?.Length ?? 0;
        if (length <= charsPerLineAtSize) return size;
        if (length <= charsPerLineAtSize * 2) return (int)(size * 0.85);
        return (int)(size * 0.7);
    }

    /// <summary>
    /// Wählt die größte Schriftgröße, bei der alle Stichpunkte in das Inhaltsfeld passen.
    /// PowerPoint passt Text beim Öffnen nicht selbst an, daher wird hier vorab geschätzt.
    /// </summary>
    public static int FitBulletSize(IReadOnlyCollection<string> bullets)
    {
        const double boxWidthPt = (SlideWidth - 2 * Margin - 342900) / 12700.0; // EMU → pt, abzüglich Einzug
        const double boxHeightPt = 4206240 / 12700.0;

        foreach (var size in new[] { 28, 24, 22, 20, 18, 16, 14 })
        {
            var charsPerLine = Math.Max(10, (int)(boxWidthPt / (size * 0.52)));
            var lines = bullets.Sum(b => Math.Max(1, (int)Math.Ceiling(b.Length / (double)charsPerLine)));
            var height = lines * size * 1.2 + bullets.Count * 9; // Zeilenhöhe + Abstand vor jedem Punkt
            if (height <= boxHeightPt)
                return size * 100;
        }

        return 1400;
    }

    // ---------------- Design (Theme) ----------------

    private static string Theme() =>
        $"<a:theme xmlns:a=\"{NsA}\" name=\"StudyScope\"><a:themeElements>" +
        "<a:clrScheme name=\"StudyScope\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        $"<a:dk2><a:srgbClr val=\"{ColorText}\"/></a:dk2><a:lt2><a:srgbClr val=\"F8F9FA\"/></a:lt2>" +
        $"<a:accent1><a:srgbClr val=\"{ColorAccent}\"/></a:accent1><a:accent2><a:srgbClr val=\"12B886\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"F76707\"/></a:accent3><a:accent4><a:srgbClr val=\"AE3EC9\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"E64980\"/></a:accent5><a:accent6><a:srgbClr val=\"2F9E44\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink></a:clrScheme>" +
        "<a:fontScheme name=\"StudyScope\">" +
        $"<a:majorFont><a:latin typeface=\"{FontName}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        $"<a:minorFont><a:latin typeface=\"{FontName}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"StudyScope\">" +
        "<a:fillStyleLst>" + Repeat("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>", 3) + "</a:fillStyleLst>" +
        "<a:lnStyleLst>" + Repeat("<a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>", 3) + "</a:lnStyleLst>" +
        "<a:effectStyleLst>" + Repeat("<a:effectStyle><a:effectLst/></a:effectStyle>", 3) + "</a:effectStyleLst>" +
        "<a:bgFillStyleLst>" + Repeat("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>", 3) + "</a:bgFillStyleLst>" +
        "</a:fmtScheme></a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";

    private static string Repeat(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    // ---------------- Hilfen ----------------

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
        writer.Write(xml);
    }

    /// <summary>Maskiert XML-Sonderzeichen und entfernt in XML unzulässige Steuerzeichen.</summary>
    private static string Esc(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var valid = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                valid.Append(c).Append(text[++i]); // Emoji u. Ä. (Surrogatpaar) behalten
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                valid.Append(c);
            }
        }

        return SecurityElement.Escape(valid.ToString()) ?? string.Empty;
    }
}
