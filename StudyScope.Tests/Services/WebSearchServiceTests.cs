using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;
using StudyScope.Tests.Common;

namespace StudyScope.Tests.Services;

/// <summary>
/// Suchdienste der Websuche ohne Netzwerk (simulierter <see cref="HttpMessageHandler"/>): Tavily und SearXNG
/// (Anfrage, Auswertung, Fehlerübersetzung, Zeitlimit, Abbruch), Auswahl über den Dispatcher, Fake-Suche nur über
/// die Umgebungsvariable.
/// </summary>
[TestFixture]
public class WebSearchServiceTests
{
    private const string DummyKey = "DUMMY_TEST_KEY_TAVILY_123";

    private static WebSearchSettings Tavily(int maxResults = 5) =>
        new() { Provider = WebSearchProviderType.Tavily, TavilyApiKey = DummyKey, MaxResults = maxResults };

    private static WebSearchSettings Searxng(string url = "http://localhost:8888") =>
        new() { Provider = WebSearchProviderType.Searxng, SearxngUrl = url };

    private const string TavilyResponse = """
        {
          "query": "Photosynthese",
          "answer": null,
          "images": [],
          "results": [
            { "title": "Photosynthese – Überblick", "url": "https://example.org/photosynthese", "content": "Pflanzen wandeln\nLichtenergie um.", "score": 0.91, "published_date": "2026-01-15T10:00:00Z" },
            { "title": "", "url": "https://www.example.org/ohne-titel", "content": "Ohne Titel", "score": 0.5 },
            { "title": "Doppelt", "url": "https://example.org/photosynthese", "content": "gleiche Adresse", "score": 0.4 },
            { "title": "Gefährlich", "url": "javascript:alert(1)", "content": "darf nicht durch", "score": 0.3 },
            { "title": "Datei", "url": "file:///C:/geheim.txt", "content": "darf nicht durch", "score": 0.2 }
          ],
          "response_time": 0.8,
          "usage": { "credits": 1 },
          "request_id": "test"
        }
        """;

    // ---------------- Tavily ----------------

    [Test]
    public async Task Tavily_SendsOnlyQueryAndFixedOptions_WithBearerKey()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.OK, TavilyResponse);
        var service = new TavilySearchService(new HttpClient(handler));

        await service.SearchAsync(Tavily(maxResults: 3), WebSearchQuery.FromUserQuestion("Was ist Photosynthese?"), TestCultures.De, 3, CancellationToken.None);

        Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.LastRequestUri!.AbsoluteUri, Is.EqualTo(TavilySearchService.Endpoint));
        Assert.That(handler.LastHeaders!.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(handler.LastHeaders.Authorization.Parameter, Is.EqualTo(DummyKey));
        Assert.That(handler.LastRequestUri.Query, Is.Empty, "Schlüssel nie in der Adresse");

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var names = body.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.That(names, Is.EquivalentTo(new[] { "query", "search_depth", "max_results", "include_answer", "include_raw_content", "include_images", "include_published_date" }));
        Assert.Multiple(() =>
        {
            Assert.That(body.RootElement.GetProperty("query").GetString(), Is.EqualTo("Was ist Photosynthese?"));
            Assert.That(body.RootElement.GetProperty("search_depth").GetString(), Is.EqualTo("basic"), "1 Credit je Suche");
            Assert.That(body.RootElement.GetProperty("max_results").GetInt32(), Is.EqualTo(3));
            Assert.That(body.RootElement.GetProperty("include_answer").GetBoolean(), Is.False);
            Assert.That(body.RootElement.GetProperty("include_raw_content").GetBoolean(), Is.False);
            Assert.That(handler.LastRequestBody, Does.Not.Contain("max_age_hours"));
        });
    }

    [Test]
    public async Task Tavily_ParsesResults_DropsUnsafeAndDuplicateUrls()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.OK, TavilyResponse);
        var service = new TavilySearchService(new HttpClient(handler));

        var results = await service.SearchAsync(Tavily(), WebSearchQuery.FromUserQuestion("Photosynthese"), TestCultures.De, 5, CancellationToken.None);

        Assert.That(results.Select(r => r.Url), Is.EqualTo(new[] { "https://example.org/photosynthese", "https://www.example.org/ohne-titel" }));
        Assert.Multiple(() =>
        {
            Assert.That(results[0].Title, Is.EqualTo("Photosynthese – Überblick"));
            Assert.That(results[0].Snippet, Is.EqualTo("Pflanzen wandeln Lichtenergie um."), "Zeilenumbruch → Leerzeichen");
            Assert.That(results[0].PublishedAt, Is.EqualTo(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero)));
            Assert.That(results[0].Domain, Is.EqualTo("example.org"));
            Assert.That(results[1].Title, Is.EqualTo("example.org"), "fehlender Titel → Domain");
            Assert.That(results[1].PublishedAt, Is.Null);
        });
    }

    [Test]
    public async Task Tavily_RespectsMaxResults()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.OK, TavilyResponse);
        var results = await new TavilySearchService(new HttpClient(handler))
            .SearchAsync(Tavily(1), WebSearchQuery.FromUserQuestion("Photosynthese"), TestCultures.De, 1, CancellationToken.None);
        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void Tavily_WithoutKey_FailsWithoutRequest()
    {
        var handler = new FakeHandler();
        var service = new TavilySearchService(new HttpClient(handler));
        var settings = Tavily();
        settings.TavilyApiKey = "  ";

        var ex = Assert.ThrowsAsync<WebSearchException>(() =>
            service.SearchAsync(settings, WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De, 5, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("API-Schlüssel"));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [TestCase(401, "Schlüssel wurde abgelehnt")]
    [TestCase(403, "Schlüssel wurde abgelehnt")]
    [TestCase(429, "Zu viele Anfragen")]
    [TestCase(432, "Kontingent")]
    [TestCase(433, "Ausgabengrenze")]
    [TestCase(500, "gestört oder überlastet")]
    [TestCase(400, "abgelehnt")]
    public void Tavily_HttpErrors_BecomeUnderstandableMessages(int status, string expected)
    {
        var handler = new FakeHandler();
        handler.Respond((HttpStatusCode)status, """{"detail":{"error":"technischer Text"}}""");
        var service = new TavilySearchService(new HttpClient(handler));

        var ex = Assert.ThrowsAsync<WebSearchException>(() =>
            service.SearchAsync(Tavily(), WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De, 5, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain(expected));
            Assert.That(ex.Message, Does.Contain("Tavily"));
            Assert.That(ex.Message, Does.Contain($"HTTP {status}"));
            Assert.That(ex.StatusCode, Is.EqualTo(status));
            Assert.That(ex.Detail, Does.Contain("technischer Text"));
            Assert.That(ex.Message, Does.Not.Contain("technischer Text"), "technische Antwort nur in Detail");
            Assert.That(ex.Message, Does.Not.Contain(DummyKey));
        });
    }

    [SetUICulture("en")]
    [Test]
    public void Tavily_QuotaMessage_InEnglish()
    {
        var handler = new FakeHandler();
        handler.Respond((HttpStatusCode)432, "{}");
        var ex = Assert.ThrowsAsync<WebSearchException>(() => new TavilySearchService(new HttpClient(handler))
            .SearchAsync(Tavily(), WebSearchQuery.FromUserQuestion("Question"), TestCultures.En, 5, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("quota"));
    }

    [TestCase("not json")]
    [TestCase("""{"answer":"x"}""")]
    [TestCase("""{"results":{}}""")]
    public void Tavily_UnexpectedResponse(string body)
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.OK, body);
        var ex = Assert.ThrowsAsync<WebSearchException>(() => new TavilySearchService(new HttpClient(handler))
            .SearchAsync(Tavily(), WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De, 5, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("unerwartete Antwort"));
    }

    [Test]
    public void Tavily_NetworkError_BecomesNotReachable()
    {
        var handler = new FakeHandler();
        handler.Throw(new HttpRequestException("Name nicht auflösbar"));
        var ex = Assert.ThrowsAsync<WebSearchException>(() => new TavilySearchService(new HttpClient(handler))
            .SearchAsync(Tavily(), WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De, 5, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("nicht erreichbar"));
    }

    [Test]
    public void Timeout_BecomesWebSearchException()
    {
        var handler = new FakeHandler { Delay = TimeSpan.FromSeconds(5) };
        handler.Respond(HttpStatusCode.OK, TavilyResponse);

        var ex = Assert.ThrowsAsync<WebSearchException>(() => WebSearchHttp.SendAsync(new HttpClient(handler), "Tavily",
            () => new HttpRequestMessage(HttpMethod.Get, "https://example.org/"), CancellationToken.None, TimeSpan.FromMilliseconds(50)));
        Assert.That(ex!.Message, Does.Contain("nicht innerhalb von"));
    }

    [Test]
    public void UserCancellation_StaysOperationCanceled()
    {
        var handler = new FakeHandler { Delay = TimeSpan.FromSeconds(5) };
        handler.Respond(HttpStatusCode.OK, TavilyResponse);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.CatchAsync<OperationCanceledException>(() => new TavilySearchService(new HttpClient(handler))
            .SearchAsync(Tavily(), WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De, 5, cts.Token));
    }

    // ---------------- SearXNG ----------------

    private const string SearxngResponse = """
        {
          "query": "Photosynthese",
          "number_of_results": 2,
          "results": [
            { "url": "https://example.org/a", "title": "Treffer A", "content": "Text A", "engine": "test", "publishedDate": "2025-12-01T00:00:00" },
            { "url": "https://example.org/b", "title": "Treffer B", "content": "Text B", "engine": "test", "publishedDate": null }
          ],
          "answers": [], "suggestions": []
        }
        """;

    [TestCase("http://localhost:8888", "http://localhost:8888/search")]
    [TestCase("http://localhost:8888/", "http://localhost:8888/search")]
    [TestCase("https://suche.example.org/searx/search", "https://suche.example.org/searx/search")]
    [TestCase("https://suche.example.org/searx/?x=1#y", "https://suche.example.org/searx/search")]
    public void Searxng_BuildsSearchUri(string baseUrl, string expectedPath)
    {
        Assert.That(SearxngSearchService.TryBuildSearchUri(baseUrl, WebSearchQuery.FromUserQuestion("Zelle & Kern?"), TestCultures.En, out var uri), Is.True);
        Assert.That(uri.GetLeftPart(UriPartial.Path), Is.EqualTo(expectedPath));
        Assert.That(uri.Query, Is.EqualTo("?format=json&q=Zelle%20%26%20Kern%3F&language=en"));
    }

    [TestCase("")]
    [TestCase("localhost:8888")]
    [TestCase("ftp://example.org")]
    [TestCase("http://benutzer:geheim@example.org")]
    public void Searxng_RejectsInvalidUrl_WithoutRequest(string url)
    {
        var handler = new FakeHandler();
        var ex = Assert.ThrowsAsync<WebSearchException>(() => new SearxngSearchService(new HttpClient(handler))
            .SearchAsync(Searxng(url), WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De, 5, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("Adresse"));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task Searxng_ParsesResults_WithLanguage()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.OK, SearxngResponse);

        var results = await new SearxngSearchService(new HttpClient(handler))
            .SearchAsync(Searxng(), WebSearchQuery.FromUserQuestion("Photosynthese"), TestCultures.De, 5, CancellationToken.None);

        Assert.That(handler.LastMethod, Is.EqualTo(HttpMethod.Get));
        Assert.That(handler.LastRequestUri!.Query, Does.Contain("language=de"));
        Assert.That(results.Select(r => r.Title), Is.EqualTo(new[] { "Treffer A", "Treffer B" }));
        Assert.That(results[0].PublishedAt?.Year, Is.EqualTo(2025));
        Assert.That(results[1].PublishedAt, Is.Null);
    }

    [Test]
    public void Searxng_403_ExplainsJsonFormat()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.Forbidden, "<html>Forbidden</html>");
        var ex = Assert.ThrowsAsync<WebSearchException>(() => new SearxngSearchService(new HttpClient(handler))
            .SearchAsync(Searxng(), WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De, 5, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("JSON-Format"));
        Assert.That(ex.Message, Does.Contain("settings.yml"));
    }

    [Test]
    public void Searxng_HtmlInsteadOfJson_ExplainsAddress()
    {
        var handler = new FakeHandler();
        handler.Respond(HttpStatusCode.OK, "<!DOCTYPE html><html></html>");
        var ex = Assert.ThrowsAsync<WebSearchException>(() => new SearxngSearchService(new HttpClient(handler))
            .SearchAsync(Searxng(), WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De, 5, CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("kein JSON"));
    }

    // ---------------- Dispatcher ----------------

    private static (WebSearchDispatcher Dispatcher, FakeHandler Handler) CreateDispatcher(WebSearchSettings stored, string? fakeEnv = null)
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.LoadWebSearchSettingsAsync()).ReturnsAsync(() => stored.Clone());
        var handler = new FakeHandler();
        var dispatcher = new WebSearchDispatcher(storage.Object, new HttpClient(handler),
            name => name == WebSearchDispatcher.FakeEnvironmentVariable ? fakeEnv : null);
        return (dispatcher, handler);
    }

    [Test]
    public async Task Dispatcher_NotConfigured_ThrowsWithoutRequest()
    {
        var (dispatcher, handler) = CreateDispatcher(new WebSearchSettings());

        Assert.That(await dispatcher.IsConfiguredAsync(), Is.False);
        var ex = Assert.ThrowsAsync<WebSearchException>(() => dispatcher.SearchAsync(WebSearchQuery.FromUserQuestion("Frage"), TestCultures.De));
        Assert.That(ex!.Message, Does.Contain("kein Suchdienst eingerichtet"));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task Dispatcher_UsesTavily_WithStoredMaxResults()
    {
        var (dispatcher, handler) = CreateDispatcher(Tavily(maxResults: 2));
        handler.Respond(HttpStatusCode.OK, TavilyResponse);

        Assert.That(await dispatcher.IsConfiguredAsync(), Is.True);
        var results = await dispatcher.SearchAsync(WebSearchQuery.FromUserQuestion("Photosynthese"), TestCultures.De);

        Assert.That(handler.LastRequestUri!.AbsoluteUri, Is.EqualTo(TavilySearchService.Endpoint));
        Assert.That(handler.LastRequestBody, Does.Contain("\"max_results\":2"));
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Dispatcher_UsesSearxng()
    {
        var (dispatcher, handler) = CreateDispatcher(Searxng("http://localhost:8888"));
        handler.Respond(HttpStatusCode.OK, SearxngResponse);

        await dispatcher.SearchAsync(WebSearchQuery.FromUserQuestion("Photosynthese"), TestCultures.En);

        Assert.That(handler.LastRequestUri!.Host, Is.EqualTo("localhost"));
        Assert.That(handler.LastRequestUri.Query, Does.Contain("language=en"));
    }

    [Test]
    public async Task Dispatcher_Test_UsesUnsavedSettings_AndFixedTerm()
    {
        var (dispatcher, handler) = CreateDispatcher(new WebSearchSettings());
        handler.Respond(HttpStatusCode.OK, TavilyResponse);

        var count = await dispatcher.TestAsync(Tavily());

        Assert.That(count, Is.EqualTo(2));
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.That(body.RootElement.GetProperty("query").GetString(), Is.EqualTo(WebSearchQuery.ConnectionTestText));
    }

    [Test]
    public void Dispatcher_Test_WithoutKey_IsNotConfigured()
    {
        var (dispatcher, handler) = CreateDispatcher(new WebSearchSettings());
        Assert.ThrowsAsync<WebSearchException>(() => dispatcher.TestAsync(new WebSearchSettings { Provider = WebSearchProviderType.Tavily }));
        Assert.That(handler.RequestCount, Is.Zero);
    }

    [Test]
    public async Task Dispatcher_FakeOnlyThroughEnvironment_NoNetwork()
    {
        var (dispatcher, handler) = CreateDispatcher(new WebSearchSettings(), fakeEnv: "1");

        Assert.That(dispatcher.IsFakeMode, Is.True);
        Assert.That(await dispatcher.IsConfiguredAsync(), Is.True);
        var results = await dispatcher.SearchAsync(WebSearchQuery.FromUserQuestion("Zellatmung"), TestCultures.De);

        Assert.That(handler.RequestCount, Is.Zero);
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results.All(r => r.Url.StartsWith("https://example.org/", StringComparison.Ordinal)), Is.True);
        Assert.That(results[0].Title, Does.Contain("Zellatmung"));
    }

    [TestCase(null)]
    [TestCase("0")]
    [TestCase("true")]
    public void Dispatcher_FakeNotActiveOtherwise(string? value)
    {
        var (dispatcher, _) = CreateDispatcher(new WebSearchSettings(), fakeEnv: value);
        Assert.That(dispatcher.IsFakeMode, Is.False);
    }

    [Test]
    public void Fake_AnswersInResultLanguage()
    {
        var german = FakeWebSearchService.Create(WebSearchQuery.FromUserQuestion("Zelle"), TestCultures.De, 5);
        var english = FakeWebSearchService.Create(WebSearchQuery.FromUserQuestion("Cell"), TestCultures.En, 5);

        Assert.That(german[0].Title, Does.StartWith("Überblick"));
        Assert.That(german[0].Snippet, Does.Contain("Testtreffer"));
        Assert.That(english[0].Title, Does.StartWith("Overview"));
        Assert.That(english[0].Snippet, Does.Contain("Test result"));
        Assert.That(FakeWebSearchService.Create(WebSearchQuery.FromUserQuestion("Zelle"), TestCultures.De, 2), Has.Count.EqualTo(2));
    }

    // ---------------- Hilfen ----------------

    [TestCase("2026-01-15", 2026)]
    [TestCase("Wed, 15 Oct 2025 12:00:00 GMT", 2025)]
    [TestCase("kein Datum", null)]
    [TestCase("", null)]
    public void ParseDate_IsTolerant(string value, int? year) =>
        Assert.That(WebSearchHttp.ParseDate(value)?.Year, Is.EqualTo(year));

    [Test]
    public void Clean_ShortensLongSnippets()
    {
        var longText = string.Join(' ', Enumerable.Repeat("Wort", 1000));
        var cleaned = WebSearchHttp.Clean(new[] { new WebSearchResult("T", "https://example.org/", longText) }, 5);
        Assert.That(cleaned[0].Snippet.Length, Is.LessThanOrEqualTo(WebSearchHttp.MaxSnippetLength + 2));
        Assert.That(cleaned[0].Snippet, Does.EndWith(" …"));
    }

    /// <summary>Simulierter HTTP-Dienst: feste Antwort, Ausnahme oder Verzögerung; merkt sich die letzte Anfrage.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "{}";
        private Exception? _exception;

        public TimeSpan Delay { get; set; } = TimeSpan.Zero;
        public string? LastRequestBody { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public int RequestCount { get; private set; }
        public System.Net.Http.Headers.HttpRequestHeaders? LastHeaders { get; private set; }

        public void Respond(HttpStatusCode status, string body) { _status = status; _body = body; _exception = null; }
        public void Throw(Exception exception) => _exception = exception;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            LastHeaders = request.Headers;
            LastRequestBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            if (_exception != null)
                throw _exception;
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
