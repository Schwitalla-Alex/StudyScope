using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using System.Net.Http.Headers;
using System.Text.Json;

namespace StudyScope.Infrastructure.Services;

public class LocalLlmDiscoveryService : ILocalLlmDiscoveryService
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly HttpClient _httpClient;

    /// <param name="httpClient">Für Tests austauschbar; Standard ist ein gemeinsamer Client mit kurzer Zeitgrenze.</param>
    public LocalLlmDiscoveryService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    /// <summary>Anzeigename des Cloud-Dienstes für Fehlermeldungen.</summary>
    private static string ProviderNameFor(string? customEndpoint) =>
        string.IsNullOrWhiteSpace(customEndpoint) ? "OpenAI"
        : customEndpoint.Contains("mistral.ai", StringComparison.OrdinalIgnoreCase) ? "Mistral AI"
        : "Der KI-Dienst";

    public async Task<List<string>> FetchOllamaModelsAsync(string endpoint)
    {
        var models = new List<string>();
        try
        {
            var url = $"{endpoint.TrimEnd('/')}/api/tags";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return models;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var m in modelsArray.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            models.Add(name);
                    }
                }
            }
        }
        catch
        {
            // Server offline oder Timeout
        }
        return models;
    }

    public async Task<List<string>> FetchLmStudioModelsAsync(string endpoint)
    {
        var models = new List<string>();
        try
        {
            var url = $"{endpoint.TrimEnd('/')}/v1/models";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return models;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var m in dataArray.EnumerateArray())
                {
                    if (m.TryGetProperty("id", out var idProp))
                    {
                        var id = idProp.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                            models.Add(id);
                    }
                }
            }
        }
        catch
        {
            // Server offline oder Timeout
        }
        return models;
    }

    public async Task<List<string>> FetchGeminiModelsAsync(string apiKey)
    {
        var models = new List<string>();
        if (string.IsNullOrWhiteSpace(apiKey)) return models;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models");
            req.Headers.Add("x-goog-api-key", apiKey.Trim());
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode)
                throw LlmServiceException.FromHttpStatus("Google Gemini", (int)response.StatusCode, await response.Content.ReadAsStringAsync());

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var m in modelsArray.EnumerateArray())
                {
                    // Nur Modelle übernehmen, die Text generieren können (generateContent)
                    var canGenerate = false;
                    if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                    {
                        foreach (var method in methods.EnumerateArray())
                        {
                            if (method.GetString() == "generateContent")
                            {
                                canGenerate = true;
                                break;
                            }
                        }
                    }

                    if (canGenerate && m.TryGetProperty("name", out var nameProp))
                    {
                        var fullName = nameProp.GetString(); // z. B. "models/gemini-2.5-flash"
                        if (!string.IsNullOrWhiteSpace(fullName))
                        {
                            var modelId = fullName.Replace("models/", "");

                            // Nur echte Chat-/Flash-/Pro-Modelle aufnehmen (keine Bildmodelle wie Imagen/Banana etc. und Veraltete Versionen ausschließen)
                            if (modelId.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) &&
                            !modelId.Contains("2.5") &&
                            !modelId.Contains("2.0") &&
                            !modelId.Contains("transcribe") &&
                            !modelId.Contains("banana"))
                            {
                                models.Add(modelId);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not LlmServiceException)
        {
            // Offline bzw. unlesbare Antwort: leere Liste („Keine Modelle gefunden“)
        }

        return models.OrderByDescending(m => m).ToList();
    }

    public async Task<List<string>> FetchOpenAiModelsAsync(string apiKey, string? customEndpoint = null)
    {
        var models = new List<string>();

        var baseUrl = string.IsNullOrWhiteSpace(customEndpoint)
            ? "https://api.openai.com/v1"
            : customEndpoint.TrimEnd('/');

        var url = baseUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/models";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            }

            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode)
                throw LlmServiceException.FromHttpStatus(ProviderNameFor(customEndpoint), (int)response.StatusCode, await response.Content.ReadAsStringAsync());

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var m in dataArray.EnumerateArray())
                {
                    if (m.TryGetProperty("id", out var idProp))
                    {
                        var id = idProp.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            // Bei OpenAI nur relevante Chat-/Text-Modelle filtern (keine Whisper/TTS/Embeddings)
                            if (string.IsNullOrWhiteSpace(customEndpoint))
                            {
                                if (id.Contains("gpt") || id.Contains("o1") || id.Contains("o3"))
                                    models.Add(id);
                            }
                            else
                            {
                                models.Add(id);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not LlmServiceException)
        {
            // Offline bzw. unlesbare Antwort: leere Liste („Keine Modelle gefunden“)
        }

        return models.OrderBy(m => m).ToList();
    }
}
