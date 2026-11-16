using Moq;
using NUnit.Framework;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Gesprächsverlauf je Profil, Stichwortsuche (auch mit „deutschen“ Anführungszeichen) und Fehler-/Abbruchmeldungen im Chat.
/// </summary>
[TestFixture]
public class ChatViewModelProfileTests
{
    private Mock<ILlmService> _llm = null!;
    private Mock<IStorageService> _storage = null!;
    private Mock<IDocumentService> _documents = null!;
    private DocumentListViewModel _docVm = null!;
    private ChatViewModel _chat = null!;

    [SetUp]
    public void SetUp()
    {
        _llm = new Mock<ILlmService>();
        _storage = new Mock<IStorageService>();
        _storage.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>())).ReturnsAsync(new List<DocumentItem>());
        _storage.Setup(s => s.LoadAiConfigForProfileAsync(It.IsAny<string>())).ReturnsAsync(new AiConfig());
        _documents = new Mock<IDocumentService>();
        _docVm = new DocumentListViewModel(_storage.Object, _documents.Object);
        _chat = new ChatViewModel(_llm.Object, _documents.Object, _docVm);
    }

    private Task AskAsync(string text)
    {
        _chat.CurrentInputPrompt = text;
        return _chat.SendPromptAsync();
    }

    [Test]
    public async Task Profilwechsel_ZeigtNurDenVerlaufDesAktivenProfils()
    {
        _llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Antwort");
        await AskAsync("Frage im Profil A");
        Assert.That(_chat.Messages, Has.Count.EqualTo(2));

        await _docVm.SwitchProfileAsync("Testprofil B");
        Assert.That(_chat.Messages, Is.Empty, "kein Verlauf aus einem anderen Profil sichtbar");
        Assert.That(_chat.ActiveProfile, Is.EqualTo("Testprofil B"));

        await AskAsync("Frage im Profil B");
        await _docVm.SwitchProfileAsync("Standard");

        Assert.That(_chat.Messages.Select(m => m.Content), Does.Contain("Frage im Profil A").And.Not.Contain("Frage im Profil B"));
    }

    [Test]
    public async Task AntwortNachProfilwechsel_LandetImUrspruenglichenProfil()
    {
        var pending = new TaskCompletionSource<string>();
        _llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(pending.Task);

        var sending = AskAsync("Langsame Frage");
        await _docVm.SwitchProfileAsync("Testprofil B");
        pending.SetResult("Späte Antwort");
        await sending;

        Assert.That(_chat.Messages, Is.Empty);
        await _docVm.SwitchProfileAsync("Standard");
        Assert.That(_chat.Messages.Select(m => m.Content), Is.EqualTo(new[] { "Langsame Frage", "Späte Antwort" }));
    }

    [Test]
    public async Task DienstFehler_ErscheintAlsVerstaendlicheSystemmeldung()
    {
        _llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(LlmServiceException.FromHttpStatus("Mistral AI", 503, "{}"));

        await AskAsync("Frage");

        var last = _chat.Messages.Last();
        Assert.That(last.Role, Is.EqualTo(MessageRole.System));
        Assert.That(last.Content, Does.Contain("Mistral AI").And.Contain("HTTP 503"));
    }

    [Test]
    public async Task Abbruch_AlsTextVomDienst_WirdAlsSystemmeldungAngezeigt()
    {
        _llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns<string, IEnumerable<string>, CancellationToken>(async (_, _, token) =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
                catch (OperationCanceledException) { }
                return "[Anfrage durch Benutzer abgebrochen]";
            });

        var sending = AskAsync("Frage");
        _chat.CancelPrompt();
        await sending;

        var last = _chat.Messages.Last();
        Assert.That(last.Role, Is.EqualTo(MessageRole.System), "kein KI-Beitrag mit Bewertungsknöpfen");
        Assert.That(last.Content, Does.Contain("abgebrochen"));
    }

    [Test]
    public void ExtractSearchWords_TrenntAuchAnDeutschenAnfuehrungszeichen()
    {
        var words = ChatViewModel.ExtractSearchWords("Erkläre mir „Windows-Installation“ im Zusammenhang mit „Installationsanleitung“ anhand meiner Unterlagen.");

        Assert.That(words, Is.EquivalentTo(new[] { "windows", "installation", "installationsanleitung" }));
    }

    [Test]
    public void GetMostRelevantChunks_FindetDenPassendenAbschnittTrotzAnfuehrungszeichen()
    {
        var chunks = new List<string>
        {
            "Ollama läuft auf Port 11434, LM Studio auf Port 1234. API-Schlüssel für Cloud-Dienste eintragen.",
            "Installation unter Windows: ZIP-Archiv entpacken und StudyScope.exe starten. Der Ordner wwwroot muss bleiben.",
            "Profile trennen Lerninhalte nach Themen."
        };

        var result = ChatViewModel.GetMostRelevantChunks("Erkläre mir „Windows-Installation“ anhand meiner Unterlagen.", chunks, maxChunks: 2);

        Assert.That(result[0], Does.Contain("Installation unter Windows"));
        Assert.That(result, Has.Count.EqualTo(2), "mit weiteren Abschnitten aufgefüllt");
    }

    [Test]
    public void GetMostRelevantChunks_FuelltInDokumentreihenfolgeAuf()
    {
        var chunks = new List<string> { "eins", "zwei Treffer", "drei", "vier" };

        var result = ChatViewModel.GetMostRelevantChunks("Treffer", chunks, maxChunks: 3);

        Assert.That(result, Is.EqualTo(new[] { "zwei Treffer", "eins", "drei" }));
    }
}
