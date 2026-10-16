using Moq;
using NUnit.Framework;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Test für StudyScope.ViewModels mit der Logik für die Dokumentenliste, Filterung/Suche, 
/// das Auswählen/Abwählen und die Statusbar-Statistiken, das Suche und Zählung von Dokumenten und Textstellen, Markieren aller Dokumente, Löschen fehlender Dateien.
/// </summary>
/// </summary>
[TestFixture]
public class DocumentListViewModelTests
{
    private Mock<IStorageService> _storageServiceMock;
    private Mock<IDocumentService> _pdfServiceMock;

    [SetUp]
    public void SetUp()
    {
        _storageServiceMock = new Mock<IStorageService>();
        _pdfServiceMock = new Mock<IDocumentService>();
    }

    [Test]
    public async Task LoadDocumentsAsync_ShouldPopulateDocumentsAndCalculateCounters()
    {
        // Arrange
        var testDocs = new List<DocumentItem>
        {
            new() { FileName = "Doc1.pdf", FilePath = "C:/Doc1.pdf", ExtractedChunkCount = 10, IsSelected = true },
            new() { FileName = "Doc2.pdf", FilePath = "C:/Doc2.pdf", ExtractedChunkCount = 25, IsSelected = true }
        };

        _storageServiceMock.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>()))
                           .ReturnsAsync(testDocs);

        // Hier beide Mocks übergeben:
        var viewModel = new DocumentListViewModel(_storageServiceMock.Object, _pdfServiceMock.Object);

        // Act
        await viewModel.LoadDocumentsAsync();

        // Assert
        Assert.That(viewModel.Documents.Count, Is.EqualTo(2));
        Assert.That(viewModel.TotalDocumentCount, Is.EqualTo(2));
        Assert.That(viewModel.TotalChunkCount, Is.EqualTo(35));
    }

    [Test]
    public async Task AddDocumentAsync_ShouldCalculateChunks_AndSaveToProfile()
    {
        // Arrange: Echte temporäre Datei erstellen, damit File.Exists true ergibt
        var tempFile = Path.Combine(Path.GetTempPath(), "Handbuch.pdf");
        await File.WriteAllTextAsync(tempFile, "Dummy PDF Content");

        try
        {
            _pdfServiceMock.Setup(p => p.GetChunkCountAsync(tempFile))
                           .ReturnsAsync(42);

            var viewModel = new DocumentListViewModel(_storageServiceMock.Object, _pdfServiceMock.Object);

            // Act
            await viewModel.AddDocumentAsync(tempFile);

            // Assert
            Assert.That(viewModel.Documents.Count, Is.EqualTo(1));
            var added = viewModel.Documents.First();
            Assert.That(added.FileName, Is.EqualTo("Handbuch.pdf"));
            Assert.That(added.ExtractedChunkCount, Is.EqualTo(42));
            Assert.That(viewModel.TotalChunkCount, Is.EqualTo(42));

            _storageServiceMock.Verify(s => s.SaveDocumentsAsync(viewModel.CurrentProfile, viewModel.Documents), Times.Once);
        }
        finally
        {
            // Aufräumen
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task SearchText_ShouldFilterVisibleDocuments()
    {
        // Arrange
        var testDocs = new List<DocumentItem>
        {
            new() { FileName = "BGB_Skript.pdf", FilePath = "C:/BGB_Skript.pdf" },
            new() { FileName = "HGB_Fallfragen.pdf", FilePath = "C:/HGB_Fallfragen.pdf" },
            new() { FileName = "BGB_Musterloesung.pdf", FilePath = "C:/BGB_Musterloesung.pdf" }
        };

        _storageServiceMock.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>())).ReturnsAsync(testDocs);

        // Hier ebenfalls beide Mocks übergeben:
        var viewModel = new DocumentListViewModel(_storageServiceMock.Object, _pdfServiceMock.Object);
        await viewModel.LoadDocumentsAsync();

        // Act
        viewModel.SearchText = "BGB";

        // Assert
        Assert.That(viewModel.FilteredDocuments.Count(), Is.EqualTo(2));
        Assert.That(viewModel.FilteredDocuments.All(d => d.FileName.Contains("BGB")), Is.True);
    }

    [Test]
    public async Task SelectAll_And_DeselectAll_ShouldUpdateSelectionStates()
    {
        // Arrange
        var testDocs = new List<DocumentItem>
        {
            new() { FileName = "Doc1.pdf", IsSelected = true },
            new() { FileName = "Doc2.pdf", IsSelected = true }
        };

        _storageServiceMock.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>())).ReturnsAsync(testDocs);

        // Hier ebenfalls beide Mocks übergeben:
        var viewModel = new DocumentListViewModel(_storageServiceMock.Object, _pdfServiceMock.Object);
        await viewModel.LoadDocumentsAsync();

        // Act & Assert - Deselect all
        viewModel.DeselectAll();
        Assert.That(viewModel.Documents.All(d => !d.IsSelected), Is.True);

        // Act & Assert - Select all
        viewModel.SelectAll();
        Assert.That(viewModel.Documents.All(d => d.IsSelected), Is.True);
    }

    [Test]
    public async Task CreateNewProfileAsync_ShouldAddProfileAndSwitchToIt()
    {
        // Arrange
        _storageServiceMock.Setup(s => s.GetAllProfileNamesAsync())
                           .ReturnsAsync(new List<string> { "Standard" });
        _storageServiceMock.Setup(s => s.LoadDocumentsAsync("Jura"))
                           .ReturnsAsync(new List<DocumentItem>());

        var viewModel = new DocumentListViewModel(_storageServiceMock.Object, _pdfServiceMock.Object);
        await viewModel.InitializeAsync();

        // Act
        await viewModel.CreateNewProfileAsync("Jura");

        // Assert
        Assert.That(viewModel.CurrentProfile, Is.EqualTo("Jura"));
        Assert.That(viewModel.AvailableProfiles, Does.Contain("Jura"));
        _storageServiceMock.Verify(s => s.SetLastActiveProfileNameAsync("Jura"), Times.Once);
    }

    [Test]
    public async Task DeleteCurrentProfileAsync_WhenNotStandard_ShouldDeleteAndFallbackToStandard()
    {
        // Arrange
        _storageServiceMock.Setup(s => s.GetAllProfileNamesAsync())
                           .ReturnsAsync(new List<string> { "Standard", "Chemie" });
        _storageServiceMock.Setup(s => s.GetLastActiveProfileNameAsync())
                           .ReturnsAsync("Chemie");
        _storageServiceMock.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>()))
                           .ReturnsAsync(new List<DocumentItem>());

        var viewModel = new DocumentListViewModel(_storageServiceMock.Object, _pdfServiceMock.Object);
        await viewModel.InitializeAsync();

        // Act
        await viewModel.DeleteProfileAsync("Chemie");

        // Assert
        _storageServiceMock.Verify(s => s.DeleteProfileAsync("Chemie"), Times.Once);
        Assert.That(viewModel.CurrentProfile, Is.EqualTo("Standard"));
    }

    [Test]
    public async Task DeleteCurrentProfileAsync_WhenCustomProfile_ShouldDeleteAndResetToStandard()
    {
        // Arrange
        var storageMock = new Mock<IStorageService>();
        storageMock.Setup(s => s.GetAllProfileNamesAsync())
                   .ReturnsAsync(new List<string> { "Standard", "Prüfung" });
        storageMock.Setup(s => s.GetLastActiveProfileNameAsync())
                   .ReturnsAsync("Prüfung");
        storageMock.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>()))
                   .ReturnsAsync(new List<DocumentItem>());

        var pdfMock = new Mock<IDocumentService>();
        var viewModel = new DocumentListViewModel(storageMock.Object, pdfMock.Object);
        await viewModel.InitializeAsync();

        // Act
        await viewModel.DeleteProfileAsync("Prüfung");

        // Assert
        storageMock.Verify(s => s.DeleteProfileAsync("Prüfung"), Times.Once);
        Assert.That(viewModel.CurrentProfile, Is.EqualTo("Standard"));
        Assert.That(viewModel.AvailableProfiles, Does.Not.Contain("Prüfung"));
    }
}
