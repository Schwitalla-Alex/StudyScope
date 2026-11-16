using System.Net;
using System.Text;
using NUnit.Framework;
using StudyScope.Core.Common;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>Modellabruf: HTTP-Fehler der Cloud-Dienste werden mit Status gemeldet statt verschluckt.</summary>
[TestFixture]
public class LocalLlmDiscoveryServiceTests
{
    private static LocalLlmDiscoveryService Create(HttpStatusCode status, string body, List<HttpRequestMessage>? requests = null) =>
        new(new HttpClient(new StubHandler(status, body, requests)));

    [Test]
    public void FetchOpenAiModelsAsync_Mistral401_MeldetAbgelehntenSchluessel()
    {
        var service = Create(HttpStatusCode.Unauthorized, "{\"message\":\"Unauthorized\"}");

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => service.FetchOpenAiModelsAsync("DUMMY_TEST_KEY_falsch", "https://api.mistral.ai/v1"));

        Assert.That(ex!.ProviderName, Is.EqualTo("Mistral AI"));
        Assert.That(ex.Message, Does.Contain("HTTP 401").And.Contain("API-Schlüssel"));
    }

    [Test]
    public void FetchOpenAiModelsAsync_Serverfehler_MeldetStoerung()
    {
        var service = Create(HttpStatusCode.InternalServerError, "{\"detail\":\"Internal server error\"}");

        var ex = Assert.ThrowsAsync<LlmServiceException>(() => service.FetchOpenAiModelsAsync("DUMMY_TEST_KEY_ok", "https://api.mistral.ai/v1"));

        Assert.That(ex!.Message, Does.Contain("HTTP 500").And.Contain("gestört"));
    }

    [Test]
    public async Task FetchOpenAiModelsAsync_Erfolg_LiefertModelle()
    {
        var service = Create(HttpStatusCode.OK, "{\"data\":[{\"id\":\"testmodell-b\"},{\"id\":\"testmodell-a\"}]}");

        var models = await service.FetchOpenAiModelsAsync("DUMMY_TEST_KEY_ok", "https://api.mistral.ai/v1");

        Assert.That(models, Is.EqualTo(new[] { "testmodell-a", "testmodell-b" }));
    }

    [Test]
    public async Task FetchGeminiModelsAsync_SchluesselImHeaderNichtInDerUrl()
    {
        var requests = new List<HttpRequestMessage>();
        var service = Create(HttpStatusCode.OK, "{\"models\":[]}", requests);

        await service.FetchGeminiModelsAsync("DUMMY_TEST_KEY_gemini");

        Assert.That(requests.Single().RequestUri!.Query, Does.Not.Contain("DUMMY_TEST_KEY"));
        Assert.That(requests.Single().Headers.GetValues("x-goog-api-key").Single(), Is.EqualTo("DUMMY_TEST_KEY_gemini"));
    }

    [Test]
    public async Task FetchOllamaModelsAsync_NichtErreichbar_LiefertLeereListe()
    {
        var service = Create(HttpStatusCode.ServiceUnavailable, "");

        var models = await service.FetchOllamaModelsAsync("http://localhost:11434");

        Assert.That(models, Is.Empty);
    }

    private sealed class StubHandler(HttpStatusCode status, string body, List<HttpRequestMessage>? requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests?.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
