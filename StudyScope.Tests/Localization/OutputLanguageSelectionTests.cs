using System.Globalization;
using System.Text.RegularExpressions;
using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;
using StudyScope.ViewModels.DataTable;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Sprache der KI-Ergebnisse, Teil 3c-2: Auswahl in den Einstellungen, Exporte in der Sprache des Ergebnisses,
/// Kennzeichen „EN“, Rückfall-Titel in der Ergebnissprache, schärfere Sprachzeile, Fake-KI englisch für Bericht,
/// Datentabelle, Audio und Video.
/// </summary>
[TestFixture]
public class OutputLanguageSelectionTests
{
    private static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    private static readonly string[] GermanChunks =
    {
        "Die Photosynthese wandelt Lichtenergie in chemische Energie um. Sie findet in den Chloroplasten der Pflanzen statt, " +
        "und dabei wird aus Wasser und Kohlenstoffdioxid mit Hilfe des Lichts Zucker gebildet.",
        "Die Zellatmung setzt die gespeicherte Energie wieder frei. Dabei entsteht Kohlenstoffdioxid, das die Pflanze wieder nutzen kann.",
    };

    // ------------------------------------------------------------------ Exportsprache

    [Test, SetUICulture("de-DE")]
    public void ExportCulture_FollowsTheResult_OldDataFollowsTheUi()
    {
        Assert.That(ExportCulture.For((string?)null).Name, Is.EqualTo("de-DE"));
        Assert.That(ExportCulture.For("").Name, Is.EqualTo("de-DE"));
        Assert.That(ExportCulture.For("xx-unbekannt").Name, Is.EqualTo("de-DE"));
        Assert.That(ExportCulture.For("en").Name, Is.EqualTo("en"));
        // Gleiche Sprache wie die Oberfläche: deren Kultur samt Region bleibt
        Assert.That(ExportCulture.For("de").Name, Is.EqualTo("de-DE"));
    }

    [Test, SetUICulture("de-CH")]
    public void ExportCulture_SameLanguage_KeepsTheRegionOfTheUi() =>
        Assert.That(ExportCulture.For("de").Name, Is.EqualTo("de-CH"));

    [Test, SetUICulture("de-DE")]
    public void ExportCulture_Majority_TieOrNothingFollowsTheUi()
    {
        Assert.That(ExportCulture.ForMajority(new[] { "en", "en", "de", null, null, null }).Name, Is.EqualTo("en"));
        Assert.That(ExportCulture.ForMajority(new[] { "en", "de" }).Name, Is.EqualTo("de-DE"), "Gleichstand");
        Assert.That(ExportCulture.ForMajority(new string?[] { null, null }).Name, Is.EqualTo("de-DE"), "keine Angabe");
        Assert.That(ExportCulture.ForMajority(Array.Empty<string?>()).Name, Is.EqualTo("de-DE"));
    }

    [Test, SetUICulture("de-DE")]
    public void ExportCulture_Report_OwnLanguageFirst_ThenMajorityOfTheEmbeddedContent()
    {
        var studio = new ReportStudioContent
        {
            Flashcards = new[] { new Flashcard { Language = "en" }, new Flashcard { Language = "en" } },
            Mindmap = new MindmapDocument { Language = "de" }
        };
        Assert.That(ExportCulture.For(new Report { Language = "de" }, studio).Name, Is.EqualTo("de-DE"));
        Assert.That(ExportCulture.For(new Report { Language = null }, studio).Name, Is.EqualTo("en"));
        Assert.That(ExportCulture.For(new Report(), ReportStudioContent.Empty).Name, Is.EqualTo("de-DE"));
    }

    [Test, SetUICulture("de-DE")]
    public void FlashcardExport_UsesTheLanguageOfMostCards()
    {
        CultureInfo? used = null;
        var service = new Mock<IFlashcardService>();
        service.Setup(s => s.ExportToMarkdown(It.IsAny<string>(), It.IsAny<IEnumerable<Flashcard>>(), It.IsAny<CultureInfo>()))
            .Callback<string, IEnumerable<Flashcard>, CultureInfo>((_, _, c) => used = c)
            .Returns("# md");
        var vm = new FlashcardViewModel(service.Object, Mock.Of<IDocumentService>(), Mock.Of<IStorageService>());
        vm.Flashcards.Add(new Flashcard { Question = "Q1", Answer = "A1", Language = "en" });
        vm.Flashcards.Add(new Flashcard { Question = "Q2", Answer = "A2", Language = "en" });
        vm.Flashcards.Add(new Flashcard { Question = "F3", Answer = "A3", Language = "de" });

        vm.GetMarkdownExport("Testprofil");

        Assert.That(used?.Name, Is.EqualTo("en"));
    }

    [Test]
    public void ExportLanguage_WrittenFromTheResult_IntoTheFile()
    {
        // Ende-zu-Ende über den echten Dienst: englische Tabelle bei deutscher Oberfläche → Quellspalte „Source“
        using var _ = CultureScope.Use(C("de-DE"));
        var table = new DataTableDocument { Title = "Energy", Language = "en", Columns = { "Term" } };
        table.Rows.Add(new DataTableRow { Cells = { "ATP" }, Source = "Biologie.txt" });
        var service = new DataTableService(new FakeLlmService());
        var md = service.ExportToMarkdown(table, table.Rows, ExportCulture.For(table.Language));
        var mdOld = service.ExportToMarkdown(table, table.Rows, ExportCulture.For((string?)null));
        Assert.That(md, Does.Contain("Source").And.Not.Contain("Quelle"));
        Assert.That(mdOld, Does.Contain("Quelle"), "ältere Daten ohne Sprache: Oberfläche");
    }

    // ------------------------------------------------------------------ Kennzeichen „EN“

    [Test, SetUICulture("de-DE")]
    public void Badge_OnlyWhenTheLanguageDiffersFromTheUi()
    {
        Assert.That(ResultLanguageBadge.Text("en"), Is.EqualTo("EN"));
        Assert.That(ResultLanguageBadge.Text("de"), Is.Null);
        Assert.That(ResultLanguageBadge.Text(null), Is.Null, "ältere Daten ohne Angabe");
        Assert.That(ResultLanguageBadge.Text("kein-code"), Is.Null);
        Assert.That(ResultLanguageBadge.Tooltip("en"), Is.EqualTo("Sprache des Inhalts: English"));
        Assert.That(ResultLanguageBadge.Tooltip("de"), Is.Empty);
    }

    [Test, SetUICulture("en-GB")]
    public void Badge_EnglishUi_MarksGermanResults()
    {
        Assert.That(ResultLanguageBadge.Text("de"), Is.EqualTo("DE"));
        Assert.That(ResultLanguageBadge.Text("en"), Is.Null);
        Assert.That(ResultLanguageBadge.Tooltip("de"), Is.EqualTo("Content language: Deutsch"));
    }

    [Test]
    public void Badge_Majority()
    {
        Assert.That(ResultLanguageBadge.Majority(new[] { "en", "en", "de" }), Is.EqualTo("en"));
        Assert.That(ResultLanguageBadge.Majority(new[] { "en", "de" }), Is.Null);
        Assert.That(ResultLanguageBadge.Majority(new string?[] { null }), Is.Null);
    }

    [Test]
    public void QuizSession_KeepsTheLanguageOfEveryQuestion()
    {
        // Die Sitzung arbeitet mit Kopien (gemischte Antworten) – ohne Sprache fehlte das Kennzeichen an der Frage
        var topic = new QuizTopic { Name = "Testthema" };
        var questions = new[]
        {
            new QuizQuestion { Type = QuizQuestionType.SingleChoice, Text = "Q1", Options = { "a", "b", "c" }, CorrectIndices = { 0 }, Language = "en" },
            new QuizQuestion { Type = QuizQuestionType.TrueFalse, Text = "Q2", Options = { "True", "False" }, CorrectIndices = { 1 }, Language = "en" },
            new QuizQuestion { Type = QuizQuestionType.TrueFalse, Text = "F3", Options = { "Wahr", "Falsch" }, CorrectIndices = { 0 } }
        };
        var session = new StudyScope.ViewModels.Quiz.QuizSession(questions.Select(q => (q, topic)), QuizMode.Learn, new Random(1));
        Assert.That(session.Items.Select(i => i.Question.Language), Is.EqualTo(new[] { "en", "en", null }));
    }

    [Test]
    public void Badge_ShownInEveryResultList()
    {
        var root = RepoRoot();
        var views = new[] { "MindmapView", "PresentationView", "FlashcardView", "QuizView", "DataTableView", "ReportView", "AudioView", "VideoView" };
        foreach (var view in views)
        {
            var text = File.ReadAllText(Path.Combine(root, "StudyScope.UI", "Components", view + ".razor"));
            Assert.That(text, Does.Contain("<LanguageBadge "), view);
        }
    }

    // ------------------------------------------------------------------ Auswahl in den Einstellungen

    private static (SettingsViewModel Vm, Mock<IStorageService> Storage, Mock<ILlmService> Llm) Settings(string stored = "ui")
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync("Testprofil");
        storage.Setup(s => s.LoadOutputLanguageAsync("Testprofil")).ReturnsAsync(stored);
        var llm = new Mock<ILlmService>();
        var languages = new Mock<IUiLanguageService>();
        languages.SetupGet(l => l.AvailableCultures).Returns(new[] { "de", "en" });
        var vm = new SettingsViewModel(storage.Object, llm.Object, Mock.Of<ILocalLlmDiscoveryService>(), languages.Object);
        return (vm, storage, llm);
    }

    [Test, SetUICulture("de-DE")]
    public async Task Settings_LoadsTheProfileSetting_EvenWithoutAModel()
    {
        var (vm, _, _) = Settings("en");
        await vm.InitializeAsync();
        Assert.That(vm.OutputLanguage, Is.EqualTo("en"));
    }

    [Test, SetUICulture("de-DE")]
    public void Settings_Options_UiDocumentsAndEveryUiLanguageInItsOwnName()
    {
        var (vm, _, _) = Settings();
        Assert.That(vm.OutputLanguageOptions.Select(o => o.Value), Is.EqualTo(new[] { "ui", "documents", "de", "en" }));
        Assert.That(vm.OutputLanguageOptions.Select(o => o.Label), Is.EqualTo(new[] { "Wie Oberfläche", "Wie die Dokumente", "Deutsch", "English" }));
    }

    [Test, SetUICulture("de-DE")]
    public void Settings_DocumentsHint_NamesTheDetectableLanguages()
    {
        var (vm, _, _) = Settings();
        Assert.That(vm.OutputLanguageDocumentsHint, Is.Empty);
        vm.OutputLanguage = "documents";
        Assert.That(vm.OutputLanguageDocumentsHint, Does.Contain("Deutsch, English").And.Contain("Oberfläche"));
    }

    [Test, SetUICulture("de-DE")]
    public async Task Settings_ChangingTheLanguage_SavesAtOnce_EvenWhenTheConnectionFails()
    {
        var (vm, storage, llm) = Settings();
        llm.Setup(l => l.ValidateConnectionAsync(It.IsAny<AiConfig?>())).ReturnsAsync(false);
        await vm.InitializeAsync();
        Assert.That(vm.OutputLanguageStatus, Is.Empty);

        Assert.That(await vm.ChangeOutputLanguageAsync("en"), Is.True);
        storage.Verify(s => s.SaveOutputLanguageAsync("Testprofil", "en"), Times.Once);
        Assert.That(vm.OutputLanguageStatus, Is.EqualTo("✓ Gespeichert"));
        Assert.That(vm.OutputLanguageSaveFailed, Is.False);

        // „Speichern & Verbinden“ schlägt fehl – die Sprache bleibt gespeichert und wird nicht erneut geschrieben
        Assert.That(await vm.SaveAndConnectAsync(), Is.False);
        storage.Verify(s => s.SaveOutputLanguageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        storage.Verify(s => s.SaveAiConfigForProfileAsync(It.IsAny<string>(), It.IsAny<AiConfig>()), Times.Never);
    }

    [Test, SetUICulture("de-DE")]
    public async Task Settings_SaveAndConnect_DoesNotTouchTheLanguage()
    {
        var (vm, storage, llm) = Settings("en");
        llm.Setup(l => l.ValidateConnectionAsync(It.IsAny<AiConfig?>())).ReturnsAsync(true);
        await vm.InitializeAsync();
        Assert.That(await vm.SaveAndConnectAsync(), Is.True);
        storage.Verify(s => s.SaveOutputLanguageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test, SetUICulture("de-DE")]
    public async Task Settings_LanguageSaveError_IsShown()
    {
        var (vm, storage, _) = Settings();
        storage.Setup(s => s.SaveOutputLanguageAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new IOException("Datenträger voll"));
        Assert.That(await vm.ChangeOutputLanguageAsync("en"), Is.False);
        Assert.That(vm.OutputLanguageSaveFailed, Is.True);
        Assert.That(vm.OutputLanguageStatus, Does.Contain("Datenträger voll"));
    }

    [Test, SetUICulture("de-DE")]
    public async Task Settings_LoadingTheDialog_DoesNotSave()
    {
        var (vm, storage, _) = Settings("en");
        await vm.InitializeAsync();
        storage.Verify(s => s.SaveOutputLanguageAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ------------------------------------------------------------------ Nummerierung im Dialog KI-Modell

    [TestCase(AiProviderType.FakeAi, new[] { SettingsField.Provider, SettingsField.Model, SettingsField.OutputLanguage })]
    [TestCase(AiProviderType.OllamaLocal, new[] { SettingsField.Provider, SettingsField.Server, SettingsField.Model, SettingsField.OutputLanguage })]
    [TestCase(AiProviderType.LmStudioLocal, new[] { SettingsField.Provider, SettingsField.Server, SettingsField.Model, SettingsField.OutputLanguage })]
    [TestCase(AiProviderType.GoogleGemini, new[] { SettingsField.Provider, SettingsField.ApiKey, SettingsField.Model, SettingsField.OutputLanguage })]
    [TestCase(AiProviderType.OpenAiGpt4, new[] { SettingsField.Provider, SettingsField.ApiKey, SettingsField.Model, SettingsField.OutputLanguage })]
    [TestCase(AiProviderType.MistralAi, new[] { SettingsField.Provider, SettingsField.ApiKey, SettingsField.Model, SettingsField.OutputLanguage })]
    [TestCase(AiProviderType.CustomOpenAi, new[] { SettingsField.Provider, SettingsField.Server, SettingsField.ApiKey, SettingsField.Model, SettingsField.OutputLanguage })]
    public void Settings_Numbering_HasNoGaps(AiProviderType provider, SettingsField[] expected)
    {
        var (vm, _, _) = Settings();
        vm.SelectedProvider = provider;
        Assert.That(vm.VisibleFields, Is.EqualTo(expected));
        Assert.That(expected.Select(vm.FieldNumber), Is.EqualTo(Enumerable.Range(1, expected.Length)));
        foreach (var hidden in Enum.GetValues<SettingsField>().Except(expected))
            Assert.That(vm.FieldNumber(hidden), Is.Zero, hidden.ToString());
        Assert.That(vm.ShowsServer, Is.EqualTo(expected.Contains(SettingsField.Server)));
        Assert.That(vm.ShowsApiKey, Is.EqualTo(expected.Contains(SettingsField.ApiKey)));
    }

    [Test, SetUICulture("de-DE")]
    public void Settings_NumberedLabel_AndNoFixedNumbersInTheTexts()
    {
        var (vm, _, _) = Settings();
        Assert.That(vm.NumberedLabel(SettingsField.Model, "Modell auswählen:"), Is.EqualTo("2. Modell auswählen:"));
        vm.SelectedProvider = AiProviderType.CustomOpenAi;
        Assert.That(vm.NumberedLabel(SettingsField.OutputLanguage, "Sprache der KI-Ergebnisse:"), Is.EqualTo("5. Sprache der KI-Ergebnisse:"));

        // Die Nummern vergibt nur noch das ViewModel – kein Text des Dialogs beginnt mit einer festen Nummer
        var root = RepoRoot();
        foreach (var file in new[] { "UiStrings.resx", "UiStrings.en.resx" })
        {
            var text = File.ReadAllText(Path.Combine(root, "StudyScope.UI", "Resources", file));
            var numbered = Regex.Matches(text, "<data name=\"(Settings_[^\"]+)\" xml:space=\"preserve\"><value>\\d+\\. ").Select(m => m.Groups[1].Value);
            Assert.That(numbered, Is.Empty, file);
        }
        var razor = File.ReadAllText(Path.Combine(root, "StudyScope.UI", "Components", "SettingsModal.razor"));
        foreach (var field in Enum.GetValues<SettingsField>())
            Assert.That(razor, Does.Contain($"NumberedLabel(SettingsField.{field},"), field.ToString());
    }

    // ------------------------------------------------------------------ Quellspalte der Datentabelle in der Ansicht

    private static async Task<(DataTableViewModel Vm, string Dir)> TablesAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StudyScopeSourceLabel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var service = new DataTableService(new FakeLlmService(), dir);
        DataTableDocument Table(string id, string? language) => new()
        {
            Id = id, Title = id, Language = language, Columns = { "Begriff" },
            Rows =
            {
                new DataTableRow { Cells = { "ATP" }, Source = "zellbiologie.txt" },
                new DataTableRow { Cells = { "Chlorophyll" }, Source = "botanik.txt" }
            }
        };
        await service.SaveLibraryAsync("Testprofil", new DataTableLibrary { Tables = { Table("de", "de"), Table("en", "en"), Table("alt", null) } });
        var vm = new DataTableViewModel(service, Mock.Of<IDocumentService>());
        await vm.LoadForProfileAsync("Testprofil");
        return (vm, dir);
    }

    [TestCase("de-DE", "de", "Quelle")]
    [TestCase("de-DE", "en", "Source")]
    [TestCase("de-DE", "alt", "Quelle")]
    [TestCase("en-US", "de", "Quelle")]
    [TestCase("en-US", "en", "Source")]
    [TestCase("en-US", "alt", "Source")]
    public async Task DataTableView_SourceColumn_InTheLanguageOfTheTable(string ui, string table, string expected)
    {
        using var scope = CultureScope.Use(C(ui));
        var (vm, dir) = await TablesAsync();
        try
        {
            vm.SelectTable(table);
            Assert.That(vm.SourceColumnLabel, Is.EqualTo(expected));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test, SetUICulture("de-DE")]
    public async Task DataTableView_SourceColumn_FilterSortAndReservedNameStillWork()
    {
        var (vm, dir) = await TablesAsync();
        try
        {
            vm.SelectTable("en");
            Assert.That(vm.SourceColumnLabel, Is.EqualTo("Source"));

            vm.FilterText = "botanik";
            Assert.That(vm.VisibleRows.Select(r => r.Cells[0]), Is.EqualTo(new[] { "Chlorophyll" }));
            vm.FilterText = string.Empty;

            vm.ToggleSort(DataTableSorting.SourceColumn);
            Assert.That(vm.VisibleRows.Select(r => r.Source), Is.EqualTo(new[] { "botanik.txt", "zellbiologie.txt" }));

            Assert.That(vm.ValidateColumnName("Source"), Is.Not.Null, "englischer Name reserviert");
            Assert.That(vm.ValidateColumnName("Quelle"), Is.Not.Null, "deutscher Name reserviert");
            Assert.That(vm.ValidateColumnName("Herkunft"), Is.Null);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void DataTableView_UsesTheLabelOfTheTable_NotTheUiName()
    {
        var razor = File.ReadAllText(Path.Combine(RepoRoot(), "StudyScope.UI", "Components", "DataTableView.razor"));
        Assert.That(razor, Does.Contain("@DtVm.SourceColumnLabel"));
        // Der Formularhinweis (noch keine Tabelle) nennt den Namen weiter in der Oberflächensprache
        Assert.That(Regex.Matches(razor, @"DataTableDocument\.SourceColumnName\b").Count, Is.EqualTo(1));
    }

    [Test]
    public void Settings_UnknownValue_FallsBackToUi()
    {
        var (vm, _, _) = Settings();
        vm.OutputLanguage = "klingonisch";
        Assert.That(vm.OutputLanguage, Is.EqualTo("ui"));
    }

    // ------------------------------------------------------------------ Rückfall-Titel

    [Test, SetUICulture("de-DE")]
    public void FallbackTitles_InTheResultLanguage_NotTheUi()
    {
        var one = new[] { new DocumentItem { FileName = "Skript.pdf" } };
        var two = new[] { new DocumentItem { FileName = "a.pdf" }, new DocumentItem { FileName = "b.pdf" } };

        Assert.That(FallbackTitles.Titled(C("en"), () => TemplateTexts.Name(ReportTemplate.Glossary), one, "Testprofil"), Is.EqualTo("Glossary: Skript"));
        Assert.That(FallbackTitles.Titled(C("de"), () => TemplateTexts.Name(ReportTemplate.Glossary), two, "Standard"), Is.EqualTo("Glossar: Standard"));
        Assert.That(FallbackTitles.Titled(C("en"), () => TemplateTexts.Name(ReportTemplate.Glossary), two, "Standard"), Is.EqualTo("Glossary: Default"));
        Assert.That(FallbackTitles.Documents(C("en"), two, "Testprofil"), Is.EqualTo("Documents: Testprofil"));
        Assert.That(FallbackTitles.Documents(C("de"), two, "Testprofil"), Is.EqualTo("Unterlagen: Testprofil"));
        Assert.That(FallbackTitles.Documents(C("en"), one, "Testprofil"), Is.EqualTo("Skript"));
        Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo("de-DE"), "Kultur danach zurückgestellt");
    }

    [Test, SetUICulture("de-DE")]
    public void FallbackTitles_LanguageResolvedLikeTheServices()
    {
        Assert.That(FallbackTitles.Language("en", GermanChunks).Name, Is.EqualTo("en"));
        Assert.That(FallbackTitles.Language("ui", GermanChunks).Name, Is.EqualTo("de"));
    }

    [Test]
    public void ViewModels_BuildNoFallbackTitleFromFixedGerman()
    {
        var root = RepoRoot();
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "StudyScope.ViewModels"), "*ViewModel*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (var bad in new[] { "$\"Unterlagen: ", "\"Videoübersicht\"", ".DisplayName(Template)}: " })
                if (text.Contains(bad, StringComparison.Ordinal)) problems.Add($"{Path.GetFileName(file)}: {bad}");
        }
        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    // ------------------------------------------------------------------ Sprachzeile

    [Test]
    public void PromptLine_AsksToTranslateTechnicalTerms_AndStillParses()
    {
        var line = OutputLanguage.PromptLine(C("en"));
        Assert.That(line, Does.Contain("Fachbegriffe").And.Contain("übersetzen").And.Contain("Klammern").And.Contain("Eigennamen"));
        Assert.That(Regex.Matches(line, @"\(").Count, Is.EqualTo(1), "nur die Sprachangabe steht in Klammern");
        Assert.That(OutputLanguage.TryParse("Frage\n\n" + line)?.Name, Is.EqualTo("en"));
        Assert.That(OutputLanguage.WithoutLine("Frage\n\n" + line), Is.EqualTo("Frage"));
    }

    [Test]
    public void PromptLine_DoesNotTriggerOtherFakeAiTasks()
    {
        // Die Fake-KI erkennt Aufträge an deutschen Merkmalen – die Regel darf keins davon enthalten
        var line = OutputLanguage.PromptLine(C("en"));
        foreach (var marker in new[] { "JSON", "Mindmap", "Lernkarteikarten", "Quiz", "Erstelle", "Präsentation", "Bericht", "Datentabelle" })
            Assert.That(line, Does.Not.Contain(marker), marker);
    }

    // ------------------------------------------------------------------ Fake-KI englisch (Rest aus 3c-1)

    private static IEnumerable<SourceChunk> Sources() => GermanChunks.Select(t => new SourceChunk("Biologie.txt", t));

    [Test]
    public void FakeAi_ReportDataTableAudioVideo_InEnglish_WhenAsked()
    {
        var fake = new FakeLlmService();

        var report = new ReportService(fake).GenerateReportAsync(GermanChunks, ReportTemplate.Glossary, "", "Energie", "en").GetAwaiter().GetResult().Report;
        Assert.That(report?.Title, Is.EqualTo("[Test mode] Glossary"));
        Assert.That(report!.Language, Is.EqualTo("en"));
        Assert.That(report.Sections.Select(s => s.Heading), Has.All.StartWith("Section "));

        var table = new DataTableService(fake).GenerateAsync(Sources(), DataTableTemplate.Custom, "", Array.Empty<string>(), "", "Energie", "en")
            .GetAwaiter().GetResult().Table;
        Assert.That(table?.Title, Is.EqualTo("[Test mode] Data table"));
        Assert.That(table!.Language, Is.EqualTo("en"));

        var audio = new AudioScriptService(fake).GenerateAsync(Sources(), AudioScriptFormat.Podcast, AudioScriptLength.Short, "", "Energie", "en")
            .GetAwaiter().GetResult().Summary;
        Assert.That(audio?.Title, Is.EqualTo("Test mode: podcast about your documents"));
        Assert.That(audio!.Segments[0].Text, Does.StartWith("Welcome!"));
        Assert.That(audio.Segments.Select(s => s.Speaker).Distinct(), Is.SubsetOf(new[] { "Moderatorin", "Experte" }), "gespeicherte Rollen bleiben");

        var video = new VideoScriptService(fake).GenerateAsync(new PresentationDocument
        {
            Title = "Energie",
            Slides = { new PresentationSlide { Title = "Photosynthese", Bullets = { "Licht wird zu Energie" } }, new PresentationSlide { Title = "Zellatmung" } }
        }, "Energie", "en").GetAwaiter().GetResult().Video;
        Assert.That(video?.Scenes[0].Narration, Does.StartWith("Welcome to this explainer video"));
        Assert.That(video!.Scenes[1].Narration, Does.StartWith("This slide is about"));
    }

    [Test]
    public void FakeAi_ReportDataTableAudioVideo_German_Unchanged()
    {
        var fake = new FakeLlmService();
        var report = new ReportService(fake).GenerateReportAsync(GermanChunks, ReportTemplate.Glossary, "", "Energie", "de").GetAwaiter().GetResult().Report;
        Assert.That(report?.Title, Is.EqualTo("[Testmodus] Glossar"));
        var audio = new AudioScriptService(fake).GenerateAsync(Sources(), AudioScriptFormat.Podcast, AudioScriptLength.Short, "", "Energie", "de")
            .GetAwaiter().GetResult().Summary;
        Assert.That(audio?.Title, Is.EqualTo("Testmodus: Podcast zu Ihren Unterlagen"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StudyScope.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("StudyScope.slnx nicht gefunden");
    }
}
