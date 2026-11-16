using System.Net;
using System.Text;
using System.Text.Json;
using Moq;
using NUnit.Framework;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Dispatcher gegen einen simulierten HTTP-Dienst: Fehlercodes, JSON-Modus, Systemanweisung, Zeitüberschreitung und Abbruch.
/// </summary>
[TestFixture]
public class LlmServiceDispatcherHttpTests
{
    private const string FlashcardPrompt = "Erstelle genau 3 Lernkarteikarten. Antworte AUSSCHLIESSLICH als gültiges JSON-Array von Objekten.";

    private Mock<IStorageService> _storageMock = null!;
    private FakeHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _storageMock = new Mock<IStorageService>();
        _storageMock.Setup(s => s.GetLastActiveProfileNameAsync()).ReturnsAsync("Testprofil");
        UseProvider(AiProviderType.MistralAi);
        _handler = new FakeHandler();
    }

    [TearDown]
    public void TearDown() => _handler.Dispose();

    private void UseProvider(AiProviderType provider) =>
        _storageMock.Setup(s => s.LoadAiConfigForProfileAsync("Testprofil")).ReturnsAsync(new AiConfig
        {
            SelectedProvider = provider,
            ApiKey = "DUMMY_TEST_KEY_dispatcher",
            SelectedModelName = "testmodell",
            LocalEndpointUrl = "http://localhost:11434"
        });

    private LlmServiceDispatcher CreateDispatcher(TimeSpan? timeout = null) =>
        new(_storageMock.Object, new HttpClient(_handler) { Timeout = timeout ?? TimeSpan.FromSeconds(30) });

    private static string ChatCompletion(string content) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content }, finish_reason = "stop" } } });

    [TestCase(401, "API-Schlüssel")]
    [TestCase(429, "Zu viele Anfragen")]
    [TestCase(503, "gestört")]
    [TestCase(404, "nicht gefunden")]
    public void AskQuestionAsync_HttpFehler_WirftVerstaendlicheAusnahme(int status, string expectedHint)
    {
        _handler.Respond((HttpStatusCode)status, "{\"object\":\"error\",\"message\":\"Service unavailable.\"}");

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => CreateDispatcher().AskQuestionAsync(FlashcardPrompt, new[] { "Text" }));

        Assert.That(ex!.StatusCode, Is.EqualTo(status));
        Assert.That(ex.ProviderName, Is.EqualTo("Mistral AI"));
        Assert.That(ex.Message, Does.Contain($"HTTP {status}").And.Contain(expectedHint));
        Assert.That(ex.Detail, Does.Contain("Service unavailable"));
    }

    [Test]
    public void AskQuestionAsync_Netzwerkfehler_WirftAusnahmeStattText()
    {
        _handler.Throw(new HttpRequestException("Host nicht gefunden"));

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => CreateDispatcher().AskQuestionAsync("Frage", new[] { "Text" }));

        Assert.That(ex!.Message, Does.Contain("nicht erreichbar"));
    }

    [Test]
    public void AskQuestionAsync_Zeitueberschreitung_IstKeinBenutzerabbruch()
    {
        _handler.Delay = TimeSpan.FromSeconds(5);

        var ex = Assert.ThrowsAsync<LlmServiceException>(() =>
            CreateDispatcher(TimeSpan.FromMilliseconds(100)).AskQuestionAsync("Frage", new[] { "Text" }));

        Assert.That(ex!.Message, Does.Contain("Zeitüberschreitung"));
    }

    [Test]
    public async Task AskQuestionAsync_AbbruchDurchBenutzer_LiefertAbbruchText()
    {
        _handler.Delay = TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var answer = await CreateDispatcher().AskQuestionAsync("Frage", new[] { "Text" }, cts.Token);

        Assert.That(answer, Is.EqualTo(LlmServiceDispatcher.CancelledResponse));
    }

    [Test]
    public async Task AskQuestionAsync_StrukturierterAuftrag_NutztJsonModusUndPacktArrayAus()
    {
        _handler.Respond(HttpStatusCode.OK, ChatCompletion("{\"items\": [{\"question\": \"F\", \"answer\": \"A\"}]}"));

        var answer = await CreateDispatcher().AskQuestionAsync(FlashcardPrompt, new[] { "Text" });

        Assert.That(answer, Is.EqualTo("[{\"question\": \"F\", \"answer\": \"A\"}]"));
        using var body = JsonDocument.Parse(_handler.LastRequestBody!);
        Assert.That(body.RootElement.GetProperty("response_format").GetProperty("type").GetString(), Is.EqualTo("json_object"));
        Assert.That(SystemMessage(body), Is.EqualTo(LlmServiceDispatcher.JsonModeSystemPrompt));
    }

    [Test]
    public async Task AskQuestionAsync_Chatfrage_OhneJsonModusMitQuellenbindung()
    {
        _handler.Respond(HttpStatusCode.OK, ChatCompletion("Dazu enthalten Ihre Unterlagen keine Angaben."));

        var answer = await CreateDispatcher().AskQuestionAsync("Wie deinstalliere ich das Programm?", new[] { "Text" });

        Assert.That(answer, Does.Contain("keine Angaben"));
        using var body = JsonDocument.Parse(_handler.LastRequestBody!);
        Assert.That(body.RootElement.TryGetProperty("response_format", out _), Is.False);
        Assert.That(SystemMessage(body), Is.EqualTo(LlmServiceDispatcher.RagSystemPrompt));
        Assert.That(LlmServiceDispatcher.RagSystemPrompt, Does.Contain("ausschließlich").And.Contain("erfinde nichts"));
    }

    [Test]
    public async Task AskQuestionAsync_LmStudio_OhneJsonModus()
    {
        UseProvider(AiProviderType.LmStudioLocal);
        _handler.Respond(HttpStatusCode.OK, ChatCompletion("[]"));

        await CreateDispatcher().AskQuestionAsync(FlashcardPrompt, new[] { "Text" });

        using var body = JsonDocument.Parse(_handler.LastRequestBody!);
        Assert.That(body.RootElement.TryGetProperty("response_format", out _), Is.False, "lokale Server unterstützen den JSON-Modus nicht zuverlässig");
    }

    [Test]
    public void AskQuestionAsync_Ollama_HttpFehler_WirftAusnahme()
    {
        UseProvider(AiProviderType.OllamaLocal);
        _handler.Respond(HttpStatusCode.NotFound, "{\"error\":\"model not found\"}");

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => CreateDispatcher().AskQuestionAsync("Frage", new[] { "Text" }));

        Assert.That(ex!.ProviderName, Is.EqualTo("Ollama"));
        Assert.That(ex.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task AskQuestionAsync_Gemini_SchluesselImHeaderNichtInDerUrl()
    {
        UseProvider(AiProviderType.GoogleGemini);
        _handler.Respond(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "Antwort" } } } } }
        }));

        var answer = await CreateDispatcher().AskQuestionAsync("Frage", new[] { "Text" });

        Assert.That(answer, Is.EqualTo("Antwort"));
        Assert.That(_handler.LastRequestUri!.Query, Does.Not.Contain("DUMMY_TEST_KEY"));
        Assert.That(_handler.LastHeaders!.GetValues("x-goog-api-key").Single(), Is.EqualTo("DUMMY_TEST_KEY_dispatcher"));
    }

    [TestCase("{\"items\": [1, 2]}", "[1, 2]")]
    [TestCase("{\"cards\": [{\"q\": 1}]}", "[{\"q\": 1}]")]
    [TestCase("{\"title\": \"Wurzel\", \"children\": []}", "[{\"title\": \"Wurzel\", \"children\": []}]")]
    [TestCase("[1, 2]", "[1, 2]")]
    [TestCase("kein JSON", "kein JSON")]
    public void UnwrapJsonItems_LiefertDasArray(string content, string expected)
    {
        Assert.That(LlmServiceDispatcher.UnwrapJsonItems(content), Is.EqualTo(expected));
    }

    [TestCase("Antworte AUSSCHLIESSLICH als gültiges JSON-Array.", true)]
    [TestCase("Wie speichere ich ein JSON-Objekt?", false)]
    public void IsStructuredJsonPrompt_ErkenntNurAuftraegeDerDienste(string prompt, bool expected)
    {
        Assert.That(LlmServiceDispatcher.IsStructuredJsonPrompt(prompt), Is.EqualTo(expected));
    }

    // ---------------- Verbindungsprüfung („Speichern & Verbinden“) ----------------

    private static AiConfig Config(AiProviderType provider, string apiKey = "DUMMY_TEST_KEY_verbindung", string endpoint = "") => new()
    {
        SelectedProvider = provider,
        ApiKey = apiKey,
        LocalEndpointUrl = endpoint,
        SelectedModelName = "testmodell"
    };

    [TestCase(AiProviderType.MistralAi, "https://api.mistral.ai/v1/models")]
    [TestCase(AiProviderType.OpenAiGpt4, "https://api.openai.com/v1/models")]
    public async Task ValidateConnectionAsync_Cloud_RuftModelllisteMitSchluesselAb(AiProviderType provider, string expectedUrl)
    {
        _handler.Respond(HttpStatusCode.OK, "{\"object\":\"list\",\"data\":[{\"id\":\"testmodell\"}]}");

        var ok = await CreateDispatcher().ValidateConnectionAsync(Config(provider));

        Assert.That(ok, Is.True);
        Assert.That(_handler.LastMethod, Is.EqualTo(HttpMethod.Get), "Modellliste per GET – kein kostenpflichtiger Aufruf");
        Assert.That(_handler.LastRequestUri!.ToString(), Is.EqualTo(expectedUrl));
        Assert.That(_handler.LastHeaders!.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(_handler.LastHeaders.Authorization.Parameter, Is.EqualTo("DUMMY_TEST_KEY_verbindung"));
    }

    [Test]
    public async Task ValidateConnectionAsync_Gemini_SchluesselImHeaderNichtInDerUrl()
    {
        _handler.Respond(HttpStatusCode.OK, "{\"models\":[]}");

        var ok = await CreateDispatcher().ValidateConnectionAsync(Config(AiProviderType.GoogleGemini));

        Assert.That(ok, Is.True);
        Assert.That(_handler.LastMethod, Is.EqualTo(HttpMethod.Get));
        Assert.That(_handler.LastRequestUri!.ToString(), Is.EqualTo("https://generativelanguage.googleapis.com/v1beta/models"));
        Assert.That(_handler.LastHeaders!.GetValues("x-goog-api-key").Single(), Is.EqualTo("DUMMY_TEST_KEY_verbindung"));
    }

    [TestCase(AiProviderType.MistralAi, 401, "Mistral AI", "API-Schlüssel wurde abgelehnt")]
    [TestCase(AiProviderType.OpenAiGpt4, 403, "OpenAI", "API-Schlüssel wurde abgelehnt")]
    [TestCase(AiProviderType.GoogleGemini, 401, "Google Gemini", "API-Schlüssel wurde abgelehnt")]
    [TestCase(AiProviderType.MistralAi, 500, "Mistral AI", "gestört")]
    [TestCase(AiProviderType.MistralAi, 503, "Mistral AI", "gestört")]
    [TestCase(AiProviderType.MistralAi, 429, "Mistral AI", "Zu viele Anfragen")]
    public void ValidateConnectionAsync_Cloud_FehlerWirdVerstaendlichGemeldet(AiProviderType provider, int status, string providerName, string hint)
    {
        _handler.Respond((HttpStatusCode)status, "{\"message\":\"Unauthorized\"}");

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => CreateDispatcher().ValidateConnectionAsync(Config(provider)));

        Assert.That(ex!.StatusCode, Is.EqualTo(status));
        Assert.That(ex.ProviderName, Is.EqualTo(providerName));
        Assert.That(ex.Message, Does.Contain($"HTTP {status}").And.Contain(hint));
        Assert.That(ex.Message, Does.Not.Contain("DUMMY_TEST_KEY"), "Schlüssel darf nicht in der Meldung stehen");
    }

    [Test]
    public void ValidateConnectionAsync_Cloud_Netzwerkfehler_WirdVerstaendlichGemeldet()
    {
        _handler.Throw(new HttpRequestException("Host nicht gefunden"));

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => CreateDispatcher().ValidateConnectionAsync(Config(AiProviderType.MistralAi)));

        Assert.That(ex!.Message, Does.Contain("nicht erreichbar"));
    }

    [Test]
    public void ValidateConnectionAsync_Cloud_Zeitueberschreitung_WirdVerstaendlichGemeldet()
    {
        _handler.Delay = TimeSpan.FromSeconds(5);

        var ex = Assert.ThrowsAsync<LlmServiceException>(() =>
            CreateDispatcher(TimeSpan.FromMilliseconds(100)).ValidateConnectionAsync(Config(AiProviderType.MistralAi)));

        Assert.That(ex!.Message, Does.Contain("Zeitüberschreitung"));
    }

    [TestCase(AiProviderType.MistralAi)]
    [TestCase(AiProviderType.OpenAiGpt4)]
    [TestCase(AiProviderType.GoogleGemini)]
    public async Task ValidateConnectionAsync_Cloud_OhneSchluessel_FalschOhneAnfrage(AiProviderType provider)
    {
        var ok = await CreateDispatcher().ValidateConnectionAsync(Config(provider, apiKey: "  "));

        Assert.That(ok, Is.False);
        Assert.That(_handler.RequestCount, Is.Zero);
    }

    [TestCase("http://localhost:8080/v1", "http://localhost:8080/v1/models")]
    [TestCase("http://localhost:8080/v1/chat/completions", "http://localhost:8080/v1/models")]
    public async Task ValidateConnectionAsync_Custom_RuftModelllisteAb(string endpoint, string expectedUrl)
    {
        _handler.Respond(HttpStatusCode.OK, "{\"data\":[]}");

        var ok = await CreateDispatcher().ValidateConnectionAsync(Config(AiProviderType.CustomOpenAi, endpoint: endpoint));

        Assert.That(ok, Is.True);
        Assert.That(_handler.LastRequestUri!.ToString(), Is.EqualTo(expectedUrl));
    }

    [Test]
    public async Task ValidateConnectionAsync_Custom_OhneModellliste_GiltAlsErreichbar()
    {
        _handler.Respond(HttpStatusCode.NotFound, "");

        Assert.That(await CreateDispatcher().ValidateConnectionAsync(Config(AiProviderType.CustomOpenAi, endpoint: "http://localhost:8080/v1")), Is.True);
    }

    [Test]
    public void ValidateConnectionAsync_Custom_AbgelehnterSchluessel_WirdGemeldet()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, "");

        var ex = Assert.ThrowsAsync<LlmServiceException>(() =>
            CreateDispatcher().ValidateConnectionAsync(Config(AiProviderType.CustomOpenAi, endpoint: "http://localhost:8080/v1")));

        Assert.That(ex!.StatusCode, Is.EqualTo(401));
    }

    private static string? SystemMessage(JsonDocument body) =>
        body.RootElement.GetProperty("messages").EnumerateArray()
            .First(m => m.GetProperty("role").GetString() == "system")
            .GetProperty("content").GetString();

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
