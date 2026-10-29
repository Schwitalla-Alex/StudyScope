using Moq;
using NUnit.Framework;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.ViewModels;
using System.Runtime.ConstrainedExecution;
using UglyToad.PdfPig.Content;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Unit-Tests für das FlashcardViewModel, das die Logik für das Karteikarten-System steuert.
///     Karteikarten-Systems:
///     FlashcardViewModel & Dependency Injection.Das ViewModel steuert die Zustände für die spätere Blazor-Oberfläche:Extrahieren der Textauszüge aus den aktuell aktivierten PDFs via IPdfService 
///     Aufruf von IFlashcardService.GenerateFlashcardsAsync(...) 
///     Speichern & Laden der Decks je Thema / Profil 
///     Index-Verwaltung für den Lernmodus(nächste Karte, vorherige Karte, Karte aufdecken)  
///     Bewertungs-Logik: „Gewusst“ / „Nicht gewusst“   
/// </summary>
[TestFixture]
public class FlashcardViewModelTests
{
    private Mock<IFlashcardService> _flashcardServiceMock;
    private Mock<IDocumentService> _pdfServiceMock;
    private Mock<IStorageService> _storageServiceMock;
    private FlashcardViewModel _viewModel;

    [SetUp]
    public void SetUp()
    {
        _flashcardServiceMock = new Mock<IFlashcardService>();
        _pdfServiceMock = new Mock<IDocumentService>();
        _storageServiceMock = new Mock<IStorageService>();

        _viewModel = new FlashcardViewModel(
            _flashcardServiceMock.Object,
            _pdfServiceMock.Object,
            _storageServiceMock.Object);
    }

    [Test]
    public async Task GenerateCardsAsync_WhenNoDocumentsSelected_ShouldNotCallService()
    {
        // Act
        await _viewModel.GenerateCardsFromDocumentsAsync("Privat", new List<DocumentItem>());

        // Assert
        Assert.That(_viewModel.Flashcards, Is.Empty);
        _flashcardServiceMock.Verify(s => s.GenerateFlashcardsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task GenerateCardsAsync_WithSelectedDocuments_ShouldPopulateCardsAndSave()
    {
        // Arrange
        var docs = new List<DocumentItem>
        {
            new() { FileName = "Test.pdf", FilePath = "C:/Test.pdf", IsSelected = true }
        };

        _pdfServiceMock.Setup(p => p.ExtractChunksAsync("C:/Test.pdf"))
                       .ReturnsAsync(new List<string> { "Abschnitt 1 Inhalt" });

        var generatedCards = new List<Flashcard>
        {
            new() { Question = "Frage 1?", Answer = "Antwort 1" }
        };

        _flashcardServiceMock.Setup(s => s.GenerateFlashcardsAsync(It.IsAny<IEnumerable<string>>(), 5))
                             .ReturnsAsync(generatedCards);

        // Act
        await _viewModel.GenerateCardsFromDocumentsAsync("Privat", docs, cardCount: 5);

        // Assert
        Assert.That(_viewModel.Flashcards, Has.Count.EqualTo(1));
        Assert.That(_viewModel.CurrentCard?.Question, Is.EqualTo("Frage 1?"));
        _flashcardServiceMock.Verify(s => s.SaveFlashcardsAsync("Privat", It.IsAny<IEnumerable<Flashcard>>()), Times.Once);
    }

    [Test]
    public void FlipCard_ShouldToggleIsAnswerRevealed()
    {
        // Arrange
        _viewModel.IsAnswerRevealed = false;

        // Act
        _viewModel.FlipCard();

        // Assert
        Assert.That(_viewModel.IsAnswerRevealed, Is.True);

        _viewModel.FlipCard();
        Assert.That(_viewModel.IsAnswerRevealed, Is.False);
    }

    [Test]
    public async Task RateCard_WhenMarkedKnown_ShouldUpdateStatusAndAdvanceIndex()
    {
        // Arrange
        var card1 = new Flashcard { Question = "Q1", Answer = "A1" };
        var card2 = new Flashcard { Question = "Q2", Answer = "A2" };
        _viewModel.Flashcards.Add(card1);
        _viewModel.Flashcards.Add(card2);
        _viewModel.CurrentIndex = 0;
        _viewModel.IsAnswerRevealed = true;

        // Act
        await _viewModel.RateCurrentCardAsync("Privat", FlashcardStatus.Known);

        // Assert
        Assert.That(card1.Status, Is.EqualTo(FlashcardStatus.Known));
        Assert.That(_viewModel.CurrentIndex, Is.EqualTo(1));
        Assert.That(_viewModel.IsAnswerRevealed, Is.False);
    }

    [Test]
    public void FilteredFlashcards_WhenFilterApplied_ShouldOnlyReturnMatchingCards()
    {
        // Arrange
        _viewModel.Flashcards.Add(new Flashcard { Question = "Q1", Status = FlashcardStatus.Difficult });
        _viewModel.Flashcards.Add(new Flashcard { Question = "Q2", Status = FlashcardStatus.Known });
        _viewModel.Flashcards.Add(new Flashcard { Question = "Q3", Status = FlashcardStatus.Difficult });

        // Act
        _viewModel.SelectedFilter = FlashcardStatusFilter.DifficultOnly;

        // Assert
        Assert.That(_viewModel.FilteredFlashcards, Has.Count.EqualTo(2));
        Assert.That(_viewModel.FilteredFlashcards.All(c => c.Status == FlashcardStatus.Difficult), Is.True);
        Assert.That(_viewModel.CurrentCard?.Question, Is.EqualTo("Q1"));
    }

    [Test]
    public async Task RateCard_WhenFiltered_ShouldAdvanceWithinFilteredCards()
    {
        // Arrange
        var card1 = new Flashcard { Question = "Q1", Status = FlashcardStatus.Difficult };
        var card2 = new Flashcard { Question = "Q2", Status = FlashcardStatus.Known };
        var card3 = new Flashcard { Question = "Q3", Status = FlashcardStatus.Difficult };

        _viewModel.Flashcards.Add(card1);
        _viewModel.Flashcards.Add(card2);
        _viewModel.Flashcards.Add(card3);

        _viewModel.SelectedFilter = FlashcardStatusFilter.DifficultOnly;
        _viewModel.IsAnswerRevealed = true;

        // Act: card1 wird jetzt auf Known gesetzt
        await _viewModel.RateCurrentCardAsync("Privat", FlashcardStatus.Known);

        // Assert: Index und Anzeige passen sich an die verbliebenen gefilterten Karten an
        Assert.That(card1.Status, Is.EqualTo(FlashcardStatus.Known));
        Assert.That(_viewModel.FilteredFlashcards, Has.Count.EqualTo(1));
        Assert.That(_viewModel.CurrentCard?.Question, Is.EqualTo("Q3"));
    }

    /// <summary>
    /// Diese Methode testet gezielt, ob:
    /// Der Markdown-Wrapper(````json... ````) zuverlässig entfernt wird.
    /// Voran-und nachgestellter Text ignoriert wird.
    /// Unterschiedliche Schreibweisen (`Question` groß vs. `question` klein) problemlos akzeptiert werden.
    /// </summary>
    [Test]
    public async Task GenerateFlashcardsAsync_WhenLlmReturnsMarkdownAndPreamble_ShouldParseCorrectly()
    {
        // Arrange: Typische LM Studio / LLM-Antwort mit Markdown und Freitext
        var messyLlmResponse = """
        Hier sind die gewünschten Karteikarten:
        ```json
        [
          { "Question": "Was ist die Feuerwehr?", "Answer": "Eine Hilfsorganisation." },
          { "question": "Wann ist die Winterschulung?", "answer": "Am 13.01." }
        ]
        ```
        """;

        var llmMock = new Mock<ILlmService>();
        llmMock.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
               .ReturnsAsync(messyLlmResponse);

        var service = new FlashcardService(llmMock.Object);

        // Act
        var cards = await service.GenerateFlashcardsAsync(new List<string> { "Textabschnitt" }, 2);

        // Assert
        Assert.That(cards, Has.Count.EqualTo(2));
        Assert.That(cards[0].Question, Is.EqualTo("Was ist die Feuerwehr?"));
        Assert.That(cards[1].Answer, Is.EqualTo("Am 13.01."));
    }

    [Test]
    public async Task ClearFlashcardsAsync_ShouldEmptyCollections_SaveToStorage_AndNotifyChanges()
    {
        // 1. Arrange: Testkarten vorbereiten
        var testProfile = "TestProfil";
        _viewModel.Flashcards.Add(new Flashcard { Question = "Frage 1", Answer = "Antwort 1" });
        _viewModel.Flashcards.Add(new Flashcard { Question = "Frage 2", Answer = "Antwort 2" });
        _viewModel.FilteredFlashcards.Add(new Flashcard { Question = "Frage 1", Answer = "Antwort 1" });

        // Prüfen, ob vor dem Löschen Karten existieren
        Assert.That(_viewModel.Flashcards, Has.Count.EqualTo(2));

        // 2. Act: Löschmethode aufrufen
        await _viewModel.ClearFlashcardsAsync(testProfile);

        // 3. Assert: Prüfungen
        // a) Listen müssen komplett leer sein
        Assert.That(_viewModel.Flashcards, Is.Empty);
        Assert.That(_viewModel.FilteredFlashcards, Is.Empty);

        // b) CurrentCard muss null sein
        Assert.That(_viewModel.CurrentCard, Is.Null);

        // c) Es muss sichergestellt sein, dass eine leere Liste im Storage gespeichert wurde
        _flashcardServiceMock.Verify(s => s.SaveFlashcardsAsync(testProfile, It.Is<IEnumerable<Flashcard>>(cards => !cards.Any())), Times.Once);
    }

}