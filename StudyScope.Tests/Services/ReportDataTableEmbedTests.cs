using System.IO.Compression;
using System.Xml.Linq;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Datentabellen als Einbettung in Berichten: ältere Berichte ohne Eigenschaft, Filter der Einbettungen und
/// die Exporte Word (echte Tabelle), Markdown, Text und HTML (sortierbar).
/// </summary>
[TestFixture]
public class ReportDataTableEmbedTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeReportTables_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ReportService Service() => new(new FakeLlmService(), _tempDir);

    private static DataTableDocument Timeline(int rows = 3)
    {
        var table = new DataTableDocument
        {
            Title = "Zeitleiste Revolution",
            Template = DataTableTemplate.Timeline,
            Topic = "Frankreich",
            Columns = new List<string> { "Datum", "Ereignis", "Bedeutung" }
        };
        var data = new[] { ("14.07.1789", "Sturm auf die Bastille", "Beginn"), ("1804", "Kaiserkrönung", ""), ("500 v. Chr.", "Antike | Vorbild", "Zeile 1\nZeile 2") };
        for (var i = 0; i < rows; i++)
        {
            var (a, b, c) = data[i % data.Length];
            table.Rows.Add(new DataTableRow { Cells = new List<string> { a, i < data.Length ? b : $"{b} {i}", c }, Source = "skript.txt" });
        }
        return table;
    }

    private static Report Report(bool? dataTables) => new()
    {
        Title = "Geschichte",
        Template = ReportTemplate.Briefing,
        SourceDocuments = new List<string> { "skript.txt" },
        Embeds = new ReportEmbeds { Mindmap = false, Flashcards = false, Quiz = false, Presentation = false, DataTables = dataTables },
        Sections = new List<ReportSection> { new() { Heading = "Überblick", Paragraphs = new List<string> { "Text." } } }
    };

    private static ReportStudioContent Studio(DataTableDocument table, int take = int.MaxValue) => new()
    {
        DataTables = new[] { new ReportDataTable(table, table.Rows.Take(take).ToList()) }
    };

    // ---------- Laden älterer Berichte ----------

    [Test]
    public async Task Load_OldReportWithoutProperty_DoesNotEmbedTables_NewOnesKeepTheirSetting()
    {
        var folder = Path.Combine(_tempDir, "Reports");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Testprofil.json"), """
            {"Reports": [
              {"Id": "alt", "Title": "Alt", "Sections": [{"Heading": "A", "Paragraphs": ["x"]}],
               "Embeds": {"Mindmap": true, "Flashcards": true, "Quiz": true, "Presentation": true}},
              {"Id": "ohne", "Title": "Ohne Einbettungen", "Sections": [{"Heading": "A", "Paragraphs": ["x"]}]},
              {"Id": "neu", "Title": "Neu", "Sections": [{"Heading": "A", "Paragraphs": ["x"]}],
               "Embeds": {"Mindmap": true, "Flashcards": true, "Quiz": true, "Presentation": true, "DataTables": true}}
            ]}
            """);

        var library = await Service().LoadLibraryAsync("Testprofil");

        Assert.That(library.Reports.Single(r => r.Id == "alt").Embeds.DataTables, Is.False);
        Assert.That(library.Reports.Single(r => r.Id == "ohne").Embeds.DataTables, Is.False);
        Assert.That(library.Reports.Single(r => r.Id == "neu").Embeds.DataTables, Is.True);
    }

    [Test]
    public async Task SaveAndLoad_KeepsExplicitSetting()
    {
        var library = new ReportLibrary();
        library.Reports.Add(Report(true));
        library.Reports.Add(Report(false));

        await Service().SaveLibraryAsync("Testprofil", library);
        var loaded = await Service().LoadLibraryAsync("Testprofil");

        Assert.That(loaded.Reports.Select(r => r.Embeds.DataTables), Is.EqualTo(new bool?[] { true, false }));
    }

    [Test]
    public void Filter_DropsTablesUnlessExplicitlyEnabled()
    {
        var studio = Studio(Timeline());

        Assert.That(studio.Filter(new ReportEmbeds { DataTables = true }).DataTables, Has.Count.EqualTo(1));
        Assert.That(studio.Filter(new ReportEmbeds { DataTables = false }).DataTables, Is.Empty);
        Assert.That(studio.Filter(new ReportEmbeds()).DataTables, Is.Empty, "fehlende Angabe = älterer Bericht");
        Assert.That(studio.HasAny, Is.True);
        Assert.That(ReportStudioContent.Empty.HasAny, Is.False);
    }

    [Test]
    public void Exports_OfReportWithoutTables_AreUnchangedByTheFeature()
    {
        var table = Timeline();
        var service = Service();

        foreach (var report in new[] { Report(null), Report(false) })
        {
            Assert.That(service.ExportToMarkdown(report, Studio(table)), Is.EqualTo(service.ExportToMarkdown(report, ReportStudioContent.Empty)));
            Assert.That(service.ExportToPlainText(report, Studio(table)), Is.EqualTo(service.ExportToPlainText(report, ReportStudioContent.Empty)));
            Assert.That(service.ExportToHtml(report, Studio(table)), Is.EqualTo(service.ExportToHtml(report, ReportStudioContent.Empty)));
        }
    }

    // ---------- Word ----------

    [Test]
    public async Task Docx_ContainsRealTableWithHeaderAndSourceColumn()
    {
        var path = Path.Combine(_tempDir, "bericht.docx");

        await Service().ExportToDocxAsync(Report(true), Studio(Timeline()), path);

        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        var doc = XDocument.Load(stream);
        var table = doc.Descendants(W + "tbl").Single();

        var gridCols = table.Element(W + "tblGrid")!.Elements(W + "gridCol").ToList();
        Assert.That(gridCols, Has.Count.EqualTo(4));
        Assert.That(gridCols.Sum(g => int.Parse(g.Attribute(W + "w")!.Value)), Is.LessThanOrEqualTo(11906 - 2 * 1417));

        var rows = table.Elements(W + "tr").ToList();
        Assert.That(rows, Has.Count.EqualTo(4), "Kopfzeile + 3 Zeilen");
        Assert.That(rows[0].Element(W + "trPr")!.Element(W + "tblHeader"), Is.Not.Null, "Kopfzeile wiederholt sich auf jeder Seite");
        Assert.That(rows.All(r => r.Elements(W + "tc").Count() == 4), Is.True);

        string CellText(XElement tc) => string.Concat(tc.Descendants(W + "t").Select(t => t.Value));
        Assert.That(rows[0].Elements(W + "tc").Select(CellText), Is.EqualTo(new[] { "Datum", "Ereignis", "Bedeutung", "Quelle" }));
        Assert.That(rows[1].Elements(W + "tc").Select(CellText), Is.EqualTo(new[] { "14.07.1789", "Sturm auf die Bastille", "Beginn", "skript.txt" }));
        Assert.That(rows[3].Elements(W + "tc").ElementAt(2).Descendants(W + "br").Count(), Is.EqualTo(1), "Zeilenumbruch in der Zelle");

        var allText = string.Concat(doc.Descendants(W + "t").Select(t => t.Value));
        Assert.That(allText, Does.Contain("Datentabelle").And.Contain("Zeitleiste Revolution").And.Contain("Thema: Frankreich"));
    }

    [Test]
    public async Task Docx_RunPropertiesFollowSchemaOrder()
    {
        var path = Path.Combine(_tempDir, "bericht.docx");
        var table = Timeline();
        table.Columns.AddRange(new[] { "Ort", "Person" }); // > 4 Spalten → kompakte Schrift
        foreach (var row in table.Rows) row.Cells.AddRange(new[] { "Paris", "" });

        await Service().ExportToDocxAsync(Report(true), Studio(table), path);

        using var zip = ZipFile.OpenRead(path);
        using var stream = zip.GetEntry("word/document.xml")!.Open();
        var order = new[] { "b", "color", "sz", "szCs" };
        foreach (var rPr in XDocument.Load(stream).Descendants(W + "rPr"))
        {
            var names = rPr.Elements().Select(e => e.Name.LocalName).ToList();
            Assert.That(names.Select(n => Array.IndexOf(order, n)), Is.Ordered, string.Join(",", names));
        }
    }

    [Test]
    public void DocxColumnWidths_SumToOne_AndStayWithinBounds()
    {
        var widths = DocxWriter.DataTableColumnWidths(new ReportDataTable(Timeline(), Timeline().Rows));

        Assert.That(widths.Sum(), Is.EqualTo(1).Within(1e-9));
        Assert.That(widths, Has.All.GreaterThan(0.05));
    }

    // ---------- Markdown und Text ----------

    [Test]
    public void Markdown_And_Text_ContainTable_AndTruncationNote()
    {
        var table = Timeline(rows: 7);
        var service = Service();

        var md = service.ExportToMarkdown(Report(true), Studio(table, take: 5));
        var text = service.ExportToPlainText(Report(true), Studio(table, take: 5));

        Assert.That(md, Does.Contain("## 📊 Zeitleiste Revolution"));
        Assert.That(md, Does.Contain("| Datum | Ereignis | Bedeutung | Quelle |"));
        Assert.That(md, Does.Contain("Antike \\| Vorbild"));
        Assert.That(md, Does.Contain("erste 5 von 7 Zeilen"));
        Assert.That(text, Does.Contain("Datentabelle – Zeitleiste Revolution"));
        Assert.That(text, Does.Contain("Ereignis: Sturm auf die Bastille"));
        Assert.That(text, Does.Not.Contain("| Datum |").And.Not.Contain("| --- |"), "Text ohne Markdown-Tabellen");
        Assert.That(text, Does.Contain("erste 5 von 7 Zeilen"));
    }

    // ---------- HTML ----------

    [Test]
    public void Html_HasSortableTable_WithKindsAndSortKeys()
    {
        var html = Service().ExportToHtml(Report(true), Studio(Timeline()));

        Assert.That(html, Does.Contain("id=\"datentabellen\""));
        Assert.That(html, Does.Contain("<a href=\"#datentabellen\">"));
        Assert.That(html, Does.Contain("<th data-kind=\"date\""), "Datumsspalte erkannt");
        Assert.That(html, Does.Contain("<th data-kind=\"text\""));
        Assert.That(html, Does.Contain("data-sort=\"-500\""), "500 v. Chr. als negativer Schlüssel");
        Assert.That(html, Does.Contain("Zeile 1<br>Zeile 2"));
        Assert.That(html, Does.Contain("table.dt th").And.Contain("sortTable"));
    }

    [Test]
    public void Html_EscapesCellContent()
    {
        var table = Timeline();
        table.Rows[0].Cells[1] = "<img src=x onerror=alert(1)>";

        var html = Service().ExportToHtml(Report(true), Studio(table));

        Assert.That(html, Does.Not.Contain("<img src=x"));
        Assert.That(html, Does.Contain("&lt;img src=x onerror=alert(1)&gt;"));
    }
}
