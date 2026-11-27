using Moq;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.ViewModels;

namespace StudyScope.Tests.ViewModels;

/// <summary>Einstellungen › „🌐 Websuche“: Laden, Prüfen der Angaben, Testen mit ungespeicherten Werten, Speichern.</summary>
[TestFixture]
public class WebSearchSettingsViewModelTests
{
    private const string DummyKey = "DUMMY_TEST_KEY_TAVILY_VM";

    private Mock<IStorageService> _storage = null!;
    private Mock<IWebSearchService> _webSearch = null!;
    private Mock<IExternalLinkOpener> _links = null!;
    private WebSearchSettings _stored = null!;

    [SetUp]
    public void SetUp()
    {
        _stored = new WebSearchSettings { Provider = WebSearchProviderType.Tavily, TavilyApiKey = DummyKey, MaxResults = 4 };
        _storage = new Mock<IStorageService>();
        _storage.Setup(s => s.LoadWebSearchSettingsAsync()).ReturnsAsync(() => _stored.Clone());
        _storage.Setup(s => s.SaveWebSearchSettingsAsync(It.IsAny<WebSearchSettings>()))
            .Callback<WebSearchSettings>(s => _stored = s.Clone()).Returns(Task.CompletedTask);
        _webSearch = new Mock<IWebSearchService>();
        _webSearch.Setup(w => w.VerifyAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebSearchVerification.Ok());
        _links = new Mock<IExternalLinkOpener>();
    }

    private WebSearchSettingsViewModel Create() => new(_storage.Object, _webSearch.Object, _links.Object);

    [Test]
    public async Task Load_ShowsStoredValues()
    {
        var vm = Create();
        await vm.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.Provider, Is.EqualTo(WebSearchProviderType.Tavily));
            Assert.That(vm.IsTavily, Is.True);
            Assert.That(vm.TavilyApiKey, Is.EqualTo(DummyKey));
            Assert.That(vm.MaxResults, Is.EqualTo(4));
            Assert.That(vm.StatusMessage, Is.Null);
        });
    }

    [Test]
    public void ProviderOptions_ContainNoneTavilySearxng()
    {
        var vm = Create();
        Assert.That(vm.ProviderOptions.Select(o => o.Value),
            Is.EqualTo(new[] { WebSearchProviderType.None, WebSearchProviderType.Tavily, WebSearchProviderType.Searxng }));
        Assert.That(vm.ProviderOptions[0].Label, Is.EqualTo("Kein Suchdienst"));
    }

    [Test]
    public void MaxResultsOptions_OneToTen() =>
        Assert.That(Create().MaxResultsOptions, Is.EqualTo(Enumerable.Range(1, 10)));

    [Test]
    public async Task Save_WithoutKey_DoesNotSave()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.TavilyApiKey = "  ";

        Assert.That(await vm.SaveAsync(), Is.False);
        Assert.That(vm.IsSuccess, Is.False);
        Assert.That(vm.StatusMessage, Does.Contain("API-Schlüssel"));
        _storage.Verify(s => s.SaveWebSearchSettingsAsync(It.IsAny<WebSearchSettings>()), Times.Never);
    }

    [Test]
    public async Task Save_InvalidSearxngUrl_DoesNotSave()
    {
        var vm = Create();
        vm.Provider = WebSearchProviderType.Searxng;
        vm.SearxngUrl = "localhost:8888";

        Assert.That(await vm.SaveAsync(), Is.False);
        Assert.That(vm.StatusMessage, Does.Contain("SearXNG"));
        _storage.Verify(s => s.SaveWebSearchSettingsAsync(It.IsAny<WebSearchSettings>()), Times.Never);
    }

    [Test]
    public async Task Save_TrimsValues_AndStores()
    {
        var vm = Create();
        vm.Provider = WebSearchProviderType.Searxng;
        vm.SearxngUrl = "  http://localhost:8888  ";
        vm.MaxResults = 8;

        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.That(_stored.Provider, Is.EqualTo(WebSearchProviderType.Searxng));
        Assert.That(_stored.SearxngUrl, Is.EqualTo("http://localhost:8888"));
        Assert.That(_stored.MaxResults, Is.EqualTo(8));
        Assert.That(vm.IsSuccess, Is.True);
    }

    [Test]
    public async Task Save_NoProvider_IsAllowed()
    {
        var vm = Create();
        vm.Provider = WebSearchProviderType.None;
        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.That(_stored.Provider, Is.EqualTo(WebSearchProviderType.None));
    }

    [Test]
    public async Task Test_UsesUnsavedFormValues_WithoutSaving()
    {
        WebSearchSettings? tested = null;
        _webSearch.Setup(w => w.TestAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()))
            .Callback<WebSearchSettings, CancellationToken>((s, _) => tested = s).ReturnsAsync(3);
        var vm = Create();
        await vm.LoadAsync();
        vm.TavilyApiKey = " DUMMY_TEST_KEY_NEU ";

        await vm.TestAsync();

        Assert.That(tested!.TavilyApiKey, Is.EqualTo("DUMMY_TEST_KEY_NEU"));
        Assert.That(vm.IsSuccess, Is.True);
        Assert.That(vm.StatusMessage, Does.Contain("Tavily").And.Contain("3 Treffer"));
        _storage.Verify(s => s.SaveWebSearchSettingsAsync(It.IsAny<WebSearchSettings>()), Times.Never);
        Assert.That(vm.IsTesting, Is.False);
    }

    [Test]
    public async Task Test_ShowsServiceMessage()
    {
        _webSearch.Setup(w => w.TestAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(WebSearchException.FromHttpStatus("Tavily", 401, "technisch"));
        var vm = Create();
        await vm.LoadAsync();

        await vm.TestAsync();

        Assert.That(vm.IsSuccess, Is.False);
        Assert.That(vm.StatusMessage, Does.Contain("Schlüssel wurde abgelehnt"));
        Assert.That(vm.StatusMessage, Does.Not.Contain("technisch"));
    }

    [Test]
    public async Task Test_WithoutKey_DoesNotCallService()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.TavilyApiKey = string.Empty;

        await vm.TestAsync();

        Assert.That(vm.StatusMessage, Does.Contain("API-Schlüssel"));
        _webSearch.Verify(w => w.TestAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void Test_NotPossibleWithoutProvider()
    {
        var vm = Create();
        vm.Provider = WebSearchProviderType.None;
        Assert.That(vm.CanTest, Is.False);
    }

    [Test]
    public void ProviderChange_ClearsStatus_AndRaisesFlags()
    {
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Provider = WebSearchProviderType.Searxng;

        Assert.That(changed, Does.Contain(nameof(WebSearchSettingsViewModel.IsSearxng)));
        Assert.That(changed, Does.Contain(nameof(WebSearchSettingsViewModel.CanTest)));
        Assert.That(vm.StatusMessage, Is.Null);
    }

    [Test]
    public void OpenTavilySignup_UsesExternalLinkOpener()
    {
        _links.Setup(l => l.OpenUrl(It.IsAny<string>())).Returns(true);
        Assert.That(Create().OpenTavilySignup(), Is.True);
        _links.Verify(l => l.OpenUrl("https://app.tavily.com"), Times.Once);
    }

    [SetUICulture("en")]
    [Test]
    public async Task Messages_InEnglish()
    {
        _webSearch.Setup(w => w.TestAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var vm = Create();
        await vm.LoadAsync();
        await vm.TestAsync();
        Assert.That(vm.StatusMessage, Does.Contain("1 result").And.Not.Contain("results"));
        Assert.That(vm.ProviderOptions[0].Label, Is.EqualTo("No search provider"));
    }

    // ------------------------------------------------------------------ Zugangsprüfung beim Speichern (PR 2)

    private const string NewKey = "tvly-DUMMY_TEST_KEY_NEU";

    private void VerifyReturns(WebSearchVerification result) =>
        _webSearch.Setup(w => w.VerifyAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);

    private async Task<WebSearchSettingsViewModel> LoadedWithNewKey(string key = NewKey)
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.TavilyApiKey = key;
        return vm;
    }

    [SetCulture("de-DE")]
    [Test]
    public async Task Save_ChangedKey_Ok_StoresAndShowsUsage_StaysOpen()
    {
        WebSearchSettings? verified = null;
        _webSearch.Setup(w => w.VerifyAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()))
            .Callback<WebSearchSettings, CancellationToken>((s, _) => verified = s)
            .ReturnsAsync(WebSearchVerification.Ok(new WebSearchUsage(3, 1000)));
        var vm = await LoadedWithNewKey(" " + NewKey + " ");

        Assert.That(await vm.SaveAsync(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(verified!.TavilyApiKey, Is.EqualTo(NewKey), "geprüft werden die ungespeicherten, getrimmten Werte");
            Assert.That(_stored.TavilyApiKey, Is.EqualTo(NewKey));
            Assert.That(vm.IsSuccess, Is.True);
            Assert.That(vm.StatusMessage, Does.Contain("Zugang zu Tavily geprüft").And.Contain("Diesen Monat verbraucht laut Tavily: 3 von 1.000 Credits."));
            Assert.That(vm.KeepOpenAfterSave, Is.True);
            Assert.That(vm.IsVerifying, Is.False);
        });
    }

    [Test]
    public async Task Save_OkWithFallbackSearch_MentionsCredit()
    {
        VerifyReturns(WebSearchVerification.Ok(usedCredit: true));
        var vm = await LoadedWithNewKey();

        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.That(vm.StatusMessage, Does.Contain("1 Credit"));
    }

    [Test]
    public async Task Save_Rejected_DoesNotStore_OffersTavilyAccount()
    {
        VerifyReturns(WebSearchVerification.FromException(WebSearchException.FromHttpStatus("Tavily", 401, "technisch")));
        _links.Setup(l => l.OpenUrl(It.IsAny<string>())).Returns(true);
        var vm = await LoadedWithNewKey();

        Assert.That(await vm.SaveAsync(), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(_stored.TavilyApiKey, Is.EqualTo(DummyKey), "nicht gespeichert");
            Assert.That(vm.IsSuccess, Is.False);
            Assert.That(vm.StatusMessage, Does.StartWith("Nicht gespeichert:").And.Contain("Schlüssel wurde abgelehnt").And.Not.Contain("technisch"));
            Assert.That(vm.ShowTavilyAccountLink, Is.True);
            Assert.That(vm.CanSaveAnyway, Is.False);
        });
        Assert.That(vm.OpenTavilyAccount(), Is.True);
        _links.Verify(l => l.OpenUrl("https://app.tavily.com/home"), Times.Once);

        vm.TavilyApiKey = "tvly-DUMMY_TEST_KEY_ANDERS";
        Assert.That(vm.ShowTavilyAccountLink, Is.False, "neuer Schlüssel → Angebot verschwindet");
    }

    [Test]
    public async Task Save_SearxngWithoutJson_Rejected_NoTavilyLink()
    {
        VerifyReturns(WebSearchVerification.FromException(
            new WebSearchException("JSON nicht freigeschaltet.", "SearXNG", 403, kind: WebSearchErrorKind.Rejected)));
        var vm = Create();
        vm.Provider = WebSearchProviderType.Searxng;
        vm.SearxngUrl = "http://localhost:8888";

        Assert.That(await vm.SaveAsync(), Is.False);
        Assert.That(vm.ShowTavilyAccountLink, Is.False);
        Assert.That(_stored.Provider, Is.EqualTo(WebSearchProviderType.Tavily));
    }

    [Test]
    public async Task Save_Unreachable_DoesNotStore_SaveAnywayStoresWithWarning()
    {
        VerifyReturns(WebSearchVerification.FromException(WebSearchException.FromNetworkError("Tavily", new HttpRequestException("kein Netz"))));
        var vm = await LoadedWithNewKey();

        Assert.That(await vm.SaveAsync(), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(_stored.TavilyApiKey, Is.EqualTo(DummyKey));
            Assert.That(vm.CanSaveAnyway, Is.True);
            Assert.That(vm.IsWarning, Is.True);
            Assert.That(vm.StatusMessage, Does.Contain("trotzdem speichern"));
        });

        Assert.That(await vm.SaveAnywayAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(_stored.TavilyApiKey, Is.EqualTo(NewKey));
            Assert.That(vm.IsWarning, Is.True);
            Assert.That(vm.StatusMessage, Does.Contain("nicht geprüft"));
            Assert.That(vm.CanSaveAnyway, Is.False);
        });
        _webSearch.Verify(w => w.VerifyAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task SaveAnyway_NotOfferedWithoutFailedCheck()
    {
        var vm = await LoadedWithNewKey();
        Assert.That(await vm.SaveAnywayAsync(), Is.False);
        _storage.Verify(s => s.SaveWebSearchSettingsAsync(It.IsAny<WebSearchSettings>()), Times.Never);
    }

    [Test]
    public async Task Save_Unreachable_ThenEditKey_RemovesSaveAnyway()
    {
        VerifyReturns(WebSearchVerification.FromException(WebSearchException.Timeout("Tavily", 15)));
        var vm = await LoadedWithNewKey();
        await vm.SaveAsync();
        Assert.That(vm.CanSaveAnyway, Is.True);

        vm.TavilyApiKey = "tvly-DUMMY_TEST_KEY_ANDERS";
        Assert.That(vm.CanSaveAnyway, Is.False);
    }

    [Test]
    public async Task Save_LimitReached_StoresWithWarning()
    {
        VerifyReturns(WebSearchVerification.FromException(WebSearchException.FromHttpStatus("Tavily", 432, "x")));
        var vm = await LoadedWithNewKey();

        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(_stored.TavilyApiKey, Is.EqualTo(NewKey));
            Assert.That(vm.IsWarning, Is.True);
            Assert.That(vm.StatusMessage, Does.StartWith("Gespeichert – der Zugang ist gültig, aber:"));
            Assert.That(vm.KeepOpenAfterSave, Is.True);
        });
    }

    [Test]
    public async Task Save_Unchanged_OnlyMaxResults_DoesNotVerify()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.MaxResults = 9;

        Assert.That(await vm.SaveAsync(), Is.True);
        Assert.That(_stored.MaxResults, Is.EqualTo(9));
        Assert.That(vm.KeepOpenAfterSave, Is.False);
        _webSearch.Verify(w => w.VerifyAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Save_ProviderChanged_Verifies_NoneNever()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.Provider = WebSearchProviderType.None;
        Assert.That(await vm.SaveAsync(), Is.True);
        _webSearch.Verify(w => w.VerifyAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()), Times.Never);

        vm.Provider = WebSearchProviderType.Tavily;
        vm.TavilyApiKey = DummyKey;
        Assert.That(vm.NeedsVerification(vm.ToSettings()), Is.True, "Dienst gegenüber dem gespeicherten Stand geändert");
    }

    [Test]
    public async Task Save_AfterSuccessfulCheck_SameValues_NoSecondCheck()
    {
        var vm = await LoadedWithNewKey();
        await vm.SaveAsync();
        await vm.SaveAsync();
        _webSearch.Verify(w => w.VerifyAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task KeyFormat_WarnsOnBlur_WithoutNetwork_AndInRejectedMessage()
    {
        var vm = await LoadedWithNewKey("DUMMY_TEST_KEY_OHNE_VORSILBE");
        Assert.That(vm.KeyFormatWarning, Is.Null, "erst beim Verlassen des Feldes");

        vm.CheckKeyFormat();
        Assert.That(vm.KeyFormatWarning, Does.Contain("tvly-"));
        _webSearch.VerifyNoOtherCalls();

        VerifyReturns(WebSearchVerification.FromException(WebSearchException.FromHttpStatus("Tavily", 401, "x")));
        await vm.SaveAsync();
        Assert.That(vm.StatusMessage, Does.Contain("tvly-"));

        vm.TavilyApiKey = NewKey;
        Assert.That(vm.KeyFormatWarning, Is.Null, "passender Schlüssel → Hinweis verschwindet sofort");
    }

    [Test]
    public async Task KeyFormat_NoWarningForTvlyKey_OrOtherProvider()
    {
        var vm = await LoadedWithNewKey();
        vm.CheckKeyFormat();
        Assert.That(vm.KeyFormatWarning, Is.Null);

        vm.TavilyApiKey = "falsch";
        vm.CheckKeyFormat();
        vm.Provider = WebSearchProviderType.Searxng;
        Assert.That(vm.KeyFormatWarning, Is.Null);
    }

    [Test]
    public async Task Save_CancelDuringCheck_StoresNothing()
    {
        var started = new TaskCompletionSource();
        _webSearch.Setup(w => w.VerifyAsync(It.IsAny<WebSearchSettings>(), It.IsAny<CancellationToken>()))
            .Returns<WebSearchSettings, CancellationToken>(async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return WebSearchVerification.Ok();
            });
        var vm = await LoadedWithNewKey();

        var save = vm.SaveAsync();
        await started.Task;
        Assert.Multiple(() =>
        {
            Assert.That(vm.IsVerifying, Is.True);
            Assert.That(vm.CanSave, Is.False);
            Assert.That(vm.CanTest, Is.False);
            Assert.That(vm.IsProgress, Is.True);
            Assert.That(vm.StatusMessage, Does.Contain("wird geprüft"));
        });

        vm.CancelVerification();

        Assert.That(await save, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(_stored.TavilyApiKey, Is.EqualTo(DummyKey));
            Assert.That(vm.IsVerifying, Is.False);
            Assert.That(vm.StatusMessage, Is.Null);
        });
        _storage.Verify(s => s.SaveWebSearchSettingsAsync(It.IsAny<WebSearchSettings>()), Times.Never);
    }

    [Test]
    public void CancelVerification_WithoutCheck_DoesNothing() =>
        Assert.DoesNotThrow(() => Create().CancelVerification());

    [SetUICulture("en")]
    [SetCulture("en-US")]
    [Test]
    public void UsageText_English_Plural()
    {
        Assert.That(WebSearchSettingsViewModel.UsageText(new WebSearchUsage(1, null)), Does.StartWith("Used this month according to Tavily: 1 credit."));
        Assert.That(WebSearchSettingsViewModel.UsageText(new WebSearchUsage(1200, 1000)), Does.StartWith("Used this month according to Tavily: 1,200 of 1,000 credits."));
    }

    [SetCulture("de-DE")]
    [Test]
    public void UsageText_German_Plural() =>
        Assert.That(WebSearchSettingsViewModel.UsageText(new WebSearchUsage(0, 1)), Is.EqualTo("Diesen Monat verbraucht laut Tavily: 0 von 1 Credit. Neue Suchen zählt Tavily teils erst später mit."));
}
