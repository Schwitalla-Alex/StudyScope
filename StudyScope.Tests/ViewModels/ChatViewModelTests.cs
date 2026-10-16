using Moq;
using NUnit.Framework;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

[TestFixture]
public class ChatViewModelTests
{
    private Mock<ILlmService> _llmServiceMock;
    private Mock<IStorageService> _storageServiceMock;

    private Mock<IDocumentService>? _pdfServiceMock;
    private DocumentListViewModel _documentListViewModel;

    [SetUp]
    public void SetUp()
    {
        _llmServiceMock = new Mock<ILlmService>();
        _storageServiceMock = new Mock<IStorageService>();
        _pdfServiceMock = new Mock<IDocumentService>();

        _documentListViewModel = new DocumentListViewModel(_storageServiceMock.Object, _pdfServiceMock.Object);
    }

    [Test]
    public async Task SendPromptAsync_WhenPromptIsEmpty_ShouldNotCallLlm()
    {
        // Arrange
        var chatVm = new ChatViewModel(_llmServiceMock.Object, _pdfServiceMock.Object, _documentListViewModel);
        chatVm.CurrentInputPrompt = "   ";

        // Act
        await chatVm.SendPromptAsync();

        // Assert
        _llmServiceMock.Verify(x => x.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        Assert.That(chatVm.Messages, Is.Empty);
    }

    [Test]
    public async Task SendPromptAsync_ShouldAddUserAndAssistantMessages()
    {
        // Arrange
        var question = "Was besagt § 12?";
        var answer = "§ 12 regelt die Frist.";

        _llmServiceMock.Setup(x => x.AskQuestionAsync(question, It.IsAny<IEnumerable<string>>()))
                       .ReturnsAsync(answer);

        var chatVm = new ChatViewModel(_llmServiceMock.Object, _pdfServiceMock.Object, _documentListViewModel);
        chatVm.CurrentInputPrompt = question;

        // Act
        await chatVm.SendPromptAsync();

        // Assert
        Assert.That(chatVm.Messages.Count, Is.EqualTo(2));
        Assert.That(chatVm.Messages[0].Role, Is.EqualTo(MessageRole.User));
        Assert.That(chatVm.Messages[0].Content, Is.EqualTo(question));
        Assert.That(chatVm.Messages[1].Role, Is.EqualTo(MessageRole.Assistant));
        Assert.That(chatVm.Messages[1].Content, Is.EqualTo(answer));
        Assert.That(chatVm.CurrentInputPrompt, Is.Empty);
    }

    [Test]
    public void ExportConversationToText_ShouldFormatAllMessages()
    {
        // Arrange
        var chatVm = new ChatViewModel(_llmServiceMock.Object, _pdfServiceMock.Object, _documentListViewModel);
        chatVm.Messages.Add(new ChatMessage { Role = MessageRole.User, Content = "Hallo" });
        chatVm.Messages.Add(new ChatMessage { Role = MessageRole.Assistant, Content = "Guten Tag!", IsLiked = true });

        // Act
        var exportText = chatVm.GetConversationExportText();

        // Assert
        Assert.That(exportText, Does.Contain("BENUTZER:"));
        Assert.That(exportText, Does.Contain("Hallo"));
        Assert.That(exportText, Does.Contain("ASSISTENT (KI):"));
        Assert.That(exportText, Does.Contain("Guten Tag!"));
        Assert.That(exportText, Does.Contain("👍 Hilfreich / Gut"));
    }

    /// <summary>
    /// ViewModel-Logik: Wir erweitern ChatViewModel um eine Export-Methode, die prüft, ob Nachrichten vorliegen. Falls nicht, 
    /// wird ein entsprechendes Feedback gesetzt oder ein Event ausgelöst; falls Nachrichten vorhanden sind, 
    /// wird die Datei im Standard-Exportpfad (oder Dokumentenordner) als .txt abgelegt. 
    /// UI-Rückmeldung: 
    ///     In ChatArea.razor blenden wir bei leerem Verlauf eine benutzerfreundliche Hinweis-Meldung (oder via JavaScript alert()) ein und
    ///     zeigen nach erfolgreichem Export den Pfad der gespeicherten Datei an.
    /// </summary>
    [Test]
    public async Task ExportConversationToFileAsync_WhenMessagesEmpty_ShouldReturnFalseAndSetNotification()
    {
        // Arrange
        var chatVm = new ChatViewModel(_llmServiceMock.Object, _pdfServiceMock.Object, _documentListViewModel);

        // Act
        var success = await chatVm.ExportConversationToFileAsync();

        // Assert
        Assert.That(success, Is.False);
        Assert.That(chatVm.ExportStatusMessage, Does.Contain("kein Gesprächsverlauf"));
    }

    [Test]
    public async Task ExportConversationToFileAsync_WhenMessagesExist_ShouldCreateFileAndReturnTrue()
    {
        // Arrange
        var chatVm = new ChatViewModel(_llmServiceMock.Object, _pdfServiceMock.Object, _documentListViewModel);
        chatVm.Messages.Add(new ChatMessage { Role = MessageRole.User, Content = "Testfrage" });
        chatVm.Messages.Add(new ChatMessage { Role = MessageRole.Assistant, Content = "Testantwort" });

        // Act
        var success = await chatVm.ExportConversationToFileAsync();

        // Assert
        Assert.That(success, Is.True);
        Assert.That(chatVm.ExportStatusMessage, Does.Contain("erfolgreich gespeichert"));
    }

    [Test]
    public void ClearConversation_ShouldClearMessagesAndResetStatusMessage()
    {
        // Arrange
        var chatVm = new ChatViewModel(_llmServiceMock.Object, _pdfServiceMock.Object, _documentListViewModel);
        chatVm.Messages.Add(new ChatMessage { Content = "Test" });
        chatVm.ExportStatusMessage = "Vorherige Meldung";

        // Act
        chatVm.ClearConversation();

        // Assert
        Assert.That(chatVm.Messages, Is.Empty);
        Assert.That(chatVm.ExportStatusMessage, Is.Empty);
    }

}