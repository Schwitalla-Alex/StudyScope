using Moq;
using NUnit.Framework;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

[TestFixture]
public class DocumentListViewModelProfileNameTests
{
    private Mock<IStorageService> _storageServiceMock = null!;
    private DocumentListViewModel _viewModel = null!;

    [SetUp]
    public void SetUp()
    {
        _storageServiceMock = new Mock<IStorageService>();
        _storageServiceMock.Setup(s => s.LoadAiConfigForProfileAsync(It.IsAny<string>())).ReturnsAsync(new AiConfig());
        _storageServiceMock.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>())).ReturnsAsync(new List<DocumentItem>());
        _viewModel = new DocumentListViewModel(_storageServiceMock.Object, new Mock<IDocumentService>().Object);
    }

    [TestCase("WSL/Test")]
    [TestCase("..\\evil")]
    [TestCase("CON")]
    [TestCase("")]
    [TestCase("Ende.")]
    public async Task CreateNewProfileAsync_UngueltigerName_WirdAbgelehntUndNichtGespeichert(string name)
    {
        var ergebnis = await _viewModel.CreateNewProfileAsync(name);

        Assert.That(ergebnis, Is.False);
        Assert.That(_viewModel.AvailableProfiles, Does.Not.Contain(name));
        Assert.That(_viewModel.CurrentProfile, Is.EqualTo("Standard"));
        _storageServiceMock.Verify(s => s.SaveDocumentsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<DocumentItem>>()), Times.Never);
        _storageServiceMock.Verify(s => s.SetLastActiveProfileNameAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task CreateNewProfileAsync_GueltigerName_WirdAngelegt()
    {
        var ergebnis = await _viewModel.CreateNewProfileAsync("  WSL Test  ");

        Assert.That(ergebnis, Is.True);
        Assert.That(_viewModel.CurrentProfile, Is.EqualTo("WSL Test"));
        _storageServiceMock.Verify(s => s.SaveDocumentsAsync("WSL Test", It.IsAny<IEnumerable<DocumentItem>>()), Times.Once);
    }

    [Test]
    public void ValidateProfileName_LiefertMeldungFuerOberflaeche()
    {
        Assert.That(_viewModel.ValidateProfileName("a/b"), Does.Contain("/"));
        Assert.That(_viewModel.ValidateProfileName("Gültig"), Is.Null);
    }
}
