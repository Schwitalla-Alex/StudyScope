using System.Text;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;
using StudyScope.ViewModels.DataTable;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Tests für das DataTableViewModel: Laden je Profil, Erstellen mit der Fake-KI, Fehlermeldungen, Bearbeiten mit sofortigem
/// Speichern, Sortierung/Filter, Zeilengrenze, „Weitere Zeilen ergänzen“ und Export (alle bzw. nur gefilterte Zeilen).
/// </summary>
[TestFixture]
public class DataTableViewModelTests
{
    private string _tempDir = null!;
    private StubLlm _llm = null!;
    private DataTableService _service = null!;
    private DataTableViewModel _viewModel = null!;

    [SetUp]
    public async Task SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeDataTableVm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _llm = new StubLlm();
        _service = new DataTableService(_llm, _tempDir);
        _viewModel = new DataTableViewModel(_service, new FakeDocumentService());
        await _viewModel.LoadForProfileAsync("Testprofil");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static List<DocumentItem> Documents(bool selected = true) => new()
    {
        new DocumentItem { FileName = "skript.txt", FilePath = "skript.txt", IsSelected = selected }
    };

    private async Task<DataTableDocument> CreateTableAsync(params string[] firstColumn)
    {
        var rows = string.Join(", ", firstColumn.Select(v => $"[\"{v}\", \"Info {v}\"]"));
        _llm.Response = $"[{{\"title\": \"Testtabelle\", \"columns\": [\"Begriff\", \"Info\"], \"rows\": [{rows}]}}]";
        await _viewModel.GenerateAsync(Documents());
        _llm.Response = null;
        return _viewModel.SelectedTable!;
    }

    private Task<DataTableLibrary> Reload() => new DataTableService(_llm, _tempDir).LoadLibraryAsync("Testprofil");

    // ---------- Laden und Erstellen ----------

    [Test]
    public void Load_WithoutTables_OpensForm()
    {
        Assert.That(_viewModel.Tables, Is.Empty);
        Assert.That(_viewModel.IsFormOpen, Is.True);
        Assert.That(_viewModel.SelectedTable, Is.Null);
        Assert.That(_viewModel.VisibleRows, Is.Empty);
    }

    [Test]
    public async Task Generate_WithFakeAi_CreatesSavesAndSelectsTable()
    {
        _viewModel.Template = DataTableTemplate.Timeline;
        _viewModel.CustomTitle = "Meine Zeitleiste";

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.IsError, Is.False, _viewModel.StatusMessage);
        var table = _viewModel.SelectedTable!;
        Assert.That(table.Title, Is.EqualTo("Meine Zeitleiste"));
        Assert.That(table.Template, Is.EqualTo(DataTableTemplate.Timeline));
        Assert.That(table.Columns, Is.EqualTo(new[] { "Datum", "Ereignis", "Bedeutung" }));
        Assert.That(table.SourceDocuments, Is.EqualTo(new[] { "skript.txt" }));
        Assert.That(table.Rows, Has.All.Matches<DataTableRow>(r => r.Source == "skript.txt"));
        Assert.That(_viewModel.IsFormOpen, Is.False);
        Assert.That(_viewModel.CustomTitle, Is.Empty);
        Assert.That((await Reload()).Tables, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Generate_PassesCustomColumnsAndTopic()
    {
        _viewModel.Template = DataTableTemplate.Custom;
        _viewModel.CustomColumns = "Organell; Aufgabe, Quelle, aufgabe";
        _viewModel.Topic = "Zellatmung";

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_llm.LastPrompt, Does.Contain("Organell | Aufgabe").And.Contain("„Zellatmung“"));
        Assert.That(_viewModel.SelectedTable!.Columns, Is.EqualTo(new[] { "Organell", "Aufgabe" }), "„Quelle“ und doppelte Spalten fallen weg");
        Assert.That(_viewModel.SelectedTable.Topic, Is.EqualTo("Zellatmung"));
    }

    [Test]
    public async Task Generate_WithoutDocuments_OrCustomWithoutInstruction_ShowsError()
    {
        await _viewModel.GenerateAsync(Documents(selected: false));
        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_llm.CallCount, Is.Zero);

        _viewModel.Template = DataTableTemplate.Custom;
        await _viewModel.GenerateAsync(Documents());
        Assert.That(_viewModel.StatusMessage, Does.Contain("Eigene Vorgabe"));
        Assert.That(_llm.CallCount, Is.Zero);
    }

    [Test]
    public async Task Generate_TextInsteadOfJson_ShowsFailureWithDetails()
    {
        _llm.Response = "Dazu habe ich leider keine Angaben gefunden.";

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("Datentabelle"));
        Assert.That(_viewModel.StatusDetails, Does.Contain("keine Angaben"));
        Assert.That(_viewModel.Tables, Is.Empty);
    }

    [Test]
    public async Task Generate_ServiceException_IsShownAsError()
    {
        _llm.Throw = new InvalidOperationException("Dienst nicht erreichbar");

        await _viewModel.GenerateAsync(Documents());

        Assert.That(_viewModel.IsError, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("Dienst nicht erreichbar"));
        Assert.That(_viewModel.IsBusy, Is.False);
    }

    // ---------- Verwalten ----------

    [Test]
    public async Task RenameAndDelete_AreSaved()
    {
        var table = await CreateTableAsync("A", "B");

        Assert.That(await _viewModel.RenameTableAsync(table.Id, "  Neuer Name "), Is.True);
        Assert.That(await _viewModel.RenameTableAsync(table.Id, "   "), Is.False);
        Assert.That((await Reload()).Tables[0].Title, Is.EqualTo("Neuer Name"));

        await _viewModel.DeleteTableAsync(table.Id);
        Assert.That(_viewModel.Tables, Is.Empty);
        Assert.That(_viewModel.SelectedTable, Is.Null);
        Assert.That(_viewModel.IsFormOpen, Is.True);
        Assert.That((await Reload()).Tables, Is.Empty);
    }

    [Test]
    public async Task ProfileSwitch_LoadsOtherLibrary()
    {
        await CreateTableAsync("A");

        await _viewModel.LoadForProfileAsync("Anderes Profil");
        Assert.That(_viewModel.Tables, Is.Empty);

        await _viewModel.LoadForProfileAsync("Testprofil");
        Assert.That(_viewModel.Tables, Has.Count.EqualTo(1));
        Assert.That(_viewModel.SelectedTable, Is.Not.Null);
    }

    // ---------- Bearbeiten ----------

    [Test]
    public async Task EditCellsRowsAndColumns_SavesImmediately()
    {
        var table = await CreateTableAsync("A", "B");
        var rowId = table.Rows[0].Id;

        Assert.That(await _viewModel.SetCellAsync(rowId, 1, " geändert "), Is.True);
        Assert.That(await _viewModel.SetCellAsync(rowId, DataTableSorting.SourceColumn, "andere.pdf"), Is.True);
        Assert.That(await _viewModel.SetCellAsync(rowId, 7, "x"), Is.False);

        var newRow = await _viewModel.AddRowAsync();
        Assert.That(newRow, Is.Not.Null);
        await _viewModel.DeleteRowAsync(table.Rows[1].Id);

        Assert.That(await _viewModel.AddColumnAsync("Beispiel"), Is.True);
        Assert.That(await _viewModel.RenameColumnAsync(0, "Fachbegriff"), Is.True);
        Assert.That(await _viewModel.DeleteColumnAsync(1), Is.True);

        var saved = (await Reload()).Tables.Single();
        Assert.That(saved.Columns, Is.EqualTo(new[] { "Fachbegriff", "Beispiel" }));
        Assert.That(saved.Rows, Has.Count.EqualTo(2));
        Assert.That(saved.Rows[0].Cells, Is.EqualTo(new[] { "A", "" }));
        Assert.That(saved.Rows[0].Source, Is.EqualTo("andere.pdf"));
        Assert.That(saved.Rows[1].Id, Is.EqualTo(newRow));
        Assert.That(saved.Rows.All(r => r.Cells.Count == 2), Is.True);
    }

    [Test]
    public async Task ColumnNames_AreValidated()
    {
        await CreateTableAsync("A");

        Assert.That(await _viewModel.AddColumnAsync("info"), Is.False, "doppelt (ohne Groß-/Kleinschreibung)");
        Assert.That(await _viewModel.AddColumnAsync("Quelle"), Is.False, "reserviert");
        Assert.That(await _viewModel.RenameColumnAsync(0, ""), Is.False);
        Assert.That(await _viewModel.RenameColumnAsync(0, "begriff"), Is.True, "eigene Spalte darf anders geschrieben werden");
        Assert.That(await _viewModel.DeleteColumnAsync(1), Is.True);
        Assert.That(await _viewModel.DeleteColumnAsync(0), Is.False, "letzte Spalte bleibt");
        Assert.That(_viewModel.IsError, Is.True);
    }

    [Test]
    public async Task DeleteColumn_AdjustsSortColumn()
    {
        await CreateTableAsync("A");
        await _viewModel.AddColumnAsync("Drei");
        _viewModel.ToggleSort(2);

        await _viewModel.DeleteColumnAsync(0);
        Assert.That(_viewModel.SortColumn, Is.EqualTo(1));

        await _viewModel.DeleteColumnAsync(1);
        Assert.That(_viewModel.SortColumn, Is.Null);
    }

    // ---------- Sortierung und Filter ----------

    [Test]
    public async Task ToggleSort_SwitchesDirection_AndFilterNarrowsRows()
    {
        await CreateTableAsync("10", "9", "100");

        _viewModel.ToggleSort(0);
        Assert.That(_viewModel.VisibleRows.Select(r => r.Cells[0]), Is.EqualTo(new[] { "9", "10", "100" }));
        _viewModel.ToggleSort(0);
        Assert.That(_viewModel.SortDescending, Is.True);
        Assert.That(_viewModel.VisibleRows.Select(r => r.Cells[0]), Is.EqualTo(new[] { "100", "10", "9" }));

        _viewModel.FilterText = "Info 10";
        Assert.That(_viewModel.HasFilter, Is.True);
        Assert.That(_viewModel.VisibleRows.Select(r => r.Cells[0]), Is.EqualTo(new[] { "100", "10" }));

        _viewModel.ClearSort();
        Assert.That(_viewModel.SortColumn, Is.Null);
    }

    [Test]
    public async Task SelectingAnotherTable_ResetsSortAndFilter()
    {
        var first = await CreateTableAsync("A");
        await CreateTableAsync("B");
        _viewModel.ToggleSort(0);
        _viewModel.FilterText = "B";
        _viewModel.ExportFilteredOnly = true;

        _viewModel.SelectTable(first.Id);

        Assert.That(_viewModel.SortColumn, Is.Null);
        Assert.That(_viewModel.FilterText, Is.Empty);
        Assert.That(_viewModel.ExportFilteredOnly, Is.False);
    }

    // ---------- Grenzen ----------

    [Test]
    public async Task RowLimit_DisablesAddingRowsAndMoreRows()
    {
        var table = await CreateTableAsync("A");
        while (table.Rows.Count < DataTableDocument.MaxRows)
            table.Rows.Add(new DataTableRow { Cells = new List<string> { $"Z{table.Rows.Count}", "" } });
        await _viewModel.SetCellAsync(table.Rows[0].Id, 1, "aktualisiert");

        Assert.That(_viewModel.IsAtRowLimit, Is.True);
        Assert.That(_viewModel.CanAddMoreRows, Is.False);
        Assert.That(await _viewModel.AddRowAsync(), Is.Null);
        Assert.That(_viewModel.StatusMessage, Does.Contain("500"));

        var calls = _llm.CallCount;
        await _viewModel.GenerateMoreRowsAsync(Documents());
        Assert.That(_llm.CallCount, Is.EqualTo(calls), "an der Grenze wird die KI nicht aufgerufen");
        Assert.That(table.Rows, Has.Count.EqualTo(DataTableDocument.MaxRows));
    }

    // ---------- Weitere Zeilen ----------

    [Test, SetUICulture("en")]
    public async Task GenerateMoreRows_English_PluralFormsAndButtonInTips()
    {
        var table = await CreateTableAsync("Enzym");
        _llm.Response = """[{"columns": ["Begriff", "Info"], "rows": [["enzym", "doppelt"], ["Hormon", "neu"]]}]""";

        await _viewModel.GenerateMoreRowsAsync(Documents(selected: false));

        Assert.That(_viewModel.StatusMessage, Is.EqualTo("✓ 1 new row added (1 duplicate skipped)."));
        Assert.That(table.Rows, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GenerateMoreRows_AddsNewRows_SkipsDuplicates_AndRespectsLimit()
    {
        var table = await CreateTableAsync("Enzym");
        _llm.Response = """[{"columns": ["Begriff", "Info"], "rows": [["enzym", "doppelt"], ["Hormon", "neu"], ["Vitamin", "neu"]]}]""";

        await _viewModel.GenerateMoreRowsAsync(Documents(selected: false));

        Assert.That(_viewModel.IsError, Is.False, _viewModel.StatusMessage);
        Assert.That(table.Rows.Select(r => r.Cells[0]), Is.EqualTo(new[] { "Enzym", "Hormon", "Vitamin" }));
        Assert.That(_viewModel.StatusMessage, Does.Contain("2 neue Zeilen").And.Contain("1 Wiederholung"));
        Assert.That(_llm.LastPrompt, Does.Contain("- Enzym"));
        Assert.That(table.AppendCount, Is.EqualTo(1));
        Assert.That((await Reload()).Tables[0].Rows, Has.Count.EqualTo(3));

        // Kurz vor der Grenze: nur so viele Zeilen wie noch Platz ist
        while (table.Rows.Count < DataTableDocument.MaxRows - 1)
            table.Rows.Add(new DataTableRow { Cells = new List<string> { $"Z{table.Rows.Count}", "" } });
        _llm.Response = """[{"columns": ["Begriff", "Info"], "rows": [["Neu 1", ""], ["Neu 2", ""]]}]""";

        await _viewModel.GenerateMoreRowsAsync(Documents());

        Assert.That(table.Rows, Has.Count.EqualTo(DataTableDocument.MaxRows));
        Assert.That(_viewModel.IsAtRowLimit, Is.True);
        Assert.That(_viewModel.IsWarning, Is.True);
    }

    [Test]
    public async Task GenerateMoreRows_OnlyDuplicates_LeavesTableUnchanged()
    {
        var table = await CreateTableAsync("Enzym");
        _llm.Response = """[{"columns": ["Begriff", "Info"], "rows": [["Enzym", "nochmal"]]}]""";

        await _viewModel.GenerateMoreRowsAsync(Documents());

        Assert.That(table.Rows, Has.Count.EqualTo(1));
        Assert.That(_viewModel.IsWarning, Is.True);
        Assert.That(_viewModel.StatusMessage, Does.Contain("unverändert"));
    }

    // ---------- Export ----------

    [Test]
    public async Task Export_AllRowsInSortOrder_ByDefault_OrOnlyFiltered()
    {
        await CreateTableAsync("b", "c", "a");
        _viewModel.ToggleSort(0);
        _viewModel.FilterText = "Info c";
        var path = Path.Combine(_tempDir, "export.csv");

        Assert.That(await _viewModel.ExportAsync(DataTableExportFormat.Csv, path), Is.True);
        var all = File.ReadAllText(path, Encoding.UTF8).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(all.Skip(1).Select(l => l.Split(';')[0]), Is.EqualTo(new[] { "a", "b", "c" }), "Standard: alle Zeilen, sortiert");

        _viewModel.ExportFilteredOnly = true;
        Assert.That(await _viewModel.ExportAsync(DataTableExportFormat.Csv, path), Is.True);
        var filtered = File.ReadAllText(path, Encoding.UTF8).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.That(filtered.Skip(1).Select(l => l.Split(';')[0]), Is.EqualTo(new[] { "c" }));
        Assert.That(_viewModel.StatusMessage, Does.Contain("1 von 3 Zeilen"));

        // Ohne Filter wirkt das Häkchen nicht
        _viewModel.FilterText = "";
        Assert.That(_viewModel.RowsForExport, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task Export_ExcelAndMarkdown_WriteFiles_AndSuggestFileName()
    {
        await CreateTableAsync("A");
        var xlsx = Path.Combine(_tempDir, "t.xlsx");
        var md = Path.Combine(_tempDir, "t.md");

        Assert.That(await _viewModel.ExportAsync(DataTableExportFormat.Excel, xlsx), Is.True);
        Assert.That(await _viewModel.ExportAsync(DataTableExportFormat.Markdown, md), Is.True);

        Assert.That(new FileInfo(xlsx).Length, Is.GreaterThan(0));
        Assert.That(File.ReadAllText(md), Does.Contain("| Begriff | Info | Quelle |"));
        Assert.That(_viewModel.SuggestFileName(DataTableExportFormat.Excel), Does.StartWith("Testtabelle_").And.EndWith(".xlsx"));
    }

    [Test]
    public async Task Export_Failure_ShowsError()
    {
        await CreateTableAsync("A");

        var ok = await _viewModel.ExportAsync(DataTableExportFormat.Csv, Path.Combine(_tempDir, "fehlt", "x.csv"));

        Assert.That(ok, Is.False);
        Assert.That(_viewModel.IsError, Is.True);
    }

    [Test]
    public void ParseColumns_SplitsAndCleans()
    {
        Assert.That(DataTableViewModel.ParseColumns("A, B;C|D\nE, , a, quelle"), Is.EqualTo(new[] { "A", "B", "C", "D", "E" }));
        Assert.That(DataTableViewModel.ParseColumns(null), Is.Empty);
    }

    // ---------- Hilfen ----------

    private sealed class StubLlm : ILlmService
    {
        private readonly FakeLlmService _fake = new();
        public string? Response { get; set; }
        public Exception? Throw { get; set; }
        public int CallCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastPrompt = prompt;
            if (Throw != null) throw Throw;
            return Response == null ? _fake.AskQuestionAsync(prompt, contextChunks, cancellationToken) : Task.FromResult(Response);
        }

        public Task<bool> ValidateConnectionAsync(AiConfig? config = null) => Task.FromResult(true);
    }

    private sealed class FakeDocumentService : IDocumentService
    {
        public Task<int> GetChunkCountAsync(string filePath) => Task.FromResult(1);

        public Task<List<string>> ExtractChunksAsync(string filePath, int maxWordsPerChunk = 250) =>
            Task.FromResult(new List<string> { "Die Französische Revolution begann 1789 mit dem Sturm auf die Bastille. Napoleon krönte sich 1804 zum Kaiser." });
    }
}
