using System.Globalization;
using Moq;
using NUnit.Framework;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>
/// Websuche im Chat (Phase 4-2): Ohne Häkchen ändert sich nichts (byte-gleich, Suchdienst nie aufgerufen); mit Häkchen
/// geht nur die selbst eingegebene Frage ins Netz, Unterlagen und Internet werden getrennt beschriftet, Fehler sind
/// sichtbar, Fragen aus der Mindmap suchen nie.
/// </summary>
[TestFixture]
public class ChatWebSearchTests
{
    private const string Profile = "Standard";
    private string _dir = null!;
    private Mock<ILlmService> _llm = null!;
    private Mock<IStorageService> _storage = null!;
    private Mock<IDocumentService> _documents = null!;
    private Mock<IWebSearchService> _web = null!;
    private DocumentListViewModel _docVm = null!;
    private string? _prompt;
    private List<string>? _chunks;

    private static readonly string[] BiologyChunks =
    {
        "Die Photosynthese findet in den Chloroplasten statt und erzeugt Glukose.",
        "Die Zellatmung setzt Energie frei."
    };

    private static readonly string[] GeneticsChunks = { "Die Photosynthese wird in der Genetik nicht behandelt." };

    private static readonly WebSearchResult[] Results =
    {
        new("Photosynthese – Überblick", "https://example.org/photosynthese", "Ausschnitt eins."),
        new("Aktuelle Forschung", "https://www.example.org/forschung", "Ausschnitt zwei.")
    };

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "StudyScope_ChatWeb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var biology = Path.Combine(_dir, "biologie.txt");
        var genetics = Path.Combine(_dir, "genetik.txt");
        File.WriteAllText(biology, "x");
        File.WriteAllText(genetics, "x");

        _llm = new Mock<ILlmService>();
        _llm.Setup(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, CancellationToken>((p, c, _) => { _prompt = p; _chunks = c.ToList(); })
            .ReturnsAsync("Antwort [D1] [W1]");

        _documents = new Mock<IDocumentService>();
        _documents.Setup(d => d.ExtractChunksAsync(biology, It.IsAny<int>())).ReturnsAsync(BiologyChunks.ToList());
        _documents.Setup(d => d.ExtractChunksAsync(genetics, It.IsAny<int>())).ReturnsAsync(GeneticsChunks.ToList());

        _storage = new Mock<IStorageService>();
        _web = new Mock<IWebSearchService>();
        _web.Setup(w => w.GetProviderNameAsync()).ReturnsAsync("Tavily");
        _web.Setup(w => w.IsConfiguredAsync()).ReturnsAsync(true);
        _web.Setup(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Results);

        _docVm = new DocumentListViewModel(_storage.Object, _documents.Object, _web.Object);
        _docVm.Documents.Add(new DocumentItem { FileName = "biologie.txt", FilePath = biology, IsSelected = true });
        _docVm.Documents.Add(new DocumentItem { FileName = "genetik.txt", FilePath = genetics, IsSelected = true });
        _prompt = null;
        _chunks = null;
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* Temp-Ordner */ }
    }

    private void WebSearchState(bool enabled, string? acceptedFor = "Tavily") =>
        _storage.Setup(s => s.LoadWebSearchStateAsync(Profile)).ReturnsAsync(new WebSearchProfileState(enabled, acceptedFor));

    private ChatViewModel Chat(string? outputLanguage = null)
    {
        var vm = new ChatViewModel(_llm.Object, _documents.Object, _docVm) { WebSearch = _web.Object };
        if (outputLanguage != null)
        {
            var provider = new Mock<IOutputLanguageProvider>();
            provider.Setup(p => p.GetSettingAsync(It.IsAny<string>())).ReturnsAsync(outputLanguage);
            vm.OutputLanguageProvider = provider.Object;
        }
        return vm;
    }

    private static async Task Ask(ChatViewModel vm, string question, ChatQuestionOrigin origin = ChatQuestionOrigin.User)
    {
        vm.CurrentInputPrompt = question;
        await vm.SendPromptAsync(origin);
    }

    // ------------------------------------------------------------------ Häkchen aus: exakt wie bisher

    [Test]
    public async Task Off_PromptAndChunksUnchanged_SearchNeverCalled()
    {
        WebSearchState(enabled: false);
        var vm = Chat("de");

        await Ask(vm, "Wo findet die Photosynthese statt?");

        // Fest eingetragen (Stand 1.2.0): Frage unverändert, Auszüge ohne Beschriftung in der Reihenfolge der Bewertung
        Assert.That(_prompt, Is.EqualTo("Wo findet die Photosynthese statt?"));
        Assert.That(_chunks, Is.EqualTo(new[]
        {
            "Die Photosynthese findet in den Chloroplasten statt und erzeugt Glukose.",
            "Die Photosynthese wird in der Genetik nicht behandelt.",
            "Die Zellatmung setzt Energie frei."
        }));
        _web.Verify(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(vm.Messages.Select(m => m.Role), Is.EqualTo(new[] { MessageRole.User, MessageRole.Assistant }));
        Assert.That(vm.Messages[1].Sources, Is.Empty);
    }

    [Test]
    public async Task Off_EnglishLanguageLine_Unchanged()
    {
        WebSearchState(enabled: false);
        var vm = Chat("en");

        await Ask(vm, "Where does photosynthesis take place?");

        Assert.That(_prompt, Is.EqualTo("Where does photosynthesis take place?\n\nSprache der Antwort: Englisch (English). " + OutputLanguage.LineRule));
        Assert.That(_chunks!.Any(c => c.StartsWith("[D", StringComparison.Ordinal)), Is.False);
        _web.Verify(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task WithoutWebSearchService_BehavesAsOff()
    {
        WebSearchState(enabled: true);
        var vm = new ChatViewModel(_llm.Object, _documents.Object, _docVm);

        await Ask(vm, "Wo findet die Photosynthese statt?");

        Assert.That(_prompt, Is.EqualTo("Wo findet die Photosynthese statt?"));
        _web.Verify(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task NoticeForOtherProvider_SearchesNot()
    {
        // Häkchen gespeichert, Hinweis aber für SearXNG bestätigt – jetzt eingerichtet ist Tavily
        WebSearchState(enabled: true, acceptedFor: "SearXNG");
        var vm = Chat();

        await Ask(vm, "Wo findet die Photosynthese statt?");

        _web.Verify(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(_docVm.IsWebSearchActive, Is.False);
    }

    // ------------------------------------------------------------------ Häkchen an

    [Test]
    public async Task On_QueryIsOnlyTheUserQuestion()
    {
        WebSearchState(enabled: true);
        WebSearchQuery? query = null;
        _web.Setup(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()))
            .Callback<WebSearchQuery, CultureInfo, CancellationToken>((q, _, _) => query = q)
            .ReturnsAsync(Results);
        var vm = Chat("en");

        await Ask(vm, "  Wo findet die\nPhotosynthese statt?  ");

        Assert.That(query, Is.Not.Null);
        Assert.That(query!.Text, Is.EqualTo("Wo findet die Photosynthese statt?"));
        Assert.That(query.Text, Does.Not.Contain("biologie").And.Not.Contain("Chloroplasten").And.Not.Contain("Sprache der Antwort")
            .And.Not.Contain(Profile).And.Not.Contain(WebContextPrompt.MarkerToken));
    }

    [Test]
    public async Task On_SourcesLabelledAndSeparated()
    {
        WebSearchState(enabled: true);
        var vm = Chat("de");

        await Ask(vm, "Wo findet die Photosynthese statt?");

        Assert.That(_prompt, Is.EqualTo("Wo findet die Photosynthese statt?\n\n" + WebContextPrompt.MarkerLine));
        Assert.That(_chunks, Has.Count.EqualTo(5));
        Assert.That(_chunks![0], Is.EqualTo("[D1] Unterlage: biologie.txt\nDie Photosynthese findet in den Chloroplasten statt und erzeugt Glukose."));
        Assert.That(_chunks[1], Does.StartWith("[D2] Unterlage: genetik.txt\n"));
        Assert.That(_chunks[2], Does.StartWith("[D1] Unterlage: biologie.txt\n"), "gleiche Datei = gleiche Marke");
        Assert.That(_chunks[3], Does.StartWith("[W1] Internet: Photosynthese – Überblick – example.org\n" + WebContextPrompt.WebBegin));
        Assert.That(_chunks[4], Does.StartWith("[W2] Internet: Aktuelle Forschung – example.org\n"));
        Assert.That(_chunks[3], Does.Not.Contain("https://"), "Adressen gehen nicht an die KI");

        var answer = vm.Messages.Last();
        Assert.That(answer.Role, Is.EqualTo(MessageRole.Assistant));
        Assert.That(answer.Sources.Select(s => (s.Kind, s.Label, s.Name)), Is.EqualTo(new[]
        {
            (ChatSourceKind.Document, "D1", "biologie.txt"),
            (ChatSourceKind.Document, "D2", "genetik.txt"),
            (ChatSourceKind.Web, "W1", "Photosynthese – Überblick"),
            (ChatSourceKind.Web, "W2", "Aktuelle Forschung")
        }));
        Assert.That(answer.Sources[2].Url, Is.EqualTo("https://example.org/photosynthese"));
        Assert.That(answer.Sources[3].Domain, Is.EqualTo("example.org"));
    }

    [Test]
    public async Task On_LanguageLineExactlyOnce_AfterMarker()
    {
        WebSearchState(enabled: true);
        CultureInfo? searchLanguage = null;
        _web.Setup(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()))
            .Callback<WebSearchQuery, CultureInfo, CancellationToken>((_, l, _) => searchLanguage = l)
            .ReturnsAsync(Results);
        var vm = Chat("en");

        await Ask(vm, "Where does photosynthesis take place?");

        Assert.That(_prompt, Is.EqualTo("Where does photosynthesis take place?\n\n" + WebContextPrompt.MarkerLine + "\n\n"
                                        + OutputLanguage.PromptLine(CultureInfo.GetCultureInfo("en"))));
        Assert.That(System.Text.RegularExpressions.Regex.Matches(_prompt!, OutputLanguage.LinePrefix).Count, Is.EqualTo(1));
        Assert.That(searchLanguage?.TwoLetterISOLanguageName, Is.EqualTo("en"));
    }

    [Test]
    public async Task On_SearchFails_VisibleNotice_AnswerFromDocuments()
    {
        WebSearchState(enabled: true);
        _web.Setup(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebSearchException("Das Kontingent ist aufgebraucht.", "Tavily", 432));
        var vm = Chat("de");

        await Ask(vm, "Wo findet die Photosynthese statt?");

        Assert.That(vm.Messages.Select(m => m.Role), Is.EqualTo(new[] { MessageRole.User, MessageRole.System, MessageRole.Assistant }));
        Assert.That(vm.Messages[1].Content, Does.StartWith("Websuche nicht verfügbar: Das Kontingent ist aufgebraucht."));
        Assert.That(_prompt, Is.EqualTo("Wo findet die Photosynthese statt?"), "Antwort nur aus den Unterlagen, wie ohne Websuche");
        Assert.That(vm.Messages[2].Sources, Is.Empty);
    }

    [Test]
    public async Task On_NoResults_VisibleNotice()
    {
        WebSearchState(enabled: true);
        _web.Setup(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WebSearchResult>());
        var vm = Chat("de");

        await Ask(vm, "Wo findet die Photosynthese statt?");

        Assert.That(vm.Messages[1].Role, Is.EqualTo(MessageRole.System));
        Assert.That(vm.Messages[1].Content, Does.Contain("Keine Web-Treffer"));
        Assert.That(_prompt, Is.EqualTo("Wo findet die Photosynthese statt?"));
    }

    [Test]
    public async Task On_CancelDuringSearch_StopsWithoutAnswer()
    {
        WebSearchState(enabled: true);
        var vm = Chat("de");
        _web.Setup(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()))
            .Returns<WebSearchQuery, CultureInfo, CancellationToken>(async (_, _, ct) =>
            {
                vm.CancelPrompt();
                await Task.Delay(Timeout.Infinite, ct);
                return Results;
            });

        await Ask(vm, "Wo findet die Photosynthese statt?");

        _llm.Verify(l => l.AskQuestionAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(vm.Messages.Last().Role, Is.EqualTo(MessageRole.System));
        Assert.That(vm.Messages.Last().Content, Is.EqualTo(StudyScope.ViewModels.Resources.VmStrings.Chat_Cancelled));
        Assert.That(vm.IsBusy, Is.False);
    }

    // ------------------------------------------------------------------ Mindmap: nie im Internet suchen

    [Test]
    public async Task MindmapQuestion_NeverSearches_ShowsNotice()
    {
        WebSearchState(enabled: true);
        var vm = Chat("de");

        await Ask(vm, "Erkläre mir „Photosynthese“ im Zusammenhang mit „Energie“.", ChatQuestionOrigin.Mindmap);

        _web.Verify(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(vm.Messages[0].Origin, Is.EqualTo(ChatQuestionOrigin.Mindmap));
        Assert.That(vm.Messages[1].Role, Is.EqualTo(MessageRole.System));
        Assert.That(vm.Messages[1].Content, Is.EqualTo("Ohne Websuche, weil die Frage aus der Mindmap stammt. Zum Suchen im Internet die Frage selbst eingeben."));
        Assert.That(_prompt, Is.EqualTo("Erkläre mir „Photosynthese“ im Zusammenhang mit „Energie“."));
    }

    [Test]
    public async Task MindmapQuestion_WithWebSearchOff_NoNotice()
    {
        WebSearchState(enabled: false);
        var vm = Chat("de");

        await Ask(vm, "Erkläre mir „Photosynthese“.", ChatQuestionOrigin.Mindmap);

        Assert.That(vm.Messages.Select(m => m.Role), Is.EqualTo(new[] { MessageRole.User, MessageRole.Assistant }));
    }

    [Test]
    public async Task PendingMindmapPrompt_KeepsOrigin_EvenWhenExtended()
    {
        WebSearchState(enabled: true);
        var vm = Chat("de");

        vm.SetPendingPrompt("Erkläre mir „Photosynthese“.", ChatQuestionOrigin.Mindmap);
        vm.CurrentInputPrompt += " Bitte kurz.";
        await vm.SendPromptAsync();

        _web.Verify(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(vm.Messages[0].Origin, Is.EqualTo(ChatQuestionOrigin.Mindmap));

        // Danach selbst eingegeben: sucht wieder
        await Ask(vm, "Was sagt das Internet dazu?");
        _web.Verify(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Regenerate_KeepsMindmapOrigin_AndDoesNotDuplicateNotice()
    {
        WebSearchState(enabled: true);
        var vm = Chat("de");
        await Ask(vm, "Erkläre mir „Photosynthese“.", ChatQuestionOrigin.Mindmap);

        await vm.RegenerateLastResponseAsync();

        _web.Verify(w => w.SearchAsync(It.IsAny<WebSearchQuery>(), It.IsAny<CultureInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(vm.Messages.Select(m => m.Role), Is.EqualTo(new[] { MessageRole.User, MessageRole.System, MessageRole.Assistant }));
        Assert.That(vm.Messages[0].Origin, Is.EqualTo(ChatQuestionOrigin.Mindmap));
    }

    // ------------------------------------------------------------------ Export: Quellen ohne Ausschnitte

    [Test]
    public async Task Export_ListsSources_WithoutSnippets()
    {
        WebSearchState(enabled: true);
        var vm = Chat("de");
        await Ask(vm, "Wo findet die Photosynthese statt?");

        var text = vm.GetConversationExportText();

        Assert.That(text, Does.Contain("Quellen:"));
        Assert.That(text, Does.Contain("[D1] biologie.txt"));
        Assert.That(text, Does.Contain("[W1] Photosynthese – Überblick – https://example.org/photosynthese"));
        Assert.That(text, Does.Not.Contain("Ausschnitt eins"));
    }

    // ------------------------------------------------------------------ Häkchen (DocumentListViewModel)

    [Test]
    public async Task Toggle_FirstTime_NeedsNotice_ThenAcceptEnables()
    {
        WebSearchProfileState? saved = null;
        _storage.Setup(s => s.LoadWebSearchStateAsync(Profile)).ReturnsAsync(() => saved ?? WebSearchProfileState.Off);
        _storage.Setup(s => s.SaveWebSearchStateAsync(Profile, It.IsAny<WebSearchProfileState>()))
            .Callback<string, WebSearchProfileState>((_, s) => saved = s).Returns(Task.CompletedTask);
        _storage.Setup(s => s.LoadAiConfigForProfileAsync(Profile)).ReturnsAsync(new AiConfig { SelectedProvider = AiProviderType.OllamaLocal });

        var result = await _docVm.RequestWebSearchAsync(true);

        Assert.That(result, Is.EqualTo(WebSearchToggleResult.NeedsNotice));
        Assert.That(saved, Is.Null, "vor dem Hinweis wird nichts gespeichert");
        Assert.That(_docVm.IsWebSearchActive, Is.False);
        Assert.That(_docVm.WebSearchProviderName, Is.EqualTo("Tavily"));
        Assert.That(_docVm.IsLocalChatAi, Is.True);

        await _docVm.AcceptWebSearchNoticeAsync("Tavily");

        Assert.That(saved, Is.EqualTo(new WebSearchProfileState(true, "Tavily")));
        Assert.That(_docVm.IsWebSearchActive, Is.True);

        // Aus und wieder an: kein zweiter Hinweis
        Assert.That(await _docVm.RequestWebSearchAsync(false), Is.EqualTo(WebSearchToggleResult.Disabled));
        Assert.That(saved, Is.EqualTo(new WebSearchProfileState(false, "Tavily")));
        Assert.That(await _docVm.RequestWebSearchAsync(true), Is.EqualTo(WebSearchToggleResult.Enabled));
        Assert.That(_docVm.IsWebSearchActive, Is.True);
    }

    [Test]
    public async Task Toggle_ProviderChanged_NeedsNoticeAgain()
    {
        WebSearchState(enabled: false, acceptedFor: "SearXNG");

        Assert.That(await _docVm.RequestWebSearchAsync(true), Is.EqualTo(WebSearchToggleResult.NeedsNotice));
    }

    [Test]
    public async Task Toggle_NotConfigured_OpensSettings_SavesNothing()
    {
        WebSearchState(enabled: false, acceptedFor: null);
        _web.Setup(w => w.GetProviderNameAsync()).ReturnsAsync(string.Empty);

        Assert.That(await _docVm.RequestWebSearchAsync(true), Is.EqualTo(WebSearchToggleResult.NotConfigured));
        _storage.Verify(s => s.SaveWebSearchStateAsync(It.IsAny<string>(), It.IsAny<WebSearchProfileState>()), Times.Never);
        Assert.That(_docVm.IsWebSearchActive, Is.False);
    }

    [Test]
    public async Task ProfileSwitch_LoadsStateOfNewProfile_NewProfileIsOff()
    {
        _storage.Setup(s => s.LoadWebSearchStateAsync("Testprofil A")).ReturnsAsync(new WebSearchProfileState(true, "Tavily"));
        _storage.Setup(s => s.LoadWebSearchStateAsync("Testprofil B")).ReturnsAsync(WebSearchProfileState.Off);
        _storage.Setup(s => s.LoadDocumentsAsync(It.IsAny<string>())).ReturnsAsync(new List<DocumentItem>());

        await _docVm.SwitchProfileAsync("Testprofil A");
        Assert.That(_docVm.IsWebSearchActive, Is.True);

        await _docVm.SwitchProfileAsync("Testprofil B");
        Assert.That(_docVm.IsWebSearchActive, Is.False);

        await _docVm.CreateNewProfileAsync("Testprofil C");
        Assert.That(_docVm.IsWebSearchActive, Is.False, "ein neues Profil sucht nie im Internet");
    }
}
