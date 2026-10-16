using System.Text.Json;
using Moq;
using NUnit.Framework;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Gemäß den Anforderungen umfasst das Karteikarten-System folgende Kernbereiche:
/// Datenmodell(Flashcard / FlashcardDeck) : Frage, Antwort, Quell-Dokument/Seite, Lernstatus.
/// Generierung via LLM: Aus den aktivierten Dokumenten werden automatisch Frage - Antwort - Paare im JSON-Format erzeugt.
/// Lernmodus-UI: Ein interaktiver Modus(Karte umdrehen: Frage ➔ Antwort, Bewertung „Gewusst“ / „Nicht gewusst“).
/// Druck- & Exportansicht: Ausgabe als DIN - A4 - Druckansicht oder PDF/Markdown.
/// </summary>
[TestFixture]
public class FlashcardServiceTests
{
    private Mock<ILlmService> _llmServiceMock;
    private FlashcardService _service;

    [SetUp]
    public void SetUp()
    {
        _llmServiceMock = new Mock<ILlmService>();
        _service = new FlashcardService(_llmServiceMock.Object);
    }

    [Test]
    public async Task GenerateFlashcardsAsync_WhenNoChunksProvided_ShouldReturnEmptyList()
    {
        // Act
        var result = await _service.GenerateFlashcardsAsync(new List<string>(), count: 5);

        // Assert
        Assert.That(result, Is.Empty);
        _llmServiceMock.Verify(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Test]
    public async Task GenerateFlashcardsAsync_WhenLlmReturnsValidJson_ShouldParseFlashcardsCorrectly()
    {
        // Arrange
        var chunks = new List<string> { "Das BGB regelt das Privatrecht in Deutschland." };
        var jsonResponse = JsonSerializer.Serialize(new[]
        {
            new { question = "Was regelt das BGB?", answer = "Das Privatrecht in Deutschland." }
        });

        _llmServiceMock.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                       .ReturnsAsync(jsonResponse);

        // Act
        var result = await _service.GenerateFlashcardsAsync(chunks, count: 1);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Question, Is.EqualTo("Was regelt das BGB?"));
        Assert.That(result[0].Answer, Is.EqualTo("Das Privatrecht in Deutschland."));
        Assert.That(result[0].Status, Is.EqualTo(FlashcardStatus.New));
    }

    [Test]
    public async Task GenerateFlashcardsAsync_WhenLlmReturnsMarkdownJsonBlock_ShouldCleanAndParseCorrectly()
    {
        // Arrange (LLMs formatieren JSON oft in ```json ... ``` Codeblöcken)
        var chunks = new List<string> { "Textinhalt" };
        var jsonWithMarkdown = "```json\n[{\"question\":\"Frage 1\",\"answer\":\"Antwort 1\"}]\n```";

        _llmServiceMock.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                       .ReturnsAsync(jsonWithMarkdown);

        // Act
        var result = await _service.GenerateFlashcardsAsync(chunks, count: 1);

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Question, Is.EqualTo("Frage 1"));
    }

    [Test]
    public void ExportToMarkdown_WithCards_ShouldFormatValidMarkdown()
    {
        // Arrange
        var cards = new List<Flashcard>
        {
            new() { Question = "Was ist TDD?", Answer = "Test-Driven Development." },
            new() { Question = "Was regelt das BGB?", Answer = "Das Privatrecht." }
        };

        // Act
        var markdown = _service.ExportToMarkdown("TestProfil", cards);

        // Assert
        Assert.That(markdown, Does.Contain("# Lernkarteikarten: TestProfil"));
        Assert.That(markdown, Does.Contain("### Frage 1: Was ist TDD?"));
        Assert.That(markdown, Does.Contain("**Antwort:** Test-Driven Development."));
        Assert.That(markdown, Does.Contain("### Frage 2: Was regelt das BGB?"));
    }
}