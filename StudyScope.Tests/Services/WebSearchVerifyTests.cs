using System.Net;
using System.Net.Http;
using System.Text;
using Moq;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Zugangsprüfung beim Speichern der Websuche (PR 2) ohne Netzwerk: Tavily über GET /usage (Rückfall Prüfsuche),
/// SearXNG über eine Suche in der eigenen Instanz, Einordnung der Fehler über den Dispatcher.
/// </summary>
[TestFixture]
public class WebSearchVerifyTests
{
    private const string DummyKey = "DUMMY_TEST_KEY_TAVILY_VERIFY";

    private const string UsageResponse = """
        {
          "key": { "usage": 12, "limit": null, "search_usage": 12 },
          "account": { "current_plan": "Researcher", "plan_usage": 3, "plan_limit": 1000, "paygo_usage": 0, "paygo_limit": null }
        }
        """;

    private const string SearchResponse = """{ "results": [ { "title": "T", "url": "https://example.org/", "content": "x" } ] }""";

    private static WebSearchSettings Tavily() => new() { Provider = WebSearchProviderType.Tavily, TavilyApiKey = DummyKey };
    private static WebSearchSettings Searxng() => new() { Provider = WebSearchProviderType.Searxng, SearxngUrl = "http://localhost:8888" };

    private static (WebSearchDispatcher Dispatcher, RoutingHandler Handler) Create(string? fakeEnv = null)
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.LoadWebSearchSettingsAsync()).ReturnsAsync(new WebSearchSettings());
        var handler = new RoutingHandler();
        var dispatcher = new WebSearchDispatcher(storage.Object, new HttpClient(handler),
            name => name == WebSearchDispatcher.FakeEnvironmentVariable ? fakeEnv : null);
        return (dispatcher, handler);
    }

    // ---------------- Tavily ----------------

    [Test]
    public async Task Tavily_Ok_UsesUsageEndpoint_KeyOnlyInHeader_NoSearch()
    {
        var (dispatcher, handler) = Create();
        handler.Get = _ => (HttpStatusCode.OK, UsageResponse);

        var result = await dispatcher.VerifyAsync(Tavily());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Ok));
            Assert.That(result.Usage, Is.EqualTo(new WebSearchUsage(3, 1000)), "Tarif vor Schlüssel");
            Assert.That(result.UsedCredit, Is.False);
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(handler.Requests[0].Uri.AbsoluteUri, Is.EqualTo(TavilySearchService.UsageEndpoint));
            Assert.That(handler.Requests[0].Uri.AbsoluteUri, Does.Not.Contain(DummyKey));
            Assert.That(handler.Requests[0].Authorization, Is.EqualTo("Bearer " + DummyKey));
        });
    }

    [Test]
    public void ParseUsage_KeyFallback_AndUnreadable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TavilySearchService.ParseUsage("""{ "key": { "usage": 7, "limit": null } }"""), Is.EqualTo(new WebSearchUsage(7, null)));
            Assert.That(TavilySearchService.ParseUsage("""{ "key": { "usage": 7, "limit": 50 }, "account": { "plan_usage": 2 } }"""),
                Is.EqualTo(new WebSearchUsage(7, 50)), "Tarif ohne Grenze → Schlüssel");
            Assert.That(TavilySearchService.ParseUsage("<html>"), Is.Null);
            Assert.That(TavilySearchService.ParseUsage("{}"), Is.Null);
            Assert.That(TavilySearchService.ParseUsage(""), Is.Null);
        });
    }

    [Test]
    public async Task Tavily_OkWithUnreadableUsage_StillOk()
    {
        var (dispatcher, handler) = Create();
        handler.Get = _ => (HttpStatusCode.OK, "{}");

        var result = await dispatcher.VerifyAsync(Tavily());

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Ok));
        Assert.That(result.Usage, Is.Null);
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.MethodNotAllowed)]
    public async Task Tavily_UsageMissing_FallsBackToOneResultSearch(HttpStatusCode status)
    {
        var (dispatcher, handler) = Create();
        handler.Get = _ => (status, "nicht da");
        handler.Post = _ => (HttpStatusCode.OK, SearchResponse);

        var result = await dispatcher.VerifyAsync(Tavily());

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Ok));
            Assert.That(result.UsedCredit, Is.True);
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[1].Uri.AbsoluteUri, Is.EqualTo(TavilySearchService.Endpoint));
            Assert.That(handler.Requests[1].Body, Does.Contain("\"query\":\"StudyScope\"").And.Contain("\"max_results\":1"));
        });
    }

    [TestCase(401, WebSearchVerificationStatus.Rejected)]
    [TestCase(403, WebSearchVerificationStatus.Rejected)]
    [TestCase(429, WebSearchVerificationStatus.LimitReached)]
    [TestCase(432, WebSearchVerificationStatus.LimitReached)]
    [TestCase(433, WebSearchVerificationStatus.LimitReached)]
    [TestCase(500, WebSearchVerificationStatus.Unreachable)]
    [TestCase(503, WebSearchVerificationStatus.Unreachable)]
    [TestCase(400, WebSearchVerificationStatus.Unreachable)]
    public async Task Tavily_HttpStatus_IsClassified(int status, WebSearchVerificationStatus expected)
    {
        var (dispatcher, handler) = Create();
        handler.Get = _ => ((HttpStatusCode)status, """{ "detail": { "error": "technisch" } }""");

        var result = await dispatcher.VerifyAsync(Tavily());

        Assert.That(result.Status, Is.EqualTo(expected));
        Assert.That(result.Message, Is.Not.Empty.And.Not.Contain("technisch"));
        Assert.That(handler.Requests, Has.Count.EqualTo(1), "keine Prüfsuche bei Fehlern");
    }

    [Test]
    public async Task Tavily_NetworkError_Unreachable()
    {
        var (dispatcher, handler) = Create();
        handler.Exception = new HttpRequestException("Host nicht gefunden");

        var result = await dispatcher.VerifyAsync(Tavily());

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Unreachable));
        Assert.That(result.ErrorKind, Is.EqualTo(WebSearchErrorKind.Unreachable));
    }

    [Test]
    public void Tavily_CancelledByUser_StaysCancellation()
    {
        var (dispatcher, handler) = Create();
        handler.Get = _ => (HttpStatusCode.OK, UsageResponse);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(async () => await dispatcher.VerifyAsync(Tavily(), cts.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Tavily_WithoutKey_RejectedAsNotConfigured_NoRequest()
    {
        var (dispatcher, handler) = Create();

        var result = await dispatcher.VerifyAsync(new WebSearchSettings { Provider = WebSearchProviderType.Tavily });

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Rejected));
        Assert.That(result.ErrorKind, Is.EqualTo(WebSearchErrorKind.NotConfigured));
        Assert.That(handler.Requests, Is.Empty);
    }

    // ---------------- SearXNG ----------------

    [Test]
    public async Task Searxng_Json_Ok_SearchesFixedTerm()
    {
        var (dispatcher, handler) = Create();
        handler.Get = _ => (HttpStatusCode.OK, SearchResponse);

        var result = await dispatcher.VerifyAsync(Searxng());

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Ok));
        Assert.That(result.Usage, Is.Null);
        Assert.That(handler.Requests[0].Uri.Query, Does.Contain("format=json").And.Contain("q=StudyScope"));
    }

    [Test]
    public async Task Searxng_HtmlInsteadOfJson_Rejected()
    {
        var (dispatcher, handler) = Create();
        handler.Get = _ => (HttpStatusCode.OK, "<!DOCTYPE html><html></html>");

        var result = await dispatcher.VerifyAsync(Searxng());

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Rejected));
    }

    [Test]
    public async Task Searxng_JsonDisabled403_Rejected()
    {
        var (dispatcher, handler) = Create();
        handler.Get = _ => (HttpStatusCode.Forbidden, "Forbidden");

        var result = await dispatcher.VerifyAsync(Searxng());

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Rejected));
        Assert.That(result.Message, Does.Contain("JSON"));
    }

    [Test]
    public async Task Searxng_NetworkError_Unreachable()
    {
        var (dispatcher, handler) = Create();
        handler.Exception = new HttpRequestException("Verbindung abgelehnt");

        var result = await dispatcher.VerifyAsync(Searxng());

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Unreachable));
    }

    // ---------------- Dispatcher ----------------

    [Test]
    public async Task NoProvider_OkWithoutRequest()
    {
        var (dispatcher, handler) = Create();

        var result = await dispatcher.VerifyAsync(new WebSearchSettings());

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Ok));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task FakeMode_StillChecksTheEnteredService()
    {
        var (dispatcher, handler) = Create(fakeEnv: "1");
        handler.Get = _ => (HttpStatusCode.Unauthorized, "{}");

        var result = await dispatcher.VerifyAsync(Tavily());

        Assert.That(result.Status, Is.EqualTo(WebSearchVerificationStatus.Rejected));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void ErrorKinds_OfFactories()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebSearchException.FromHttpStatus("Tavily", 401, null).SettingsCanHelp, Is.True);
            Assert.That(WebSearchException.FromHttpStatus("SearXNG", 404, null).Kind, Is.EqualTo(WebSearchErrorKind.Rejected));
            Assert.That(WebSearchException.FromHttpStatus("Tavily", 432, null).SettingsCanHelp, Is.False);
            Assert.That(WebSearchException.FromHttpStatus("Tavily", 504, null).Kind, Is.EqualTo(WebSearchErrorKind.Unreachable));
            Assert.That(WebSearchException.Timeout("Tavily", 15).Kind, Is.EqualTo(WebSearchErrorKind.Unreachable));
            Assert.That(WebSearchException.NotConfigured().SettingsCanHelp, Is.True);
            Assert.That(WebSearchException.UnexpectedResponse("Tavily", 200, null).Kind, Is.EqualTo(WebSearchErrorKind.Unexpected));
        });
    }

    private sealed record Recorded(HttpMethod Method, Uri Uri, string? Authorization, string? Body);

    /// <summary>Simulierter HTTP-Dienst: Antwort je Methode, optional eine Ausnahme; merkt sich alle Anfragen.</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, (HttpStatusCode, string)> Get { get; set; } = _ => (HttpStatusCode.OK, "{}");
        public Func<HttpRequestMessage, (HttpStatusCode, string)> Post { get; set; } = _ => (HttpStatusCode.OK, "{}");
        public Exception? Exception { get; set; }
        public List<Recorded> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            if (Exception != null) throw Exception;
            var (status, text) = request.Method == HttpMethod.Post ? Post(request) : Get(request);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }
}
