using Moq;
using NUnit.Framework;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace StudyScope.Tests.Services;

[TestFixture]
public class JsonStorageServiceTests
{
    private string _tempDirectory;
    private JsonStorageService _storageService;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "StudyScope_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
        _storageService = new JsonStorageService(_tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Test]
    public async Task SaveAndLoadAiConfig_ShouldPersistSettings()
    {
        // Arrange
        var config = new AiConfig
        {
            SelectedProvider = AiProviderType.GoogleGemini,
            ApiKey = "secret-test-key-12345",
            SelectedModelName = "Google Gemini"
        };

        // Act
        await _storageService.SaveAiConfigAsync(config);
        var loaded = await _storageService.LoadAiConfigAsync();

        // Assert
        Assert.That(loaded.SelectedProvider, Is.EqualTo(AiProviderType.GoogleGemini));
        Assert.That(loaded.ApiKey, Is.EqualTo("secret-test-key-12345"));
        Assert.That(loaded.SelectedModelName, Is.EqualTo("Google Gemini"));
    }

    [Test]
    public async Task LoadDocuments_WhenFileDoesNotExistOnDisk_ShouldMarkAsMissing()
    {
        // Arrange
        var profileName = "Standard";
        var existingFilePath = Path.Combine(_tempDirectory, "ExistingFile.pdf");
        await File.WriteAllTextAsync(existingFilePath, "dummy pdf content");

        var missingFilePath = Path.Combine(_tempDirectory, "DeletedFile.pdf");

        var documents = new List<DocumentItem>
        {
            new() { FileName = "ExistingFile.pdf", FilePath = existingFilePath, ExtractedChunkCount = 5 },
            new() { FileName = "DeletedFile.pdf", FilePath = missingFilePath, ExtractedChunkCount = 3 }
        };

        // Act - Jetzt mit Profilname aufgerufen
        await _storageService.SaveDocumentsAsync(profileName, documents);
        var loaded = await _storageService.LoadDocumentsAsync(profileName);

        // Assert
        var existingDoc = loaded.FirstOrDefault(d => d.FileName == "ExistingFile.pdf");
        var missingDoc = loaded.FirstOrDefault(d => d.FileName == "DeletedFile.pdf");

        Assert.That(existingDoc, Is.Not.Null);
        Assert.That(existingDoc!.IsMissing, Is.False);

        Assert.That(missingDoc, Is.Not.Null);
        Assert.That(missingDoc!.IsMissing, Is.True);
    }

    [Test]
    public async Task SaveAndLoadProfiles_ShouldPersistDistinctDocumentsPerProfile()
    {
        // Arrange: Zwei unterschiedliche Themen-Profile
        var profileLaw = "Recht";
        var profileDev = "Programmierung";

        var lawDocs = new List<DocumentItem>
    {
        new() { FileName = "BGB_Skript.pdf", FilePath = Path.Combine(_tempDirectory, "BGB_Skript.pdf") }
    };

        var devDocs = new List<DocumentItem>
    {
        new() { FileName = "CSharp_Guide.pdf", FilePath = Path.Combine(_tempDirectory, "CSharp_Guide.pdf") },
        new() { FileName = "DesignPatterns.pdf", FilePath = Path.Combine(_tempDirectory, "DesignPatterns.pdf") }
    };

        // Act
        await _storageService.SaveDocumentsAsync(profileLaw, lawDocs);
        await _storageService.SaveDocumentsAsync(profileDev, devDocs);

        var loadedLaw = await _storageService.LoadDocumentsAsync(profileLaw);
        var loadedDev = await _storageService.LoadDocumentsAsync(profileDev);

        // Assert: Profile müssen strikt getrennt sein
        Assert.That(loadedLaw.Count, Is.EqualTo(1));
        Assert.That(loadedLaw.First().FileName, Is.EqualTo("BGB_Skript.pdf"));

        Assert.That(loadedDev.Count, Is.EqualTo(2));
        Assert.That(loadedDev.Any(d => d.FileName == "CSharp_Guide.pdf"), Is.True);
    }

    [Test]
    public async Task ActiveProfile_ShouldBeSavedAndRetrieved()
    {
        // Arrange
        var profileName = "Prüfung 2026";

        // Act
        await _storageService.SetLastActiveProfileNameAsync(profileName);
        var activeProfile = await _storageService.GetLastActiveProfileNameAsync();

        // Assert
        Assert.That(activeProfile, Is.EqualTo(profileName));
    }

    [Test]
    public async Task GetAllProfileNamesAsync_ShouldReturnSavedProfiles()
    {
        // Arrange - _storageService statt new JsonStorageService() nutzen
        var docs = new List<DocumentItem> { new() { FileName = "Test.pdf", FilePath = "C:/Test.pdf" } };
        await _storageService.SaveDocumentsAsync("Mathe", docs);
        await _storageService.SaveDocumentsAsync("Physik", docs);

        // Act
        var profiles = await _storageService.GetAllProfileNamesAsync();

        // Assert
        Assert.That(profiles, Does.Contain("Mathe"));
        Assert.That(profiles, Does.Contain("Physik"));
    }

    [Test]
    public async Task DeleteProfileAsync_ShouldRemoveProfileFile()
    {
        // Arrange - _storageService statt new JsonStorageService() nutzen
        var docs = new List<DocumentItem> { new() { FileName = "Test.pdf", FilePath = "C:/Test.pdf" } };
        await _storageService.SaveDocumentsAsync("ZuLoeschen", docs);

        // Act
        await _storageService.DeleteProfileAsync("ZuLoeschen");
        var profiles = await _storageService.GetAllProfileNamesAsync();

        // Assert
        Assert.That(profiles, Does.Not.Contain("ZuLoeschen"));
    }
}