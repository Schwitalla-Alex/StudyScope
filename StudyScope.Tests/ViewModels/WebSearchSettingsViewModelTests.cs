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
}
