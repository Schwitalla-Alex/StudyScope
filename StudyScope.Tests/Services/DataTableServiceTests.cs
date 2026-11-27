using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Tests für den DataTableService: Prompt, nachsichtige Auswertung verschiedener KI-Antworten, Zuordnung der Quellen,
/// Wiederholungen, Grenzen, Fake-KI (auch über den Dispatcher) und Speicherung je Profil.
/// </summary>
[TestFixture]
public class DataTableServiceTests
{
    private static readonly string[] Sources = { "Skript Biologie.pdf", "notizen.txt" };
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeDataTableTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---------- Prompt ----------

    [Test]
    public void BuildPrompt_StartsWithMarker_RequestsJsonArray_AndForbidsInventedValues()
    {
        foreach (var template in DataTableTemplates.All)
        {
            var prompt = DataTableService.BuildPrompt(template, "Vergleiche A und B", Array.Empty<string>(), null);

            Assert.That(prompt, Does.StartWith("Erstelle eine Datentabelle"), template.ToString());
            Assert.That(prompt, Does.Contain("JSON-Array"));
            Assert.That(prompt, Does.Contain("[Quelle: Dateiname]"));
            Assert.That(prompt, Does.Contain("leer"));
            Assert.That(prompt, Does.Not.Contain("Mindmap").And.Not.Contain("Lernkarteikarten").And.Not.Contain("Quizfragen"));
        }
    }

    [Test]
    public void BuildPrompt_UsesSuggestedOrCustomColumns_AndTopic()
    {
        var suggested = DataTableService.BuildPrompt(DataTableTemplate.Timeline, "", Array.Empty<string>(), "Revolution");
        Assert.That(suggested, Does.Contain("Spalten (Vorschlag").And.Contain("Datum | Ereignis | Bedeutung"));
        Assert.That(suggested, Does.Contain("„Revolution“"));

        var custom = DataTableService.BuildPrompt(DataTableTemplate.Custom, "Steuerarten", new[] { "Steuerart", "Satz" }, null);
        Assert.That(custom, Does.Contain("verbindlich").And.Contain("Steuerart | Satz"));
        Assert.That(custom, Does.Contain("„Steuerarten“"));
        Assert.That(custom, Does.Not.Contain("Thema:"));
    }

    [Test]
    public void BuildMoreRowsPrompt_ListsExistingRows_AndFixedColumns()
    {
        var table = Table(DataTableTemplate.Timeline, new[] { "Datum", "Ereignis" }, ("1789", "Sturm auf die Bastille"), ("1815", "Wiener Kongress"));

        var prompt = DataTableService.BuildMoreRowsPrompt(table);

        Assert.That(prompt, Does.StartWith("Erstelle eine Datentabelle"));
        Assert.That(prompt, Does.Contain("Spalten (verbindlich").And.Contain("Datum | Ereignis"));
        Assert.That(prompt, Does.Contain("- 1789 – Sturm auf die Bastille"));
        Assert.That(prompt, Does.Contain("gibt es bereits"));
    }

    // ---------- Auswertung ----------

    [Test]
    public void Analyze_StandardForm_WithCellsAndSource()
    {
        const string raw = """
            [{"title": "Begriffe der Zellbiologie", "columns": ["Begriff", "Definition"],
              "rows": [{"cells": ["Mitochondrium", "Kraftwerk der Zelle"], "source": "Skript Biologie.pdf"},
                       {"cells": ["Ribosom", ""], "source": "notizen"}]}]
            """;

        var result = DataTableService.AnalyzeResponse(raw, "Ersatz", Sources);

        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.None));
        var table = result.Value!;
        Assert.That(table.Title, Is.EqualTo("Begriffe der Zellbiologie"));
        Assert.That(table.Columns, Is.EqualTo(new[] { "Begriff", "Definition" }));
        Assert.That(table.Rows, Has.Count.EqualTo(2));
        Assert.That(table.Rows[0].Source, Is.EqualTo("Skript Biologie.pdf"));
        Assert.That(table.Rows[1].Source, Is.EqualTo("notizen.txt"), "Quelle ohne Endung wird zugeordnet");
        Assert.That(table.Rows[1].Cells[1], Is.Empty, "leere Zelle bleibt leer");
    }

    [Test]
    public void Analyze_RowsAsPlainArrays_AndCodeFenceWithText()
    {
        const string raw = """
            Hier ist die Tabelle:
            ```json
            {"titel": "Formeln", "spalten": ["Name", "Formel"], "zeilen": [["Satz des Pythagoras", "a² + b² = c²"], ["Kreisfläche", "π · r²", "zu viel"]]}
            ```
            """;

        var table = DataTableService.AnalyzeResponse(raw, "Ersatz", Sources).Value!;

        Assert.That(table.Title, Is.EqualTo("Formeln"));
        Assert.That(table.Columns, Is.EqualTo(new[] { "Name", "Formel", "Spalte 3" }));
        Assert.That(table.Rows[0].Cells, Is.EqualTo(new[] { "Satz des Pythagoras", "a² + b² = c²", "" }));
        Assert.That(table.Rows[1].Source, Is.Empty, "ohne Angabe und mit mehreren Dokumenten bleibt die Quelle leer");
    }

    [Test]
    public void Analyze_RowsAsObjectsWithColumnKeys_DerivesColumns_AndExtractsSourceColumn()
    {
        const string raw = """
            [{"Person": "Ada Lovelace", "Beitrag": "Erstes Programm", "Quelle": "notizen.txt"},
             {"Person": "Alan Turing", "Beitrag": "Turingmaschine", "Lebensdaten": 1912, "Quelle": "Skript Biologie.pdf"}]
            """;

        var table = DataTableService.AnalyzeResponse(raw, "Personen", Sources).Value!;

        Assert.That(table.Title, Is.EqualTo("Personen"));
        Assert.That(table.Columns, Is.EqualTo(new[] { "Person", "Beitrag", "Lebensdaten" }));
        Assert.That(table.Rows[0].Cells, Is.EqualTo(new[] { "Ada Lovelace", "Erstes Programm", "" }));
        Assert.That(table.Rows[1].Cells[2], Is.EqualTo("1912"));
        Assert.That(table.Rows.Select(r => r.Source), Is.EqualTo(new[] { "notizen.txt", "Skript Biologie.pdf" }));
    }

    [Test]
    public void Analyze_SourceColumnFromAi_IsMovedIntoRowSource()
    {
        const string raw = """[{"title": "T", "columns": ["Begriff", "Quelle", "Definition"], "rows": [["Enzym", "notizen.txt", "Biokatalysator"]]}]""";

        var table = DataTableService.AnalyzeResponse(raw, "Ersatz", Sources).Value!;

        Assert.That(table.Columns, Is.EqualTo(new[] { "Begriff", "Definition" }));
        Assert.That(table.Rows[0].Cells, Is.EqualTo(new[] { "Enzym", "Biokatalysator" }));
        Assert.That(table.Rows[0].Source, Is.EqualTo("notizen.txt"));
    }

    [Test]
    public void Analyze_JsonModeEnvelopes()
    {
        const string items = """{"items": [{"title": "Zeitleiste", "columns": ["Datum", "Ereignis"], "rows": [{"cells": ["1492", "Kolumbus"]}]}]}""";
        const string named = """{"tabelle": {"titel": "Zeitleiste", "spalten": [{"name": "Datum"}, {"name": "Ereignis"}], "daten": [{"zellen": {"Datum": "1517", "Ereignis": "Thesenanschlag"}, "quelle": "notizen.txt"}]}}""";
        const string bare = """{"begriffe": [{"term": "Osmose", "definition": "Diffusion durch eine Membran"}], "anzahl": 1}""";

        var a = DataTableService.AnalyzeResponse(items, "Ersatz", Sources).Value!;
        var b = DataTableService.AnalyzeResponse(named, "Ersatz", Sources).Value!;
        var c = DataTableService.AnalyzeResponse(bare, "Ersatz", Sources).Value!;

        Assert.That(a.Rows[0].Cells, Is.EqualTo(new[] { "1492", "Kolumbus" }));
        Assert.That(b.Columns, Is.EqualTo(new[] { "Datum", "Ereignis" }));
        Assert.That(b.Rows[0].Cells, Is.EqualTo(new[] { "1517", "Thesenanschlag" }));
        Assert.That(b.Rows[0].Source, Is.EqualTo("notizen.txt"));
        Assert.That(c.Columns, Is.EqualTo(new[] { "term", "definition" }));
        Assert.That(c.Rows[0].Cells[0], Is.EqualTo("Osmose"));
    }

    [Test]
    public void Analyze_TruncatedResponse_IsRepaired()
    {
        const string raw = """[{"title": "Begriffe", "columns": ["Begriff", "Definition"], "rows": [{"cells": ["Zelle", "Kleinste Einheit"]}, {"cells": ["Gewebe", "Verband gleichartiger Zel""";

        var result = DataTableService.AnalyzeResponse(raw, "Ersatz", Sources);

        Assert.That(result.Value, Is.Not.Null);
        Assert.That(result.WasRepaired, Is.True);
        Assert.That(result.Value!.Rows, Has.Count.EqualTo(1));
        Assert.That(result.Value.Rows[0].Cells[0], Is.EqualTo("Zelle"));
    }

    [Test]
    public void Analyze_NoUsableContent_ReportsReasons()
    {
        Assert.That(DataTableService.AnalyzeResponse("", "x").FailureReason, Is.EqualTo(MindmapFailureReason.EmptyResponse));
        Assert.That(DataTableService.AnalyzeResponse("Dazu steht nichts im Text.", "x").FailureReason, Is.EqualTo(MindmapFailureReason.NoJson));
        Assert.That(DataTableService.AnalyzeResponse("""[{"title": "Leer", "columns": ["A"], "rows": []}]""", "x").FailureReason, Is.EqualTo(MindmapFailureReason.NoTopics));
        Assert.That(DataTableService.AnalyzeResponse("""[{"title": "Leer", "columns": ["A"], "rows": [["", ""]]}]""", "x").Value, Is.Null);
    }

    [Test]
    public void Analyze_RespectsLimits()
    {
        var columns = string.Join(", ", Enumerable.Range(1, 12).Select(i => $"\"S{i}\""));
        var rows = string.Join(", ", Enumerable.Range(1, 100).Select(i => $"[\"Wert {i}\"]"));
        var raw = $"[{{\"title\": \"Groß\", \"columns\": [{columns}], \"rows\": [{rows}]}}]";

        var table = DataTableService.AnalyzeResponse(raw, "x").Value!;

        Assert.That(table.Columns, Has.Count.EqualTo(DataTableService.MaxColumns));
        Assert.That(table.Rows, Has.Count.EqualTo(DataTableService.MaxRows));
        Assert.That(table.Rows.All(r => r.Cells.Count == DataTableService.MaxColumns), Is.True);
    }

    [Test]
    public void Analyze_WithTargetColumns_MapsByNameOrPosition()
    {
        const string byName = """[{"columns": ["Definition", "Begriff"], "rows": [["Biokatalysator", "Enzym"]]}]""";
        const string byPosition = """[{"columns": ["Term", "Meaning"], "rows": [["Enzym", "Biokatalysator"]]}]""";
        var target = new[] { "Begriff", "Definition" };

        var a = DataTableService.AnalyzeResponse(byName, "x", Sources, target).Value!;
        var b = DataTableService.AnalyzeResponse(byPosition, "x", Sources, target).Value!;

        Assert.That(a.Columns, Is.EqualTo(target));
        Assert.That(a.Rows[0].Cells, Is.EqualTo(new[] { "Enzym", "Biokatalysator" }));
        Assert.That(b.Rows[0].Cells, Is.EqualTo(new[] { "Enzym", "Biokatalysator" }));
    }

    [Test]
    public void ResolveSource_MatchesKnownDocuments()
    {
        Assert.That(DataTableService.ResolveSource("SKRIPT BIOLOGIE.PDF", Sources), Is.EqualTo("Skript Biologie.pdf"));
        Assert.That(DataTableService.ResolveSource("[Quelle: notizen.txt]", Sources), Is.EqualTo("notizen.txt"));
        Assert.That(DataTableService.ResolveSource("aus Skript Biologie, Seite 3", Sources), Is.EqualTo("Skript Biologie.pdf"));
        Assert.That(DataTableService.ResolveSource("erfunden.pdf", Sources), Is.Empty);
        Assert.That(DataTableService.ResolveSource("", new[] { "einzig.txt" }), Is.EqualTo("einzig.txt"));
    }

    [Test]
    public void RemoveDuplicates_UsesFirstColumn_OrDateAndEventForTimeline()
    {
        var terms = Table(DataTableTemplate.Terms, new[] { "Begriff", "Definition" }, ("Enzym", "alt"));
        var newRows = new[] { Row("enzym!", "neu"), Row("Hormon", "x"), Row("Hormon", "y") };

        var kept = DataTableService.RemoveDuplicates(terms, newRows, terms.Rows, out var duplicates);

        Assert.That(kept.Select(r => r.Cells[0]), Is.EqualTo(new[] { "Hormon" }));
        Assert.That(duplicates, Is.EqualTo(2));

        var timeline = Table(DataTableTemplate.Timeline, new[] { "Datum", "Ereignis" }, ("1914", "Kriegsbeginn"));
        var sameYear = DataTableService.RemoveDuplicates(timeline, new[] { Row("1914", "Erster Flug"), Row("1914", "Kriegsbeginn") }, timeline.Rows, out var dup2);
        Assert.That(sameYear.Select(r => r.Cells[1]), Is.EqualTo(new[] { "Erster Flug" }));
        Assert.That(dup2, Is.EqualTo(1));
    }

    [Test]
    public void NormalizeColumnNames_NamesEmptyAndNumbersDuplicates()
    {
        Assert.That(DataTableService.NormalizeColumnNames(new[] { "Begriff", "", "begriff", " Wert " }),
            Is.EqualTo(new[] { "Begriff", "Spalte 2", "begriff 2", "Wert" }));
    }

    // ---------- Erzeugung ----------

    [Test]
    public async Task GenerateAsync_WithFakeAi_UsesColumnsAndSources()
    {
        var service = new DataTableService(new FakeLlmService(), _tempDir);
        var chunks = new[]
        {
            new SourceChunk("Skript Biologie.pdf", "Mitochondrien liefern der Zelle Energie in Form von ATP."),
            new SourceChunk("notizen.txt", "Chloroplasten betreiben Photosynthese in Pflanzenzellen.")
        };

        var result = await service.GenerateAsync(chunks, DataTableTemplate.Custom, "Zellorganellen", new[] { "Organell", "Aufgabe", "Vorkommen" }, "", "Ersatz");

        Assert.That(result.IsSuccess, Is.True, result.RawResponse);
        var table = result.Table!;
        Assert.That(table.Columns, Is.EqualTo(new[] { "Organell", "Aufgabe", "Vorkommen" }));
        Assert.That(table.Template, Is.EqualTo(DataTableTemplate.Custom));
        Assert.That(table.CustomInstruction, Is.EqualTo("Zellorganellen"));
        Assert.That(table.Rows, Is.Not.Empty);
        Assert.That(table.Rows.Select(r => r.Source).Distinct(), Is.EquivalentTo(new[] { "Skript Biologie.pdf", "notizen.txt" }));
        Assert.That(table.Rows.All(r => r.Cells[2] == string.Empty), Is.True, "Fake-KI erfindet nichts für weitere Spalten");
    }

    [Test]
    public async Task GenerateAsync_SendsLabeledChunks_AndReportsNoContent()
    {
        var llm = new Mock<ILlmService>();
        List<string>? sent = null;
        llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, CancellationToken>((_, c, _) => sent = c.ToList())
            .ReturnsAsync("""[{"columns": ["A"], "rows": [["x"]]}]""");
        var service = new DataTableService(llm.Object, _tempDir);

        var empty = await service.GenerateAsync(new[] { new SourceChunk("a.txt", "  ") }, DataTableTemplate.Terms, "", Array.Empty<string>(), "", "x");
        Assert.That(empty.FailureReason, Is.EqualTo(MindmapFailureReason.NoContent));

        var ok = await service.GenerateAsync(new[] { new SourceChunk("a.txt", "Inhalt") }, DataTableTemplate.Terms, "", Array.Empty<string>(), "", "x");
        Assert.That(ok.IsSuccess, Is.True);
        Assert.That(sent, Is.EqualTo(new[] { "[Quelle: a.txt]\nInhalt" }));
        Assert.That(ok.Table!.Rows[0].Source, Is.EqualTo("a.txt"), "einziges Dokument wird als Quelle eingetragen");
    }

    [Test]
    public void SelectChunks_WithTopic_PrefersMatchingText()
    {
        var chunks = Enumerable.Range(0, 60)
            .Select(i => new SourceChunk(i < 30 ? "a.txt" : "b.txt", i % 10 == 0 ? $"Abschnitt {i} über Enzyme" : $"Abschnitt {i} über Zellwände"))
            .ToList();

        var withTopic = DataTableService.SelectChunks(chunks, "Enzyme", 0);
        var spread = DataTableService.SelectChunks(chunks, "", 0);

        Assert.That(withTopic.Take(6), Has.All.Matches<SourceChunk>(c => c.Text.Contains("Enzyme")));
        Assert.That(spread, Has.Count.EqualTo(DataTableService.MaxChunks));
        Assert.That(spread.First().Text, Does.Contain("Abschnitt 0 "));
        Assert.That(spread.Last().Text, Does.Contain("Abschnitt 59"));
    }

    [Test]
    public async Task GenerateMoreRowsAsync_DropsDuplicates_AndKeepsColumns()
    {
        var table = Table(DataTableTemplate.Terms, new[] { "Begriff", "Definition" }, ("Enzym", "Biokatalysator"));
        table.SourceDocuments.Add("notizen.txt");
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""[{"columns": ["Begriff", "Definition", "Extra"], "rows": [["ENZYM", "doppelt", "x"], ["Hormon", "Botenstoff", "y"], ["Vitamin", "", ""]]}]""");
        var service = new DataTableService(llm.Object, _tempDir);

        var result = await service.GenerateMoreRowsAsync(table, new[] { new SourceChunk("notizen.txt", "Text") }, maxNewRows: 1);

        Assert.That(result.FailureReason, Is.EqualTo(MindmapFailureReason.None));
        Assert.That(result.DuplicatesSkipped, Is.EqualTo(1));
        Assert.That(result.Rows, Has.Count.EqualTo(1), "maxNewRows begrenzt");
        Assert.That(result.Rows[0].Cells, Is.EqualTo(new[] { "Hormon", "Botenstoff" }));
        Assert.That(result.Rows[0].Source, Is.EqualTo("notizen.txt"));
    }

    // ---------- Fake-KI ----------

    [Test]
    public async Task FakeAi_DataTablePromptWithOtherKeywords_ReturnsTable_ViaDispatcherAndFakeService()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync("Testprofil");
        storage.Setup(s => s.LoadAiConfigForProfileAsync("Testprofil")).ReturnsAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi });
        var dispatcher = new LlmServiceDispatcher(storage.Object, new HttpClient());

        // Eigene Vorgabe mit Wörtern anderer Bereiche: entscheidend ist nur die Kennzeile
        var prompt = StudyScope.Core.Common.TaskPrompt.Mark(StudyScope.Core.Common.TaskKind.DataTable,
            DataTableService.BuildPrompt(DataTableTemplate.Custom, "Mindmap, Quiz-Themen, Erstelle eine Präsentation, Lernkarteikarten", new[] { "Begriff", "Erklärung" }, null));
        var chunks = new List<string> { "[Quelle: a.txt]\nOsmose bezeichnet die Diffusion von Wasser durch eine Membran." };

        foreach (var llm in new ILlmService[] { dispatcher, new FakeLlmService() })
        {
            var raw = await llm.AskQuestionAsync(prompt, chunks);
            var table = DataTableService.AnalyzeResponse(raw, "x", new[] { "a.txt" }).Value;

            Assert.That(table, Is.Not.Null, raw);
            Assert.That(table!.Title, Does.Contain("Testmodus"));
            Assert.That(table.Columns, Is.EqualTo(new[] { "Begriff", "Erklärung" }));
            Assert.That(table.Rows[0].Source, Is.EqualTo("a.txt"));
            Assert.That(MindmapService.ParseMindmap(raw, "x")?.Title, Is.Not.EqualTo("[Testmodus] Mindmap"));
        }
    }

    // ---------- Speicherung ----------

    [Test]
    public async Task SaveAndLoad_RoundTrip_PerProfile_WithoutTempFile()
    {
        var service = new DataTableService(new FakeLlmService(), _tempDir);
        var library = new DataTableLibrary();
        library.Tables.Add(Table(DataTableTemplate.Terms, new[] { "Begriff", "Definition" }, ("Zelle", "Einheit des Lebens")));

        await service.SaveLibraryAsync("Testprofil", library);
        var loaded = await service.LoadLibraryAsync("Testprofil");
        var other = await service.LoadLibraryAsync("Anderes Profil");

        var folder = Path.Combine(_tempDir, "DataTables");
        Assert.That(File.Exists(Path.Combine(folder, "Testprofil.json")), Is.True);
        Assert.That(Directory.GetFiles(folder, "*.tmp"), Is.Empty);
        Assert.That(loaded.Tables, Has.Count.EqualTo(1));
        Assert.That(loaded.Tables[0].Rows[0].Cells, Is.EqualTo(new[] { "Zelle", "Einheit des Lebens" }));
        Assert.That(other.Tables, Is.Empty);
    }

    [Test]
    public async Task Load_RepairsMissingListsAndCellCounts_AndToleratesBrokenFile()
    {
        var folder = Path.Combine(_tempDir, "DataTables");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Testprofil.json"),
            """{"Tables": [{"Title": "Alt", "Columns": ["A", "B"], "Rows": [{"Cells": ["nur eins"]}, {"Cells": ["1", "2", "3"], "Source": null}], "SourceDocuments": null}]}""");
        await File.WriteAllTextAsync(Path.Combine(folder, "Kaputt.json"), "{ kein json");
        var service = new DataTableService(new FakeLlmService(), _tempDir);

        var loaded = await service.LoadLibraryAsync("Testprofil");
        var broken = await service.LoadLibraryAsync("Kaputt");

        var table = loaded.Tables.Single();
        Assert.That(table.Rows[0].Cells, Is.EqualTo(new[] { "nur eins", "" }));
        Assert.That(table.Rows[1].Cells, Is.EqualTo(new[] { "1", "2" }));
        Assert.That(table.Rows[1].Source, Is.Empty);
        Assert.That(table.SourceDocuments, Is.Empty);
        Assert.That(broken.Tables, Is.Empty);
    }

    // ---------- Hilfen ----------

    internal static DataTableDocument Table(DataTableTemplate template, string[] columns, params (string, string)[] rows)
    {
        var table = new DataTableDocument { Title = "Testtabelle", Template = template, Columns = columns.ToList() };
        foreach (var (a, b) in rows)
            table.Rows.Add(Row(a, b));
        return table;
    }

    internal static DataTableRow Row(params string[] cells) => new() { Cells = cells.ToList() };
}
