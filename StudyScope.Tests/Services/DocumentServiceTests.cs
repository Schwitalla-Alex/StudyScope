using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using StudyScope.Core.Interfaces;
using NUnit.Framework;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

[TestFixture]
public class DocumentServiceTests
{
    /// <summary>
    /// Der PDF-Parser-Dienst, der für das Einlesen von PDF-Dateien, das Extrahieren von Text und das Bilden von Chunks (Textstellen) zuständig ist. 
    /// Damit speisen wir auch die Zählung für die Statusleiste (Anzahl der Textstellen).  Für eine leichtgewichtige,
    /// plattformunabhängige (Windows & Linux) C#-Lösung eignet sich die Open-Source-Bibliothek PdfPig hervorragend, da sie ohne native C++-Abhängigkeiten auskommt.
    /// </summary>
    private DocumentService _pdfService;

    [SetUp]
    public void SetUp()
    {
        _pdfService = new DocumentService();
    }

    [Test]
    public async Task ExtractChunksAsync_WhenFileNotFound_ShouldReturnEmptyList()
    {
        // Arrange
        var missingPath = "C:/NonExistent/Missing.pdf";

        // Act
        var result = await _pdfService.ExtractChunksAsync(missingPath);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetChunkCountAsync_WhenFileNotFound_ShouldReturnZero()
    {
        // Arrange
        var missingPath = "C:/NonExistent/Missing.pdf";

        // Act
        var count = await _pdfService.GetChunkCountAsync(missingPath);

        // Assert
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void SplitIntoChunks_ShouldDivideLargeTextIntoPieces()
    {
        // Arrange: 600 Wörter Text erzeugen
        var words = Enumerable.Range(1, 600).Select(i => $"Wort{i}").ToArray();
        var fullText = string.Join(" ", words);

        // Act
        var chunks = DocumentService.SplitIntoChunks(fullText, maxWordsPerChunk: 250);

        // Assert
        Assert.That(chunks.Count, Is.EqualTo(3)); // 250 + 250 + 100
        Assert.That(chunks[0], Does.StartWith("Wort1"));
        Assert.That(chunks[2], Does.EndWith("Wort600"));
    }

    [Test]
    public async Task ExtractChunksAsync_WhenTxtFileProvided_ShouldExtractContent()
    {
        // Eindeutigen Pfad direkt erzeugen ohne blockierendes GetTempFileName
        var txtPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");

        try
        {
            var sampleText = "Dies ist ein Testabsatz für den Dokumenten-Service. StudyScope verarbeitet nun auch reine Textdateien ohne Probleme.";
            await File.WriteAllTextAsync(txtPath, sampleText, System.Text.Encoding.UTF8);

            var service = new DocumentService();

            // Act
            var chunks = await service.ExtractChunksAsync(txtPath, maxWordsPerChunk: 50);

            // Assert
            Assert.That(chunks, Has.Count.EqualTo(1));
            Assert.That(chunks[0], Does.Contain("StudyScope"));
        }
        finally
        {
            if (File.Exists(txtPath))
                File.Delete(txtPath);
        }
    }

    [Test]
    public async Task ExtractChunksAsync_WhenCsvFileProvided_ShouldIncludeHeaderInChunks()
    {
        // Eindeutigen Pfad direkt erzeugen
        var csvPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");

        try
        {
            var csvContent = "Datum;Thema;Leiter\n" +
                             "04.11.;Atemschutzübung;Max Mustermann\n" +
                             "18.11.;Funkübung;Erika Musterfrau";
            await File.WriteAllTextAsync(csvPath, csvContent, System.Text.Encoding.UTF8);

            var service = new DocumentService();

            // Act
            var chunks = await service.ExtractChunksAsync(csvPath, maxWordsPerChunk: 50);

            // Assert
            Assert.That(chunks, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(chunks[0], Does.Contain("[CSV-Spalten: Datum;Thema;Leiter]"));
            Assert.That(chunks[0], Does.Contain("Atemschutzübung"));
        }
        finally
        {
            if (File.Exists(csvPath))
                File.Delete(csvPath);
        }
    }

}