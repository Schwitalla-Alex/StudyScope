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

        // Standard-Rückgaben für Profil & Config im StorageMock definieren:
        _storageServiceMock.Setup(s => s.GetLastActiveProfileNameAsync())
                           .ReturnsAsync("Standard");

        _storageServiceMock.Setup(s => s.LoadAiConfigForProfileAsync(It.IsAny<string>()))
                           .ReturnsAsync(new AiConfig());

        _storageServiceMock.Setup(s => s.LoadAiConfigAsync())
                           .ReturnsAsync(new AiConfig());
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
            SelectedModelName = "gemini-2.5-flash" // Hier auf das neue Standard-Modell setzen
        };

        // Beide Methoden auf das existingConfig mappen:
        _storageServiceMock.Setup(s => s.LoadAiConfigAsync())
                           .ReturnsAsync(existingConfig);
        _storageServiceMock.Setup(s => s.LoadAiConfigForProfileAsync(It.IsAny<string>()))
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
        Assert.That(viewModel.SelectedModelName, Is.EqualTo("gemini-2.5-flash"));
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
            _discoveryServiceMock.Object);

        viewModel.SelectedProvider = AiProviderType.OpenAiGpt4;
        viewModel.ApiKey = "valid-api-key";

        // Modellname setzen und in AvailableModels registrieren:
        viewModel.AvailableModels.Add("gpt-4o-mini");
        viewModel.SelectedModelName = "gpt-4o-mini";

        // Act
        var result = await viewModel.SaveAndConnectAsync();

        // Assert
        Assert.That(result, Is.True);
        Assert.That(viewModel.ConnectionStatusMessage, Does.Contain("Erfolgreich verbunden"));

        // Prüfen auf SaveAiConfigForProfileAsync oder SaveAiConfigAsync:
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

    [Test]
    public void SelectedProvider_WhenSetToGoogleGemini_ShouldSelectDefaultModelIfEmpty()
    {
        // Arrange
        var viewModel = new SettingsViewModel(
            _storageServiceMock.Object,
            _llmServiceMock.Object,
            _discoveryServiceMock.Object);

        // Act
        viewModel.SelectedProvider = AiProviderType.GoogleGemini;

        // Assert
        Assert.That(viewModel.IsGeminiProvider, Is.True);
        Assert.That(viewModel.SelectedModelName, Is.EqualTo("gemini-2.5-flash"));
    }

    [Test]
    public void ResetForm_ShouldClearAllFieldsAndModels()
    {
        // Arrange
        var viewModel = new SettingsViewModel(
            _storageServiceMock.Object,
            _llmServiceMock.Object,
            _discoveryServiceMock.Object)
        {
            ApiKey = "some-key",
            LocalEndpointUrl = "http://localhost:1234",
            SelectedModelName = "my-model"
        };
        viewModel.AvailableModels.Add("my-model");

        // Act
        viewModel.ResetForm();

        // Assert
        Assert.That(viewModel.ApiKey, Is.Empty);
        Assert.That(viewModel.LocalEndpointUrl, Is.Empty);
        Assert.That(viewModel.SelectedModelName, Is.Empty);
        Assert.That(viewModel.AvailableModels, Is.Empty);
        Assert.That(viewModel.ConnectionStatusMessage, Is.Empty);
        Assert.That(viewModel.ConnectionSuccess, Is.False);
    }

    [Test]
    public void CanSaveAndConnect_ForLocalProvider_DoesNotRequireApiKey()
    {
        // Arrange
        var viewModel = new SettingsViewModel(
            _storageServiceMock.Object,
            _llmServiceMock.Object,
            _discoveryServiceMock.Object);

        // Act: LM Studio ohne API-Key einrichten
        viewModel.SelectedProvider = AiProviderType.LmStudioLocal;
        viewModel.LocalEndpointUrl = "http://localhost:1234";
        viewModel.AvailableModels.Add("local-model");
        viewModel.SelectedModelName = "local-model";
        viewModel.ApiKey = string.Empty; // Bewusst leer

        // Assert
        Assert.That(viewModel.IsApiKeyRequired, Is.False);
        Assert.That(viewModel.CanSaveAndConnect, Is.True);
    }

}