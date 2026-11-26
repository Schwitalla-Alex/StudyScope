using System.Globalization;
using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Localization;

/// <summary>
/// Die Prompts an die KI bleiben in jeder Oberflächensprache deutsch und Zeichen für Zeichen gleich: Die Fake-KI und
/// die Dienste erkennen Aufträge an deutschen Merkmalen („JSON-Array“, „Lernkarteikarten“, „Erstelle eine Datentabelle“,
/// „Format: Podcast-Gespräch“ …). Wer einen Anzeigetext in einen Prompt übernimmt, bekommt hier einen Fehler.
/// Die Sprache der KI-Ergebnisse kommt erst mit Phase 3c und nur über eine eigene Sprachzeile hinzu.
/// </summary>
[TestFixture]
public class PromptCultureTests
{
    private static readonly string[] Chunks =
    {
        "Die Photosynthese wandelt Lichtenergie in chemische Energie um. Sie findet in den Chloroplasten statt.",
        "Die Zellatmung setzt die gespeicherte Energie wieder frei. Dabei entsteht Kohlenstoffdioxid.",
    };

    private static readonly string[] OtherCultures = { "en-US", "fr-FR", "ar-SA" };

    private static readonly System.Text.RegularExpressions.Regex RandomTag = new(@"#\d+");

    [Test]
    public void AllPrompts_AreIdenticalInEveryUiCulture()
    {
        var german = BuildAll("de-DE");
        Assert.That(german, Has.Count.GreaterThan(20), "zu wenige Prompts erfasst");

        foreach (var culture in OtherCultures)
        {
            var other = BuildAll(culture);
            Assert.That(other.Keys, Is.EquivalentTo(german.Keys), culture);
            foreach (var (name, prompt) in german)
                Assert.That(other[name], Is.EqualTo(prompt), $"Prompt „{name}“ weicht unter {culture} ab");
        }
    }

    [Test]
    public void FakeAi_AnswersIdenticallyInEveryUiCulture()
    {
        // Gleiche Antwort = gleicher Zweig der Fake-KI (Karteikarten, Mindmap, Quiz, Bericht, Datentabelle, Audio, Video …)
        var german = AnswerAll("de-DE");
        foreach (var culture in OtherCultures)
        {
            var other = AnswerAll(culture);
            foreach (var (name, answer) in german)
                Assert.That(other[name], Is.EqualTo(answer), $"Fake-KI antwortet auf „{name}“ unter {culture} anders");
        }
    }

    [Test]
    public void Prompts_KeepGermanRoutingMarkers()
    {
        var prompts = BuildAll("en-US");
        Assert.Multiple(() =>
        {
            Assert.That(prompts["Audio Podcast Medium"], Does.Contain("Format: Podcast-Gespräch"));
            Assert.That(prompts["Audio Briefing Short"], Does.Contain("Format: Kurz-Briefing"));
            Assert.That(prompts.Where(p => p.Key.StartsWith("Datentabelle")).Select(p => p.Value), Has.All.StartWith("Erstelle eine Datentabelle"));
            Assert.That(prompts.Where(p => p.Key.StartsWith("Bericht")).Select(p => p.Value), Has.All.StartWith("Erstelle einen Bericht"));
            Assert.That(prompts["Video"], Does.StartWith("Erstelle ein Video-Skript"));
            Assert.That(prompts["Quizfragen"], Does.StartWith("Erstelle 5 Quizfragen"));
        });
    }

    private static Dictionary<string, string> AnswerAll(string culture) =>
        WithCulture(culture, () =>
        {
            var fake = new FakeLlmService();
            // Die Fake-KI nummeriert Quizfragen zufällig („[Testmodus #4505]“) – die Nummer zählt nicht
            return BuildAll(culture).ToDictionary(p => p.Key,
                p => RandomTag.Replace(fake.AskQuestionAsync(p.Value, Chunks).GetAwaiter().GetResult(), "#N"));
        });

    private static Dictionary<string, string> BuildAll(string culture) => WithCulture(culture, () =>
    {
        var prompts = new Dictionary<string, string>();

        foreach (var format in Enum.GetValues<AudioScriptFormat>())
            foreach (var length in Enum.GetValues<AudioScriptLength>())
            {
                prompts[$"Audio {format} {length}"] = AudioScriptService.BuildPrompt(format, length, null);
                prompts[$"Audio {format} {length} Schwerpunkt"] = AudioScriptService.BuildPrompt(format, length, "Photosynthese");
            }

        foreach (var template in Enum.GetValues<DataTableTemplate>())
            prompts[$"Datentabelle {template}"] = DataTableService.BuildPrompt(template, string.Empty, Array.Empty<string>(), null);
        prompts["Datentabelle eigene Spalten"] = DataTableService.BuildPrompt(DataTableTemplate.Custom, "Vergleiche die Prozesse", new[] { "Prozess", "Ort" }, "Energie");

        foreach (var template in Enum.GetValues<ReportTemplate>())
            prompts[$"Bericht {template}"] = ReportService.BuildPrompt(template, string.Empty);

        var topic = new QuizTopic { Name = "Photosynthese", Description = "Licht und Energie" };
        prompts["Quizfragen"] = QuizService.BuildQuestionPrompt(topic, 5, Enum.GetValues<QuizQuestionType>());

        prompts["Video"] = VideoScriptService.BuildPrompt(new PresentationDocument
        {
            Title = "Energie in Zellen",
            Slides =
            {
                new PresentationSlide { Title = "Photosynthese", Bullets = { "Licht wird zu Energie" }, Notes = "Die Pflanze nutzt Licht." },
                new PresentationSlide { Title = "Zellatmung", Bullets = { "Energie wird frei" } },
            }
        });

        // Dienste, deren Prompt nur beim Aufruf entsteht: über eine Attrappe abfangen
        prompts["Karteikarten"] = Capture(llm => new FlashcardService(llm).GenerateFlashcardsAsync(Chunks, 5));
        prompts["Mindmap"] = Capture(llm => new MindmapService(llm).GenerateMindmapAsync(Chunks, "Energie"));
        prompts["Präsentation"] = Capture(llm => new PresentationService(llm).GenerateFromDocumentsAsync(Chunks, "Energie", 6));
        prompts["Quiz-Themen"] = Capture(llm => new QuizService(llm).SuggestTopicsAsync(Chunks));
        return prompts;
    });

    private static string Capture(Func<ILlmService, Task> run)
    {
        string? prompt = null;
        var llm = new Mock<ILlmService>();
        llm.Setup(m => m.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, CancellationToken>((p, _, _) => prompt ??= p)
            .ReturnsAsync("[]");
        run(llm.Object).GetAwaiter().GetResult();
        Assert.That(prompt, Is.Not.Null, "kein Prompt abgefangen");
        return prompt!;
    }

    private static T WithCulture<T>(string name, Func<T> action)
    {
        var (ui, format) = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.CurrentCulture = format;
        }
    }
}
