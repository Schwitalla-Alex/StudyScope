using System.Globalization;
using System.Text.RegularExpressions;
using Moq;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Sprache der KI-Ergebnisse (Teil 3c-1): Einstellung, Erkennung der Dokumentsprache, die eine Sprachzeile im Prompt,
/// feste Inhalte und gespeicherte Sprache am Ergebnis, Chat-Systemanweisung, Fake-KI auf Englisch.
/// </summary>
[TestFixture]
public class OutputLanguageTests
{
    private static readonly string[] DeEn = { "de", "en" };
    private static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    private static readonly string[] GermanChunks =
    {
        "Die Photosynthese wandelt Lichtenergie in chemische Energie um. Sie findet in den Chloroplasten der Pflanzen statt, " +
        "und dabei wird aus Wasser und Kohlenstoffdioxid mit Hilfe des Lichts Zucker gebildet.",
        "Die Zellatmung setzt die gespeicherte Energie wieder frei. Dabei entsteht Kohlenstoffdioxid, das die Pflanze wieder nutzen kann.",
    };

    private static readonly string[] EnglishChunks =
    {
        "Photosynthesis converts light energy into chemical energy. It takes place in the chloroplasts of plants, " +
        "where sugar is built from water and carbon dioxide with the help of light.",
        "Cellular respiration releases the stored energy again. This produces carbon dioxide, which the plant can use again.",
    };

    // ------------------------------------------------------------------ Einstellung und Erkennung

    [TestCase(null, "ui")]
    [TestCase("", "ui")]
    [TestCase("UI", "ui")]
    [TestCase("Documents", "documents")]
    [TestCase("en-GB", "en")]
    [TestCase("de", "de")]
    [TestCase("kein-gueltiger-name", "ui")]
    public void Normalize(string? value, string expected) => Assert.That(OutputLanguage.Normalize(value), Is.EqualTo(expected));

    [Test]
    public void LanguageGuess_RecognisesGermanAndEnglish()
    {
        Assert.That(LanguageGuess.Guess(GermanChunks), Is.EqualTo("de"));
        Assert.That(LanguageGuess.Guess(EnglishChunks), Is.EqualTo("en"));
        Assert.That(LanguageGuess.Languages, Is.SupersetOf(DeEn));
    }

    [Test]
    public void LanguageGuess_TooLittleOrMixedText_IsUnsure()
    {
        Assert.That(LanguageGuess.Guess(new[] { "Photosynthese Chloroplast" }), Is.Null, "zu wenig Text");
        Assert.That(LanguageGuess.Guess(new[] { "Die Zelle und der Kern.", "The cell and the nucleus." }), Is.Null, "zu wenige eindeutige Wörter");
        // Gemischte Unterlagen: Es zählt die Mehrheit (Entscheidung D)
        Assert.That(LanguageGuess.Guess(GermanChunks.Concat(GermanChunks).Concat(EnglishChunks.Take(1))), Is.EqualTo("de"));
        Assert.That(LanguageGuess.Guess(Array.Empty<string>()), Is.Null);
    }

    [TestCase("ui", "de-AT", "de")]
    [TestCase("ui", "en-GB", "en")]
    [TestCase("en", "de-DE", "en")]
    [TestCase("de", "en-US", "de")]
    [TestCase("fr", "de-DE", "de")]   // nur Sprachen der Oberfläche wählbar
    public void Resolve_FixedAndUi(string setting, string ui, string expected) =>
        Assert.That(OutputLanguage.Resolve(setting, C(ui), GermanChunks, DeEn).Name, Is.EqualTo(expected));

    [Test]
    public void Resolve_Documents_FollowsTheExcerpts_UnsureFallsBackToUi()
    {
        Assert.That(OutputLanguage.Resolve("documents", C("de-DE"), EnglishChunks, DeEn).Name, Is.EqualTo("en"));
        Assert.That(OutputLanguage.Resolve("documents", C("en-US"), GermanChunks, DeEn).Name, Is.EqualTo("de"));
        Assert.That(OutputLanguage.Resolve("documents", C("en-US"), new[] { "Kurz." }, DeEn).Name, Is.EqualTo("en"), "unsicher → Oberfläche");
        // Französische Unterlagen: Französisch ist (noch) keine Sprache der Oberfläche → Oberflächensprache
        var french = new[] { "La photosynthèse transforme l'énergie lumineuse en énergie chimique. Elle a lieu dans les chloroplastes des plantes et elle est très importante pour la vie." };
        Assert.That(OutputLanguage.Resolve("documents", C("de-DE"), french, DeEn).Name, Is.EqualTo("de"));
    }

    // ------------------------------------------------------------------ Sprachzeile

    [Test]
    public void PromptLine_GermanAndOwnName_RoundTrip()
    {
        var en = OutputLanguage.PromptLine(C("en"));
        var de = OutputLanguage.PromptLine(C("de"));
        Assert.That(en, Does.StartWith("Sprache der Antwort: Englisch (English)."));
        Assert.That(de, Does.StartWith("Sprache der Antwort: Deutsch (de)."));
        Assert.That(OutputLanguage.TryParse("Erkläre X.\n\n" + en)?.Name, Is.EqualTo("en"));
        Assert.That(OutputLanguage.TryParse("- " + de + "\n")?.Name, Is.EqualTo("de"));
        Assert.That(OutputLanguage.TryParse("Wie geht das?"), Is.Null);
        Assert.That(OutputLanguage.WithoutLine("Erkläre X.\n\n" + en), Is.EqualTo("Erkläre X."));
    }

    [Test]
    public void ApplyToPrompt_ReplacesTheFixedGerman_WithExactlyOneLine()
    {
        var prompt = ResultLanguage.ApplyToPrompt("Regeln:\n- Nur Inhalte aus den Auszügen. Sprache: Deutsch.\n- Natürliches, gesprochenes Deutsch.", C("en"));
        Assert.That(prompt, Does.Not.Contain("Sprache: Deutsch").And.Not.Contain("gesprochenes Deutsch"));
        Assert.That(Regex.Matches(prompt, "Sprache der Antwort:").Count, Is.EqualTo(1));
        Assert.That(prompt, Does.EndWith(OutputLanguage.PromptLine(C("en")) + "\n"));
    }

    // ------------------------------------------------------------------ Dienste: Sprachzeile, Routing der Fake-KI, Sprache am Ergebnis

    private static (string Prompt, T Result) Capture<T>(Func<ILlmService, Task<T>> run, string answer = "[]")
    {
        string? prompt = null;
        var llm = new Mock<ILlmService>();
        llm.Setup(m => m.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, CancellationToken>((p, _, _) => prompt ??= p)
            .ReturnsAsync(answer);
        var result = run(llm.Object).GetAwaiter().GetResult();
        Assert.That(prompt, Is.Not.Null, "kein Prompt abgefangen");
        return (prompt!, result);
    }

    private static IEnumerable<SourceChunk> Sources(IEnumerable<string> texts) => texts.Select(t => new SourceChunk("Biologie.txt", t));

    private static Dictionary<string, string> EnglishPrompts() => new()
    {
        ["Karteikarten"] = Capture(llm => new FlashcardService(llm).GenerateFlashcardsAsync(GermanChunks, 5, "en")).Prompt,
        ["Mindmap"] = Capture(llm => new MindmapService(llm).GenerateMindmapAsync(GermanChunks, "Energie", "en")).Prompt,
        ["Präsentation"] = Capture(llm => new PresentationService(llm).GenerateFromDocumentsAsync(GermanChunks, "Energie", 6, "en")).Prompt,
        ["Quiz-Themen"] = Capture(llm => new QuizService(llm).SuggestTopicsAsync(GermanChunks, "en")).Prompt,
        ["Quizfragen"] = Capture(llm => new QuizService(llm).GenerateQuestionsAsync(new QuizTopic { Name = "Energie" }, GermanChunks, 5,
            Enum.GetValues<QuizQuestionType>(), 0, "en")).Prompt,
        ["Bericht"] = Capture(llm => new ReportService(llm).GenerateReportAsync(GermanChunks, ReportTemplate.StudyGuide, "", "Energie", "en")).Prompt,
        ["Datentabelle"] = Capture(llm => new DataTableService(llm).GenerateAsync(Sources(GermanChunks), DataTableTemplate.Terms, "", Array.Empty<string>(), "", "Energie", "en")).Prompt,
        ["Audio"] = Capture(llm => new AudioScriptService(llm).GenerateAsync(Sources(GermanChunks), AudioScriptFormat.Podcast, AudioScriptLength.Short, "", "Energie", "en")).Prompt,
        ["Video"] = Capture(llm => new VideoScriptService(llm).GenerateAsync(new PresentationDocument
        {
            Title = "Energie", Slides = { new PresentationSlide { Title = "Photosynthese", Bullets = { "Licht wird zu Energie" } } }
        }, "Energie", "en")).Prompt,
    };

    [Test]
    public void EveryService_HasExactlyOneEnglishLanguageLine_AndNoFixedGerman()
    {
        var prompts = EnglishPrompts();
        Assert.That(prompts, Has.Count.EqualTo(9));
        foreach (var (name, prompt) in prompts)
        {
            Assert.That(Regex.Matches(prompt, "Sprache der Antwort:").Count, Is.EqualTo(1), name);
            Assert.That(OutputLanguage.TryParse(prompt)?.Name, Is.EqualTo("en"), name);
            Assert.That(prompt, Does.Not.Contain("Sprache: Deutsch").And.Not.Contain("gesprochenes Deutsch"), name);
        }
    }

    [Test]
    public void FakeAi_RecognisesEveryTask_InEveryResultLanguage()
    {
        // Gleicher Zweig der Fake-KI: Die Antwort hat dieselbe Form (JSON-Array bzw. -Objekt) wie beim deutschen Prompt
        var fake = new FakeLlmService();
        foreach (var (name, englishPrompt) in EnglishPrompts())
        {
            var germanPrompt = englishPrompt.Replace(OutputLanguage.PromptLine(C("en")), OutputLanguage.PromptLine(C("de")));
            var en = fake.AskQuestionAsync(englishPrompt, GermanChunks).GetAwaiter().GetResult().TrimStart();
            var de = fake.AskQuestionAsync(germanPrompt, GermanChunks).GetAwaiter().GetResult().TrimStart();
            Assert.That(en[0], Is.EqualTo(de[0]), $"{name}: anderer Zweig der Fake-KI");
            Assert.That(en, Does.Not.StartWith("[Test"), name);
        }
    }

    [Test]
    public void FakeAi_AnswersInEnglish_WhenAsked()
    {
        var fake = new FakeLlmService();
        var line = OutputLanguage.PromptLine(C("en"));
        var chat = fake.AskQuestionAsync("What is photosynthesis?\n\n" + line, GermanChunks).GetAwaiter().GetResult();
        Assert.That(chat, Does.StartWith("[Test mode] Answer based on 2 excerpts.").And.Contain("Question: What is photosynthesis?"));
        Assert.That(chat, Does.Not.Contain("Sprache der Antwort"), "Frage ohne Sprachzeile wiedergegeben");

        var cards = new FlashcardService(fake).GenerateFlashcardsAsync(GermanChunks, 5, "en").GetAwaiter().GetResult();
        Assert.That(cards.Select(c => c.Question), Has.Some.EqualTo("What does the excerpt say?"));
        Assert.That(cards.Select(c => c.Language), Has.All.EqualTo("en"));

        var quiz = new QuizService(fake).GenerateQuestionsAsync(new QuizTopic { Name = "Energie" }, GermanChunks, 3,
            Enum.GetValues<QuizQuestionType>(), 0, "en").GetAwaiter().GetResult();
        Assert.That(quiz.Questions, Is.Not.Empty);
        Assert.That(quiz.Questions.Select(q => q.Language), Has.All.EqualTo("en"));
        Assert.That(quiz.Questions.Where(q => q.Type == QuizQuestionType.TrueFalse).SelectMany(q => q.Options), Is.SubsetOf(new[] { "True", "False" }));
        Assert.That(quiz.Questions.First().Text, Does.StartWith("[Test mode"));
    }

    [Test, SetUICulture("de-DE")]
    public void FakeAi_German_Unchanged()
    {
        var fake = new FakeLlmService();
        var chat = fake.AskQuestionAsync("Was ist Photosynthese?", GermanChunks).GetAwaiter().GetResult();
        Assert.That(chat, Does.StartWith("[Testmodus] Antwort basierend auf 2 Textstellen.\nFrage: Was ist Photosynthese?"));
        var quiz = new QuizService(fake).GenerateQuestionsAsync(new QuizTopic { Name = "Energie" }, GermanChunks, 3,
            Enum.GetValues<QuizQuestionType>(), 0).GetAwaiter().GetResult();
        Assert.That(quiz.Questions.Where(q => q.Type == QuizQuestionType.TrueFalse).SelectMany(q => q.Options), Is.SubsetOf(new[] { "Wahr", "Falsch" }));
        Assert.That(quiz.Questions.Select(q => q.Language), Has.All.EqualTo("de"));
    }

    [Test]
    public void Results_StoreTheirLanguage_AndFixedContentInIt()
    {
        var fake = new FakeLlmService();
        var mindmap = new MindmapService(fake).GenerateMindmapAsync(GermanChunks, "", "en").GetAwaiter().GetResult();
        Assert.That(mindmap.Language, Is.EqualTo("en"));
        Assert.That(mindmap.Root!.Title, Is.EqualTo("[Test mode] Mind map"));

        var presentation = new PresentationService(fake).GenerateFromDocumentsAsync(GermanChunks, "", 4, "en").GetAwaiter().GetResult();
        Assert.That(presentation.Presentation!.Language, Is.EqualTo("en"));

        var report = new ReportService(fake).GenerateReportAsync(GermanChunks, ReportTemplate.StudyGuide, "", "Energie", "en").GetAwaiter().GetResult();
        Assert.That(report.Report!.Language, Is.EqualTo("en"));

        var table = new DataTableService(fake).GenerateAsync(Sources(GermanChunks), DataTableTemplate.Terms, "", Array.Empty<string>(), "", "Energie", "en").GetAwaiter().GetResult();
        Assert.That(table.Table!.Language, Is.EqualTo("en"));

        var audio = new AudioScriptService(fake).GenerateAsync(Sources(GermanChunks), AudioScriptFormat.Summary, AudioScriptLength.Short, "", "Energie", "en").GetAwaiter().GetResult();
        Assert.That(audio.Summary!.Language, Is.EqualTo("en"));
    }

    [Test]
    public void PresentationFromMindmap_UsesTheMindmapLanguage()
    {
        var mindmap = new MindmapDocument
        {
            Language = "en",
            Root = new MindmapNode
            {
                Title = "Energy", Summary = "How cells use energy.",
                Children = { new MindmapNode { Title = "Photosynthesis" }, new MindmapNode { Title = "Respiration" } }
            }
        };
        var presentation = new PresentationService(new FakeLlmService()).CreateFromMindmap(mindmap);
        Assert.That(presentation.Language, Is.EqualTo("en"));
        var titles = presentation.Slides.Select(s => s.Title).ToList();
        Assert.That(titles, Does.Contain("Overview").And.Contain("Summary"));
        Assert.That(presentation.Slides.First(s => s.Title == "Overview").Notes, Is.EqualTo("This presentation covers the following topics: Photosynthesis, Respiration."));

        mindmap.Language = null; // ältere Mindmap: Oberflächensprache (Tests: Deutsch)
        using var scope = CultureScope.Use(C("de-DE"));
        Assert.That(new PresentationService(new FakeLlmService()).CreateFromMindmap(mindmap).Slides.Select(s => s.Title), Does.Contain("Überblick"));
    }

    [Test]
    public void Appending_KeepsTheLanguageOfTheExistingResult()
    {
        // Datentabelle: weitere Zeilen in der Sprache der Tabelle, auch wenn die Einstellung Deutsch ist
        var table = new DataTableDocument { Title = "Terms", Language = "en", Columns = { "Term", "Definition" } };
        var (prompt, _) = Capture(llm => new DataTableService(llm).GenerateMoreRowsAsync(table, Sources(GermanChunks), 10, "de"));
        Assert.That(OutputLanguage.TryParse(prompt)?.Name, Is.EqualTo("en"));

        // Quiz: weitere Fragen eines Themas in der Sprache der vorhandenen Fragen
        var topic = new QuizTopic { Name = "Energy", Questions = { new QuizQuestion { Text = "What is ATP?", Language = "en" } } };
        var (quizPrompt, _) = Capture(llm => new QuizService(llm).GenerateQuestionsAsync(topic, GermanChunks, 5, Enum.GetValues<QuizQuestionType>(), 1, "de"));
        Assert.That(OutputLanguage.TryParse(quizPrompt)?.Name, Is.EqualTo("en"));

        // Video aus einer englischen Präsentation: Sprechtexte englisch
        var slides = new PresentationDocument { Title = "Energy", Language = "en", Slides = { new PresentationSlide { Title = "Photosynthesis" } } };
        var (videoPrompt, _) = Capture(llm => new VideoScriptService(llm).GenerateAsync(slides, "Energy", "de"));
        Assert.That(OutputLanguage.TryParse(videoPrompt)?.Name, Is.EqualTo("en"));

        // Karteikarten: Mehrheit des vorhandenen Stapels
        Assert.That(FlashcardViewModel.DominantLanguage(new[] { "en", "en", "de", null }), Is.EqualTo("en"));
        Assert.That(FlashcardViewModel.DominantLanguage(new[] { "en", "de" }), Is.Null, "Gleichstand → Einstellung");
        Assert.That(FlashcardViewModel.DominantLanguage(new string?[] { null }), Is.Null);
    }

    // ------------------------------------------------------------------ Chat

    [Test]
    public void ChatSystemPrompt_FollowsTheLanguageLine()
    {
        Assert.That(LlmServiceDispatcher.RagSystemPromptFor(null), Is.EqualTo(LlmServiceDispatcher.RagSystemPrompt));
        Assert.That(LlmServiceDispatcher.RagSystemPromptFor(C("de")), Is.EqualTo(LlmServiceDispatcher.RagSystemPrompt));

        var en = LlmServiceDispatcher.RagSystemPromptFor(C("en"));
        Assert.That(en, Does.Contain("„Your documents do not contain any information on this.“"));
        Assert.That(en, Does.Contain("Sprache der Antwort: Englisch (English)."));
        Assert.That(en, Does.Not.Contain("auf Deutsch"));
        Assert.That(en, Does.Contain("ausschließlich").And.Contain("erfinde nichts"), "Regeln bleiben");
    }

    [Test]
    public void FallbackSentence_IsNotEvaluatedAnywhere()
    {
        // „Dazu enthalten Ihre Unterlagen keine Angaben.“ ist nur ein Beispiel in der Systemanweisung. Würde der Satz irgendwo
        // ausgewertet, müsste auch die englische Fassung erkannt werden – dieser Test meldet eine neue Auswertung.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StudyScope.slnx"))) root = root.Parent;
        var hits = new[] { "StudyScope.UI", "StudyScope.ViewModels", "StudyScope.Core", "StudyScope.Infrastructure" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(root!.FullName, p), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor") || f.EndsWith(".js"))
                        && !f.Split(Path.DirectorySeparatorChar).Any(d => d is "bin" or "obj"))
            .Where(f => File.ReadAllText(f).Contains("keine Angaben"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.That(hits, Is.EquivalentTo(new[] { "LlmServiceDispatcher.cs" }));
    }

    [Test]
    public void ChatViewModel_AddsTheLineOnlyForOtherLanguages()
    {
        Assert.That(ChatViewModel.WithLanguageLine("Was ist ATP?", C("de")), Is.EqualTo("Was ist ATP?"));
        var en = ChatViewModel.WithLanguageLine("What is ATP? ", C("en"));
        Assert.That(en, Is.EqualTo("What is ATP?\n\n" + OutputLanguage.PromptLine(C("en"))));
    }

    // ------------------------------------------------------------------ Speicherung

    [Test]
    public async Task Storage_ProfileThenGlobalThenUi()
    {
        var dir = Path.Combine(Path.GetTempPath(), "StudyScopeOutputLanguage_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new JsonStorageService(dir);
            await storage.SaveDocumentsAsync("Testprofil", new List<DocumentItem>());
            Assert.That(await storage.LoadOutputLanguageAsync("Testprofil"), Is.EqualTo("ui"), "nichts eingestellt");

            await storage.SaveOutputLanguageAsync("Testprofil", "en");
            Assert.That(await storage.LoadOutputLanguageAsync("Testprofil"), Is.EqualTo("en"));
            Assert.That(await storage.LoadOutputLanguageAsync("Anderes"), Is.EqualTo("en"), "global als Rückfall");

            await storage.SaveOutputLanguageAsync("Anderes", "documents");
            Assert.That(await storage.LoadOutputLanguageAsync("Testprofil"), Is.EqualTo("en"), "Profil hat Vorrang");

            // Andere Oberflächen-Einstellungen behalten das Feld
            var ui = await storage.LoadUiSettingsAsync();
            ui.SidebarCollapsed = true;
            await storage.SaveUiSettingsAsync(ui);
            Assert.That((await storage.LoadUiSettingsAsync()).OutputLanguage, Is.EqualTo("documents"));
            await storage.SaveDocumentsAsync("Testprofil", new List<DocumentItem>());
            Assert.That(await storage.LoadOutputLanguageAsync("Testprofil"), Is.EqualTo("en"), "Speichern der Dokumente behält die Einstellung");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Test]
    public void OldData_WithoutLanguage_StaysUnknown()
    {
        var json = "{\"Root\":{\"Title\":\"Alt\"},\"SourceDocuments\":[]}";
        var doc = System.Text.Json.JsonSerializer.Deserialize<MindmapDocument>(json)!;
        Assert.That(doc.Language, Is.Null);
    }
}
