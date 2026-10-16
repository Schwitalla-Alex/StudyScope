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
}