using Moq;
using NUnit.Framework;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Im JSON-Modus (Mistral/OpenAI, response_format json_object) liefert der Dienst immer ein JSON-OBJEKT.
/// Die Prompts verlangen aber Listen. Jede Erzeugung muss daher auch Hüllen wie {"items": [...]},
/// {"karteikarten": [...]}, {"questions": [...], "anzahl": 5} oder das einzelne Objekt direkt verstehen –
/// sowohl roh (andere Anbieter) als auch nach <see cref="LlmServiceDispatcher.UnwrapJsonItems"/>.
/// Simulierte Antworten, kein echter KI-Aufruf.
/// </summary>
[TestFixture]
public class JsonModeEnvelopeTests
{
    private static IEnumerable<string> Variants(string raw)
    {
        yield return raw;
        yield return LlmServiceDispatcher.UnwrapJsonItems(raw);
    }

    // ---------------- Karteikarten ----------------

    private const string Card1 = "{\"question\": \"Was ist RAG?\", \"answer\": \"Abruf plus Erzeugung.\"}";
    private const string Card2 = "{\"frage\": \"Was ist ein Chunk?\", \"antwort\": \"Ein Textabschnitt.\"}";

    [TestCase("{\"items\": [" + Card1 + ", " + Card2 + "]}", 2)]
    [TestCase("{\"karteikarten\": [" + Card1 + ", " + Card2 + "]}", 2)]
    [TestCase("{\"karteikarten\": [" + Card1 + ", " + Card2 + "], \"anzahl\": 2}", 2)]
    [TestCase("{\"thema\": \"RAG\", \"cards\": [" + Card1 + ", " + Card2 + "]}", 2)]
    [TestCase("{\"items\": {\"karteikarten\": [" + Card1 + ", " + Card2 + "]}}", 2)]
    [TestCase(Card1, 1)]
    [TestCase("{\"items\": " + Card1 + "}", 1)]
    [TestCase("[" + Card1 + ", " + Card2 + "]", 2)]
    [TestCase("```json\n{\"karteikarten\": [" + Card1 + "], \"hinweis\": \"[Quelle]\"}\n```", 1)]
    public async Task Karteikarten_VerstehenHuellen(string raw, int expected)
    {
        foreach (var variant in Variants(raw))
        {
            var llm = new Mock<ILlmService>();
            llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(variant);

            var cards = await new FlashcardService(llm.Object).GenerateFlashcardsAsync(new[] { "Text" }, expected);

            Assert.That(cards, Has.Count.EqualTo(expected), variant);
            Assert.That(cards.All(c => c.Question.Length > 0 && c.Answer.Length > 0), variant);
        }
    }

    [Test]
    public async Task Karteikarten_TextOhneKarten_LiefertLeereListe()
    {
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"fehler\": \"Keine Inhalte gefunden\", \"liste\": [1, 2]}");

        var cards = await new FlashcardService(llm.Object).GenerateFlashcardsAsync(new[] { "Text" }, 3);

        Assert.That(cards, Is.Empty);
    }

    // ---------------- Mindmap ----------------

    private const string MindmapRoot =
        "{\"title\": \"StudyScope\", \"summary\": \"Lernsoftware\", \"children\": [{\"title\": \"Chat\"}, {\"title\": \"Karteikarten\"}]}";

    [TestCase("{\"items\": [" + MindmapRoot + "]}")]
    [TestCase(MindmapRoot)]
    [TestCase("{\"items\": " + MindmapRoot + "}")]
    [TestCase("{\"mindmap\": " + MindmapRoot + "}")]
    [TestCase("{\"mindmap\": " + MindmapRoot + ", \"sprache\": \"de\"}")]
    [TestCase("{\"mindmap\": [" + MindmapRoot + "]}")]
    public void Mindmap_VerstehtHuellen(string raw)
    {
        foreach (var variant in Variants(raw))
        {
            var result = MindmapService.AnalyzeResponse(variant, "Fallback");

            Assert.That(result.Root, Is.Not.Null, variant);
            Assert.That(result.Root!.Title, Is.EqualTo("StudyScope"), variant);
            Assert.That(result.Root.Children.Select(c => c.Title), Is.EqualTo(new[] { "Chat", "Karteikarten" }), variant);
        }
    }

    // ---------------- Präsentation ----------------

    private const string PresentationRoot =
        "{\"title\": \"StudyScope\", \"subtitle\": \"Überblick\", \"slides\": [" +
        "{\"title\": \"Chat\", \"bullets\": [\"Fragen stellen\"], \"notes\": \"\"}, " +
        "{\"title\": \"Karteikarten\", \"bullets\": [\"Lernen\"], \"notes\": \"\"}]}";

    [TestCase("{\"items\": [" + PresentationRoot + "]}")]
    [TestCase(PresentationRoot)]
    [TestCase("{\"items\": " + PresentationRoot + "}")]
    [TestCase("{\"praesentation\": " + PresentationRoot + "}")]
    [TestCase("{\"presentation\": " + PresentationRoot + ", \"version\": 1}")]
    public void Praesentation_VerstehtHuellen(string raw)
    {
        foreach (var variant in Variants(raw))
        {
            var result = PresentationService.AnalyzeResponse(variant, "Fallback");

            Assert.That(result.Value, Is.Not.Null, variant);
            Assert.That(result.Value!.Title, Is.EqualTo("StudyScope"), variant);
            Assert.That(result.Value.Slides.Select(s => s.Title), Does.Contain("Chat").And.Contain("Karteikarten"), variant);
        }
    }

    // ---------------- Bericht ----------------

    private const string ReportRoot =
        "{\"title\": \"Lernübersicht StudyScope\", \"summary\": \"Kurzfassung\", \"sections\": [" +
        "{\"heading\": \"Installation\", \"paragraphs\": [\"Setup starten.\"], \"bullets\": [\"Windows\", \"Linux\"]}]}";

    [TestCase("{\"items\": [" + ReportRoot + "]}")]
    [TestCase(ReportRoot)]
    [TestCase("{\"items\": " + ReportRoot + "}")]
    [TestCase("{\"bericht\": " + ReportRoot + "}")]
    [TestCase("{\"report\": " + ReportRoot + ", \"vorlage\": \"Lernübersicht\"}")]
    public void Bericht_VerstehtHuellen(string raw)
    {
        foreach (var variant in Variants(raw))
        {
            var result = ReportService.AnalyzeResponse(variant, "Fallback");

            Assert.That(result.Value, Is.Not.Null, variant);
            Assert.That(result.Value!.Title, Is.EqualTo("Lernübersicht StudyScope"), variant);
            Assert.That(result.Value.Sections.Select(s => s.Heading), Is.EqualTo(new[] { "Installation" }), variant);
        }
    }

    // ---------------- Quizfragen ----------------

    private const string Question1 =
        "{\"type\": \"single\", \"question\": \"Womit wird StudyScope gebaut?\", \"options\": [\"Photino\", \"WPF\", \"Qt\", \"Swing\"], \"correct\": [0], \"explanation\": \"Steht im Text.\"}";
    private const string Question2 =
        "{\"type\": \"truefalse\", \"question\": \"StudyScope läuft unter Linux.\", \"options\": [\"Wahr\", \"Falsch\"], \"correct\": [0], \"explanation\": \"Steht im Text.\"}";

    private static readonly QuizQuestionType[] AllTypes =
        { QuizQuestionType.SingleChoice, QuizQuestionType.TrueFalse, QuizQuestionType.MultipleChoice };

    [TestCase("{\"items\": [" + Question1 + ", " + Question2 + "]}", 2)]
    [TestCase("{\"questions\": [" + Question1 + ", " + Question2 + "]}", 2)]
    [TestCase("{\"quizfragen\": [" + Question1 + ", " + Question2 + "]}", 2)]
    [TestCase("{\"quizfragen\": [" + Question1 + ", " + Question2 + "], \"anzahl\": 2}", 2)]
    [TestCase("{\"thema\": \"Installation\", \"fragen\": [" + Question1 + ", " + Question2 + "]}", 2)]
    [TestCase("{\"items\": {\"questions\": [" + Question1 + ", " + Question2 + "]}}", 2)]
    [TestCase(Question1, 1)]
    public void Quizfragen_VerstehenHuellen(string raw, int expected)
    {
        foreach (var variant in Variants(raw))
        {
            var result = LenientJson.Analyze(variant, e => QuizService.ParseQuestions(e, AllTypes));

            Assert.That(result.Value, Is.Not.Null, variant);
            Assert.That(result.Value!, Has.Count.EqualTo(expected), variant);
        }
    }

    // ---------------- Quiz-Themen ----------------

    private const string Topic1 = "{\"name\": \"Installation\", \"description\": \"Setup unter Windows und Linux\"}";
    private const string Topic2 = "{\"name\": \"Betrieb\", \"description\": \"Starten und Einstellungen\"}";

    [TestCase("{\"items\": [" + Topic1 + ", " + Topic2 + "]}")]
    [TestCase("{\"topics\": [" + Topic1 + ", " + Topic2 + "]}")]
    [TestCase("{\"quizthemen\": [" + Topic1 + ", " + Topic2 + "]}")]
    [TestCase("{\"quizthemen\": [" + Topic1 + ", " + Topic2 + "], \"dokument\": \"Anleitung\"}")]
    [TestCase("{\"items\": {\"themen\": [" + Topic1 + ", " + Topic2 + "]}}")]
    [TestCase("{\"themen\": [\"Installation\", \"Betrieb\"]}")]
    public void QuizThemen_VerstehenHuellen(string raw)
    {
        foreach (var variant in Variants(raw))
        {
            var result = LenientJson.Analyze(variant, QuizService.ParseTopicSuggestions);

            Assert.That(result.Value, Is.Not.Null, variant);
            Assert.That(result.Value!.Select(t => t.Name), Is.EqualTo(new[] { "Installation", "Betrieb" }), variant);
        }
    }

    // ---------------- Dispatcher ----------------

    [TestCase("{\"items\": {\"title\": \"X\"}}", "[{\"title\": \"X\"}]")]
    [TestCase("{\"mindmap\": {\"title\": \"X\"}}", "[{\"title\": \"X\"}]")]
    public void UnwrapJsonItems_EinzelnesObjektInHuelle_WirdZumArray(string content, string expected)
    {
        Assert.That(LlmServiceDispatcher.UnwrapJsonItems(content), Is.EqualTo(expected));
    }
}
