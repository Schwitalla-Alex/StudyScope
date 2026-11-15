using Moq;
using NUnit.Framework;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

[TestFixture]
public class LlmServiceDispatcherTests
{
    private Mock<IStorageService> _storageMock;

    [SetUp]
    public void SetUp()
    {
        _storageMock = new Mock<IStorageService>();
    }

    [Test]
    public async Task ValidateConnectionAsync_WhenFakeAi_ShouldReturnTrue()
    {
        // Arrange
        _storageMock.Setup(s => s.LoadAiConfigAsync())
                    .ReturnsAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi });

        var dispatcher = new LlmServiceDispatcher(_storageMock.Object, new HttpClient());

        // Act
        var result = await dispatcher.ValidateConnectionAsync();

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task AskQuestionAsync_WhenFakeAi_ShouldReturnMockAnswer()
    {
        // Arrange
        _storageMock.Setup(s => s.LoadAiConfigAsync())
                    .ReturnsAsync(new AiConfig { SelectedProvider = AiProviderType.FakeAi });

        var dispatcher = new LlmServiceDispatcher(_storageMock.Object, new HttpClient());

        // Act
        var answer = await dispatcher.AskQuestionAsync("Hallo", new List<string> { "Testkontext" });

        // Assert
        Assert.That(answer, Does.Contain("Testmodus"));
    }

    // Gemini ohne Key ist ohne Netzwerkzugriff "nicht verbunden", Fake-KI immer "verbunden":
    private static AiConfig GeminiOhneKey => new() { SelectedProvider = AiProviderType.GoogleGemini, ApiKey = "", SelectedModelName = "gemini-2.0-flash" };
    private static AiConfig FakeKi => new() { SelectedProvider = AiProviderType.FakeAi };

    [Test]
    public async Task AskQuestionAsync_NutztDieKonfigurationDesAktivenProfils_NichtDieGlobale()
    {
        _storageMock.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync("Testprofil");
        _storageMock.Setup(s => s.LoadAiConfigForProfileAsync("Testprofil")).ReturnsAsync(FakeKi);
        _storageMock.Setup(s => s.LoadAiConfigAsync()).ReturnsAsync(GeminiOhneKey);

        var dispatcher = new LlmServiceDispatcher(_storageMock.Object, new HttpClient());
        var answer = await dispatcher.AskQuestionAsync("Hallo", new List<string> { "Testkontext" });

        Assert.That(answer, Does.Contain("Testmodus"));
        _storageMock.Verify(s => s.LoadAiConfigAsync(), Times.Never);
    }

    [Test]
    public async Task ValidateConnectionAsync_FolgtDemProfilwechsel()
    {
        _storageMock.SetupSequence(s => s.GetLastActiveProfileNameAsync())
                    .ReturnsAsync("Profil A")
                    .ReturnsAsync("Profil B");
        _storageMock.Setup(s => s.LoadAiConfigForProfileAsync("Profil A")).ReturnsAsync(GeminiOhneKey);
        _storageMock.Setup(s => s.LoadAiConfigForProfileAsync("Profil B")).ReturnsAsync(FakeKi);

        var dispatcher = new LlmServiceDispatcher(_storageMock.Object, new HttpClient());

        Assert.That(await dispatcher.ValidateConnectionAsync(), Is.False, "Profil A (Gemini ohne Key)");
        Assert.That(await dispatcher.ValidateConnectionAsync(), Is.True, "Profil B (Fake-KI)");
    }

    [Test]
    public async Task ValidateConnectionAsync_ProfilOhneEinstellung_NutztGlobaleKonfiguration()
    {
        _storageMock.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync("Testprofil");
        _storageMock.Setup(s => s.LoadAiConfigForProfileAsync("Testprofil")).ReturnsAsync((AiConfig)null!);
        _storageMock.Setup(s => s.LoadAiConfigAsync()).ReturnsAsync(GeminiOhneKey);

        var dispatcher = new LlmServiceDispatcher(_storageMock.Object, new HttpClient());

        Assert.That(await dispatcher.ValidateConnectionAsync(), Is.False);
        _storageMock.Verify(s => s.LoadAiConfigAsync(), Times.Once);
    }

    [Test]
    public async Task ValidateConnectionAsync_MitUebergebenerKonfiguration_PrueftDieseOhneSpeicherzugriff()
    {
        var dispatcher = new LlmServiceDispatcher(_storageMock.Object, new HttpClient());

        Assert.That(await dispatcher.ValidateConnectionAsync(FakeKi), Is.True);
        Assert.That(await dispatcher.ValidateConnectionAsync(GeminiOhneKey), Is.False);
        _storageMock.VerifyNoOtherCalls();
    }
}