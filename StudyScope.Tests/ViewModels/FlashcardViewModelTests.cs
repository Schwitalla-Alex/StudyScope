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
    public async Task GenerateCardsAsync_KeineVerwertbarenKarten_BehaeltVorhandenenStapel()
    {
        var docs = new List<DocumentItem> { new() { FileName = "Test.pdf", FilePath = "C:/Test.pdf", IsSelected = true } };
        _pdfServiceMock.Setup(p => p.ExtractChunksAsync("C:/Test.pdf")).ReturnsAsync(new List<string> { "Abschnitt" });
        _flashcardServiceMock.SetupSequence(s => s.GenerateFlashcardsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Flashcard> { new() { Question = "Alt?", Answer = "Alt", Status = FlashcardStatus.Known } })
            .ReturnsAsync(new List<Flashcard>());

        Assert.That((await _viewModel.GenerateCardsFromDocumentsAsync("Testprofil", docs)).Added, Is.EqualTo(1));
        _flashcardServiceMock.Invocations.Clear();

        var result = await _viewModel.GenerateCardsFromDocumentsAsync("Testprofil", docs, mode: FlashcardGenerationMode.Replace);

        Assert.That(result.Added, Is.EqualTo(0));
        Assert.That(result.Received, Is.EqualTo(0));
        Assert.That(_viewModel.Flashcards.Single().Question, Is.EqualTo("Alt?"));
        Assert.That(_viewModel.IsGenerating, Is.False);
        _flashcardServiceMock.Verify(s => s.SaveFlashcardsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Flashcard>>()), Times.Never);
    }

    [Test]
    public void GenerateCardsAsync_DienstFehler_WirdWeitergereichtUndStapelBleibt()
    {
        var docs = new List<DocumentItem> { new() { FileName = "Test.pdf", FilePath = "C:/Test.pdf", IsSelected = true } };
        _pdfServiceMock.Setup(p => p.ExtractChunksAsync("C:/Test.pdf")).ReturnsAsync(new List<string> { "Abschnitt" });
        _flashcardServiceMock.Setup(s => s.GenerateFlashcardsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<int>()))
            .ThrowsAsync(StudyScope.Core.Common.LlmServiceException.FromHttpStatus("Mistral AI", 503, "{}"));

        Assert.ThrowsAsync<StudyScope.Core.Common.LlmServiceException>(() => _viewModel.GenerateCardsFromDocumentsAsync("Testprofil", docs));

        Assert.That(_viewModel.IsGenerating, Is.False);
        _flashcardServiceMock.Verify(s => s.SaveFlashcardsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Flashcard>>()), Times.Never);
    }

    // ---------------- Hinzufügen oder Ersetzen ----------------

    private List<DocumentItem> SetupDocsReturning(params List<Flashcard>[] batches)
    {
        var docs = new List<DocumentItem> { new() { FileName = "Test.pdf", FilePath = "C:/Test.pdf", IsSelected = true } };
        _pdfServiceMock.Setup(p => p.ExtractChunksAsync("C:/Test.pdf")).ReturnsAsync(new List<string> { "Abschnitt" });
        var sequence = _flashcardServiceMock.SetupSequence(s => s.GenerateFlashcardsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<int>()));
        foreach (var batch in batches)
            sequence = sequence.ReturnsAsync(batch);
        return docs;
    }

    private void AddExistingCards()
    {
        _viewModel.Flashcards.Add(new Flashcard { Question = "Was ist RAG?", Answer = "Abruf plus Erzeugung", Status = FlashcardStatus.Known });
        _viewModel.Flashcards.Add(new Flashcard { Question = "Was ist ein Chunk?", Answer = "Ein Textabschnitt", Status = FlashcardStatus.Difficult });
    }

    [Test]
    public async Task GenerateCards_Standard_FuegtHinzuUndBehaeltLernstand()
    {
        AddExistingCards();
        var docs = SetupDocsReturning(new List<Flashcard>
        {
            new() { Question = "Was ist Photino?", Answer = "Ein Desktop-Rahmen" },
            new() { Question = "Wo liegen die Benutzerdaten?", Answer = "Im Benutzerordner" }
        });

        var result = await _viewModel.GenerateCardsFromDocumentsAsync("Testprofil", docs);

        Assert.That(result.Mode, Is.EqualTo(FlashcardGenerationMode.Append), "Standard ist Hinzufügen");
        Assert.That(result.Added, Is.EqualTo(2));
        Assert.That(result.SkippedDuplicates, Is.Zero);
        Assert.That(_viewModel.Flashcards.Select(c => c.Question), Is.EqualTo(new[]
        {
            "Was ist RAG?", "Was ist ein Chunk?", "Was ist Photino?", "Wo liegen die Benutzerdaten?"
        }));
        Assert.That(_viewModel.Flashcards[0].Status, Is.EqualTo(FlashcardStatus.Known), "Lernstand bleibt erhalten");
        Assert.That(_viewModel.Flashcards[1].Status, Is.EqualTo(FlashcardStatus.Difficult), "Lernstand bleibt erhalten");
        Assert.That(_viewModel.CurrentCard?.Question, Is.EqualTo("Was ist Photino?"), "springt zur ersten neuen Karte");
        _flashcardServiceMock.Verify(s => s.SaveFlashcardsAsync("Testprofil", It.Is<IEnumerable<Flashcard>>(c => c.Count() == 4)), Times.Once);
    }

    [Test]
    public async Task GenerateCards_Hinzufuegen_LaesstDoppelteFragenWeg()
    {
        AddExistingCards();
        var docs = SetupDocsReturning(new List<Flashcard>
        {
            new() { Question = "  was ist   RAG ", Answer = "Andere Antwort" },     // schon im Stapel (Schreibweise egal)
            new() { Question = "Was ist Photino?", Answer = "Ein Desktop-Rahmen" },
            new() { Question = "Was ist Photino", Answer = "Doppelt in der Antwort" } // doppelt innerhalb der Antwort
        });

        var result = await _viewModel.GenerateCardsFromDocumentsAsync("Testprofil", docs);

        Assert.That(result.Received, Is.EqualTo(3));
        Assert.That(result.Added, Is.EqualTo(1));
        Assert.That(result.SkippedDuplicates, Is.EqualTo(2));
        Assert.That(_viewModel.Flashcards, Has.Count.EqualTo(3));
        Assert.That(_viewModel.Flashcards.Single(c => c.Question == "Was ist RAG?").Answer, Is.EqualTo("Abruf plus Erzeugung"), "vorhandene Karte bleibt unverändert");
    }

    [Test]
    public async Task GenerateCards_Hinzufuegen_NurWiederholungen_SpeichertNicht()
    {
        AddExistingCards();
        var docs = SetupDocsReturning(new List<Flashcard> { new() { Question = "Was ist ein Chunk?", Answer = "x" } });

        var result = await _viewModel.GenerateCardsFromDocumentsAsync("Testprofil", docs);

        Assert.That(result.Added, Is.Zero);
        Assert.That(result.SkippedDuplicates, Is.EqualTo(1));
        Assert.That(_viewModel.Flashcards, Has.Count.EqualTo(2));
        _flashcardServiceMock.Verify(s => s.SaveFlashcardsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Flashcard>>()), Times.Never);
    }

    [Test]
    public async Task GenerateCards_Ersetzen_ErsetztStapelSamtLernstand()
    {
        AddExistingCards();
        var docs = SetupDocsReturning(new List<Flashcard>
        {
            new() { Question = "Was ist RAG?", Answer = "Neue Antwort" },
            new() { Question = "Was ist Photino?", Answer = "Ein Desktop-Rahmen" },
            new() { Question = "was ist photino", Answer = "Doppelt" }
        });

        var result = await _viewModel.GenerateCardsFromDocumentsAsync("Testprofil", docs, mode: FlashcardGenerationMode.Replace);

        Assert.That(result.Added, Is.EqualTo(2));
        Assert.That(result.SkippedDuplicates, Is.EqualTo(1), "nur Wiederholungen innerhalb der neuen Karten");
        Assert.That(_viewModel.Flashcards.Select(c => c.Question), Is.EqualTo(new[] { "Was ist RAG?", "Was ist Photino?" }));
        Assert.That(_viewModel.Flashcards.All(c => c.Status == FlashcardStatus.New), "alter Lernstand ist weg");
        Assert.That(_viewModel.CurrentIndex, Is.Zero);
        _flashcardServiceMock.Verify(s => s.SaveFlashcardsAsync("Testprofil", It.Is<IEnumerable<Flashcard>>(c => c.Count() == 2)), Times.Once);
    }

    [TestCase("Was ist RAG?", "was ist rag")]
    [TestCase("  Was   ist\tRAG ?! ", "was ist rag")]
    [TestCase(null, "")]
    public void NormalizeQuestion_IgnoriertSchreibweise(string? input, string expected)
    {
        Assert.That(FlashcardViewModel.NormalizeQuestion(input), Is.EqualTo(expected));
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