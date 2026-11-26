using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Common;

namespace StudyScope.Tests.Services;

/// <summary>Tests für die Exporte einer Datentabelle: CSV (Excel), Excel-Arbeitsmappe (.xlsx als ZIP geprüft) und Markdown.</summary>
[TestFixture]
public class DataTableExportTests
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeDataTableExport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static DataTableDocument SampleTable()
    {
        var table = new DataTableDocument
        {
            Title = "Größen: Übersicht [Test]",
            Template = DataTableTemplate.Formulas,
            Columns = new List<string> { "Größe", "Wert", "Hinweis" }
        };
        table.Rows.Add(new DataTableRow { Cells = new List<string> { "Ölpreis", "3,5", "Semikolon; im Text" }, Source = "übung.txt" });
        table.Rows.Add(new DataTableRow { Cells = new List<string> { "Zitat", "007", "Er sagte \"Hallo\"\nZweite Zeile" }, Source = "skript.pdf" });
        table.Rows.Add(new DataTableRow { Cells = new List<string> { "Pipe | Zeichen", "1.234", "" }, Source = "" });
        return table;
    }

    // ---------- CSV ----------

    [Test]
    public void Csv_HasBom_Semicolons_SourceColumn_AndQuoting()
    {
        var table = SampleTable();

        var bytes = DataTableExporter.ToCsvBytes(table, table.Rows, TestCultures.De, ';');

        Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8-BOM für Excel");
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n");
        Assert.That(lines[0], Is.EqualTo("Größe;Wert;Hinweis;Quelle"));
        Assert.That(lines[1], Is.EqualTo("Ölpreis;3,5;\"Semikolon; im Text\";übung.txt"));
        Assert.That(text, Does.Contain("\"Er sagte \"\"Hallo\"\"\nZweite Zeile\""));
        Assert.That(text, Does.EndWith("\r\n"));
    }

    [Test]
    public void Csv_UsesGivenRowsInGivenOrder()
    {
        var table = SampleTable();
        var rows = new[] { table.Rows[2], table.Rows[0] };

        var csv = DataTableExporter.ToCsv(table, rows, TestCultures.De, ';');

        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(3));
        Assert.That(lines[1], Does.StartWith("Pipe | Zeichen;"));
        Assert.That(lines[2], Does.StartWith("Ölpreis;"));
    }

    // ---------- Markdown ----------

    [Test]
    public void Markdown_EscapesPipesAndLineBreaks()
    {
        var table = SampleTable();

        var md = DataTableExporter.ToMarkdown(table, table.Rows, TestCultures.De);

        Assert.That(md, Does.StartWith("# Größen: Übersicht [Test]"));
        Assert.That(md, Does.Contain("| Größe | Wert | Hinweis | Quelle |"));
        Assert.That(md, Does.Contain("| --- | --- | --- | --- |"));
        Assert.That(md, Does.Contain("Pipe \\| Zeichen"));
        Assert.That(md, Does.Contain("Er sagte \"Hallo\"<br>Zweite Zeile"));
        Assert.That(md, Does.Contain("3 Zeilen"));
    }

    // ---------- XLSX ----------

    [Test]
    public async Task Xlsx_IsValidPackage_WithHeaderRowsNumbersAndFilter()
    {
        var table = SampleTable();
        var service = new DataTableService(new FakeLlmService(), _tempDir);
        var path = Path.Combine(_tempDir, "tabelle.xlsx");

        await service.ExportToXlsxAsync(table, table.Rows, path, TestCultures.De);

        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.That(names, Is.SupersetOf(new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml" }));

        // Alle Teile müssen wohlgeformtes XML sein
        foreach (var entry in zip.Entries)
        {
            using var s = entry.Open();
            Assert.DoesNotThrow(() => XDocument.Load(s), entry.FullName);
        }

        var workbook = Load(zip, "xl/workbook.xml");
        var sheetName = workbook.Descendants(Main + "sheet").Single().Attribute("name")!.Value;
        Assert.That(sheetName, Is.EqualTo("Größen Übersicht Test"), "unzulässige Zeichen entfernt");
        Assert.That(sheetName.Length, Is.LessThanOrEqualTo(31));

        var sheet = Load(zip, "xl/worksheets/sheet1.xml");
        var rows = sheet.Descendants(Main + "row").ToList();
        Assert.That(rows, Has.Count.EqualTo(4), "Kopfzeile + 3 Zeilen");
        Assert.That(CellTexts(rows[0]), Is.EqualTo(new[] { "Größe", "Wert", "Hinweis", "Quelle" }));
        Assert.That(CellTexts(rows[1]), Is.EqualTo(new[] { "Ölpreis", "3.5", "Semikolon; im Text", "übung.txt" }));

        var number = rows[1].Elements(Main + "c").ElementAt(1);
        Assert.That(number.Attribute("t"), Is.Null, "3,5 wird als Zahl geschrieben");
        Assert.That(number.Element(Main + "v")!.Value, Is.EqualTo("3.5"));

        var leadingZero = rows[2].Elements(Main + "c").ElementAt(1);
        Assert.That(leadingZero.Attribute("t")!.Value, Is.EqualTo("inlineStr"), "007 bleibt Text");
        var ambiguous = rows[3].Elements(Main + "c").ElementAt(1);
        Assert.That(ambiguous.Attribute("t")!.Value, Is.EqualTo("inlineStr"), "1.234 ist mehrdeutig und bleibt Text");
        Assert.That(CellTexts(rows[2])[2], Is.EqualTo("Er sagte \"Hallo\"\nZweite Zeile"));

        Assert.That(sheet.Descendants(Main + "autoFilter").Single().Attribute("ref")!.Value, Is.EqualTo("A1:D4"));
        Assert.That(sheet.Descendants(Main + "pane").Single().Attribute("state")!.Value, Is.EqualTo("frozen"));
        Assert.That(sheet.Descendants(Main + "col").Count(), Is.EqualTo(4));
    }

    [Test]
    public void Xlsx_EmptyCellsAreOmitted_AndControlCharsRemoved()
    {
        var table = new DataTableDocument { Title = "", Columns = new List<string> { "A", "B" } };
        table.Rows.Add(new DataTableRow { Cells = new List<string> { "x\u0001y", "" } });

        using var buffer = new MemoryStream();
        XlsxWriter.Write(table, table.Rows, TestCultures.De, buffer);
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);

        var sheet = Load(zip, "xl/worksheets/sheet1.xml");
        var dataRow = sheet.Descendants(Main + "row").ElementAt(1);
        Assert.That(dataRow.Elements(Main + "c").Select(c => c.Attribute("r")!.Value), Is.EqualTo(new[] { "A2" }));
        Assert.That(CellTexts(dataRow), Is.EqualTo(new[] { "xy" }));
        Assert.That(Load(zip, "xl/workbook.xml").Descendants(Main + "sheet").Single().Attribute("name")!.Value, Is.EqualTo("Datentabelle"));
    }

    [TestCase(0, "A")]
    [TestCase(25, "Z")]
    [TestCase(26, "AA")]
    [TestCase(701, "ZZ")]
    [TestCase(702, "AAA")]
    public void ColumnLetter_ConvertsIndex(int index, string expected) =>
        Assert.That(XlsxWriter.ColumnLetter(index), Is.EqualTo(expected));

    [Test]
    public void SheetName_IsShortenedTo31Characters()
    {
        var name = XlsxWriter.SheetName(new string('a', 40));
        Assert.That(name, Has.Length.EqualTo(31));
    }

    private static XDocument Load(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    private static List<string> CellTexts(XElement row) =>
        row.Elements(Main + "c")
            .Select(c => c.Element(Main + "v")?.Value ?? string.Concat(c.Descendants(Main + "t").Select(t => t.Value)))
            .ToList();
}
