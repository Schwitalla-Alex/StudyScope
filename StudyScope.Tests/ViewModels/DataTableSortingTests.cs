using StudyScope.Core.Common;
using StudyScope.Core.Models;
using StudyScope.ViewModels.DataTable;

namespace StudyScope.Tests.ViewModels;

/// <summary>Tests für Sortierung und Filter der Datentabelle sowie die Erkennung von Zahlen und Datumsangaben.</summary>
[TestFixture]
public class DataTableSortingTests
{
    private static List<DataTableRow> Rows(params string[] firstColumn) =>
        firstColumn.Select((v, i) => new DataTableRow { Cells = new List<string> { v, $"Zeile {i}" }, Source = i % 2 == 0 ? "a.txt" : "b.txt" }).ToList();

    private static List<string> First(IEnumerable<DataTableRow> rows) => rows.Select(r => r.Cells[0]).ToList();

    // ---------- Werte ----------

    [TestCase("42", 42)]
    [TestCase("-3,5", -3.5)]
    [TestCase("0.25", 0.25)]
    [TestCase("1.234,5", 1234.5)]
    [TestCase("0", 0)]
    public void StrictNumber_Recognized(string text, double expected)
    {
        Assert.That(DataTableValues.TryParseStrictNumber(text, out var value), Is.True);
        Assert.That(value, Is.EqualTo(expected).Within(1e-9));
    }

    [TestCase("1.234")]
    [TestCase("007")]
    [TestCase("12 kg")]
    [TestCase("abc")]
    [TestCase("")]
    public void StrictNumber_RejectsAmbiguousOrText(string text) =>
        Assert.That(DataTableValues.TryParseStrictNumber(text, out _), Is.False);

    [TestCase("3,5 %", 3.5)]
    [TestCase("12 kg", 12)]
    [TestCase("1.200 €", 1200)]
    [TestCase("€ 7,99", 7.99)]
    [TestCase("−4", -4)]
    [TestCase("2,5 Mio.", 2.5)]
    [TestCase("1,234.5", 1234.5)]
    public void SortNumber_AllowsUnits(string text, double expected)
    {
        Assert.That(DataTableValues.TryParseSortNumber(text, out var value), Is.True, text);
        Assert.That(value, Is.EqualTo(expected).Within(1e-9));
    }

    [TestCase("Kapitel 3")]
    [TestCase("3 Gesetze der Thermodynamik im Überblick")]
    [TestCase("1914–1918")]
    [TestCase("14.07.1789")]
    [TestCase("500 v. Chr.")]
    [TestCase("19. Jh.")]
    [TestCase("1.2.3")]
    public void SortNumber_RejectsText(string text) =>
        Assert.That(DataTableValues.TryParseSortNumber(text, out _), Is.False, text);

    [Test]
    public void DateKey_OrdersDifferentNotations()
    {
        var ordered = new[]
        {
            "500 v. Chr.", "um 44 v. Chr.", "800", "15. Jh.", "März 1517", "31.10.1517", "1789-07-14", "14. Juli 1789", "1914–1918", "08/1914", "ca. 2000"
        };

        var keys = ordered.Select(t =>
        {
            Assert.That(DataTableValues.TryParseDateKey(t, out var k), Is.True, t);
            return k;
        }).ToList();

        Assert.That(keys, Is.Ordered, string.Join(" | ", keys));
        Assert.That(keys[0], Is.LessThan(0));
    }

    [TestCase("Kapitel 3")]
    [TestCase("Erste Phase")]
    [TestCase("32.13.2020")]
    public void DateKey_RejectsText(string text) =>
        Assert.That(DataTableValues.TryParseDateKey(text, out _), Is.False, text);

    // ---------- Sortierung ----------

    [Test]
    public void Sort_Text_GermanRules_CaseInsensitive_EmptyLast()
    {
        var rows = Rows("zebra", "Äpfel", "", "apfel", "Birne");

        Assert.That(First(DataTableSorting.Sort(rows, 0, descending: false)), Is.EqualTo(new[] { "apfel", "Äpfel", "Birne", "zebra", "" }));
        Assert.That(First(DataTableSorting.Sort(rows, 0, descending: true)), Is.EqualTo(new[] { "zebra", "Birne", "Äpfel", "apfel", "" }));
    }

    [Test]
    public void Sort_Numbers_NumericNotAlphabetic()
    {
        var rows = Rows("10", "9,5", "100 %", "", "-2");

        Assert.That(DataTableSorting.DetectKind(rows, 0), Is.EqualTo(DataTableColumnKind.Number));
        Assert.That(First(DataTableSorting.Sort(rows, 0, false)), Is.EqualTo(new[] { "-2", "9,5", "10", "100 %", "" }));
        Assert.That(First(DataTableSorting.Sort(rows, 0, true)), Is.EqualTo(new[] { "100 %", "10", "9,5", "-2", "" }));
    }

    [Test]
    public void Sort_Dates_Chronological()
    {
        var rows = Rows("14. Juli 1789", "1492", "500 v. Chr.", "09.11.1989", "19. Jh.");

        Assert.That(DataTableSorting.DetectKind(rows, 0), Is.EqualTo(DataTableColumnKind.Date));
        Assert.That(First(DataTableSorting.Sort(rows, 0, false)), Is.EqualTo(new[] { "500 v. Chr.", "1492", "14. Juli 1789", "19. Jh.", "09.11.1989" }));
    }

    [Test]
    public void Sort_NumericDatesAndEras_AreDatesNotNumbers()
    {
        var rows = Rows("14.07.1789", "1804", "500 v. Chr.", "01.01.1790");

        Assert.That(DataTableSorting.DetectKind(rows, 0), Is.EqualTo(DataTableColumnKind.Date));
        Assert.That(First(DataTableSorting.Sort(rows, 0, false)), Is.EqualTo(new[] { "500 v. Chr.", "14.07.1789", "01.01.1790", "1804" }));
    }

    [Test]
    public void Sort_MixedColumn_FallsBackToText_AndIsStable()
    {
        var rows = Rows("b", "10", "a", "b");
        rows[3].Cells[1] = "zweites b";

        Assert.That(DataTableSorting.DetectKind(rows, 0), Is.EqualTo(DataTableColumnKind.Text));
        var sorted = DataTableSorting.Sort(rows, 0, false);
        Assert.That(First(sorted), Is.EqualTo(new[] { "10", "a", "b", "b" }));
        Assert.That(sorted[3].Cells[1], Is.EqualTo("zweites b"), "gleiche Werte behalten ihre Reihenfolge");
    }

    [Test]
    public void Sort_BySourceColumn()
    {
        var rows = Rows("x", "y", "z");

        var sorted = DataTableSorting.Sort(rows, DataTableSorting.SourceColumn, descending: true);

        Assert.That(sorted.Select(r => r.Source), Is.EqualTo(new[] { "b.txt", "a.txt", "a.txt" }));
    }

    // ---------- Filter ----------

    [Test]
    public void Filter_AllWordsMustMatch_InCellsOrSource_IgnoringCase()
    {
        var rows = new List<DataTableRow>
        {
            new() { Cells = new List<string> { "Enzym", "Biokatalysator im Stoffwechsel" }, Source = "bio.pdf" },
            new() { Cells = new List<string> { "Hormon", "Botenstoff" }, Source = "bio.pdf" },
            new() { Cells = new List<string> { "Katalysator", "Chemie" }, Source = "chemie.txt" }
        };

        Assert.That(First(DataTableSorting.Filter(rows, "katalysator")), Is.EqualTo(new[] { "Enzym", "Katalysator" }));
        Assert.That(First(DataTableSorting.Filter(rows, "katalysator BIO")), Is.EqualTo(new[] { "Enzym" }));
        Assert.That(First(DataTableSorting.Filter(rows, "chemie.txt")), Is.EqualTo(new[] { "Katalysator" }));
        Assert.That(First(DataTableSorting.Filter(rows, "\"im Stoffwechsel\"")), Is.EqualTo(new[] { "Enzym" }));
        Assert.That(DataTableSorting.Filter(rows, "   "), Has.Count.EqualTo(3));
    }

    [Test]
    public void SplitTerms_HandlesQuotes()
    {
        Assert.That(DataTableSorting.SplitTerms("a „b c“ d"), Is.EqualTo(new[] { "a", "b c", "d" }));
        Assert.That(DataTableSorting.SplitTerms("\"offen"), Is.EqualTo(new[] { "offen" }));
    }

    [Test]
    public void Apply_FiltersThenSorts()
    {
        var rows = Rows("3", "1", "2");
        rows[1].Cells[1] = "raus";

        var result = DataTableSorting.Apply(rows, "Zeile", 0, descending: false);

        Assert.That(First(result), Is.EqualTo(new[] { "2", "3" }));
    }
}
