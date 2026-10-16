using Moq;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;
using NUnit.Framework;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Die Tests zu SettingsViewModel, das die KI-Konfiguration steuert.
/// </summary>
[TestFixture]
public class SettingsViewModelTests
{
    private Mock<IStorageService> _storageServiceMock;
    private Mock<ILlmService> _llmServiceMock;
    private Mock<ILocalLlmDiscoveryService> _discoveryServiceMock;

    [SetUp]
    public void SetUp()
    {
        _storageServiceMock = new Mock<IStorageService>();
        _llmServiceMock = new Mock<ILlmService>();
        _discoveryServiceMock = new Mock<ILocalLlmDiscoveryService>();
    }

    [Test]
    public async Task InitializeAsync_ShouldLoadExistingConfig()
    {
        // Arrange
        var existingConfig = new AiConfig
        {
            SelectedProvider = AiProviderType.GoogleGemini,
            ApiKey = "my-secret-key",
            LocalEndpointUrl = "http://localhost:11434",
            SelectedModelName = "gemini-1.5-flash"
        };

        _storageServiceMock.Setup(s => s.LoadAiConfigAsync())
                           .ReturnsAsync(existingConfig);

        var viewModel = new SettingsViewModel(
            _storageServiceMock.Object,
            _llmServiceMock.Object,
            _discoveryServiceMock.Object);

        // Act
        await viewModel.InitializeAsync();

        // Assert
        Assert.That(viewModel.SelectedProvider, Is.EqualTo(AiProviderType.GoogleGemini));
        Assert.That(viewModel.ApiKey, Is.EqualTo("my-secret-key"));
    }

    [Test]
    public async Task SaveAndConnectAsync_WhenConnectionSucceeds_ShouldSaveAndSetStatusSuccess()
    {
        // Arrange
        _llmServiceMock.Setup(l => l.ValidateConnectionAsync())
                       .ReturnsAsync(true);

        var viewModel = new SettingsViewModel(
            _storageServiceMock.Object,
            _llmServiceMock.Object,
            _discoveryServiceMock.Object)
        {
            ApiKey = "valid-api-key",
            SelectedProvider = AiProviderType.OpenAiGpt4
        };

        // Act
        var result = await viewModel.SaveAndConnectAsync();

        // Assert
        Assert.That(result, Is.True);
        Assert.That(viewModel.ConnectionStatusMessage, Does.Contain("Erfolgreich verbunden"));
        _storageServiceMock.Verify(s => s.SaveAiConfigAsync(It.Is<AiConfig>(c =>
            c.ApiKey == "valid-api-key" && c.SelectedProvider == AiProviderType.OpenAiGpt4)), Times.Once);
    }

    [Test]
    public async Task SaveAndConnectAsync_WhenConnectionFails_ShouldSetStatusError()
    {
        // Arrange
        _llmServiceMock.Setup(l => l.ValidateConnectionAsync())
                       .ReturnsAsync(false);

        var viewModel = new SettingsViewModel(
            _storageServiceMock.Object,
            _llmServiceMock.Object,
            _discoveryServiceMock.Object)
        {
            ApiKey = "invalid-key",
            SelectedProvider = AiProviderType.GoogleGemini
        };

        // Act
        var result = await viewModel.SaveAndConnectAsync();

        // Assert
        Assert.That(result, Is.False);
        Assert.That(viewModel.ConnectionStatusMessage, Does.Contain("Verbindung fehlgeschlagen"));
        _storageServiceMock.Verify(s => s.SaveAiConfigAsync(It.IsAny<AiConfig>()), Times.Never);
    }

    [Test]
    public void IsApiKeyRequired_ShouldBeFalse_ForOllamaAndFakeAi()
    {
        // Arrange
        var viewModel = new SettingsViewModel(
            _storageServiceMock.Object,
            _llmServiceMock.Object,
            _discoveryServiceMock.Object);

        // Act & Assert - FakeAi
        viewModel.SelectedProvider = AiProviderType.FakeAi;
        Assert.That(viewModel.IsApiKeyRequired, Is.False);

        // Act & Assert - Ollama
        viewModel.SelectedProvider = AiProviderType.OllamaLocal;
        Assert.That(viewModel.IsApiKeyRequired, Is.False);

        // Act & Assert - Cloud-Dienste
        viewModel.SelectedProvider = AiProviderType.GoogleGemini;
        Assert.That(viewModel.IsApiKeyRequired, Is.True);

        viewModel.SelectedProvider = AiProviderType.OpenAiGpt4;
        Assert.That(viewModel.IsApiKeyRequired, Is.True);
    }
}