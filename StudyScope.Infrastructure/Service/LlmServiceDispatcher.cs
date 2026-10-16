using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

public class LlmServiceDispatcher : ILlmService
{
    private readonly IStorageService _storageService;
    private readonly HttpClient _httpClient;

    public LlmServiceDispatcher(IStorageService storageService, HttpClient? httpClient = null)
    {
        _storageService = storageService;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<bool> ValidateConnectionAsync()
    {
        var config = await _storageService.LoadAiConfigAsync() ?? new AiConfig();

        return config.SelectedProvider switch
        {
            AiProviderType.FakeAi => true,
            AiProviderType.OllamaLocal => await CheckHttpReachableAsync($"{config.LocalEndpointUrl.TrimEnd('/')}/api/tags"),
            AiProviderType.LmStudioLocal => await CheckHttpReachableAsync($"{config.LocalEndpointUrl.TrimEnd('/')}/v1/models"),
            AiProviderType.GoogleGemini => !string.IsNullOrWhiteSpace(config.ApiKey),
            AiProviderType.OpenAiGpt4 => !string.IsNullOrWhiteSpace(config.ApiKey),
            _ => false
        };
    }

    public async Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks)
    {
        var config = await _storageService.LoadAiConfigAsync() ?? new AiConfig();
        var chunks = contextChunks?.ToList() ?? new List<string>();

        return config.SelectedProvider switch
        {
            AiProviderType.FakeAi => AskFakeAi(prompt, chunks),
            AiProviderType.OllamaLocal => await AskOllamaAsync(config, prompt, chunks),
            AiProviderType.LmStudioLocal => await AskOpenAiCompatibleAsync(config, prompt, chunks, isLmStudio: true),
            AiProviderType.OpenAiGpt4 => await AskOpenAiCompatibleAsync(config, prompt, chunks, isLmStudio: false),
            AiProviderType.GoogleGemini => await AskGeminiAsync(config, prompt, chunks),
            _ => "Kein gültiger KI-Provider konfiguriert."
        };
    }

    private static string AskFakeAi(string prompt, List<string> chunks)
    {
        if (prompt.Contains("Lernkarteikarten", StringComparison.OrdinalIgnoreCase) || prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase))
        {
            var snippet = chunks.FirstOrDefault() ?? "Beispielinhalt";
            var preview = snippet.Substring(0, Math.Min(40, snippet.Length));
            return $$"""
            [
              {
                "question": "Was besagt der Textauszug?",
                "answer": "Der Auszug lautet auszugsweise: '{{preview}}...'"
              },
              {
                "question": "Wie viele Textstellen wurden analysiert?",
                "answer": "Es wurden genau {{chunks.Count}} Textstelle(n) analysiert."
              }
            ]
            """;
        }

        if (!chunks.Any())
            return $"[Testmodus] Keine Dokumentinhalte aktiv. Deine Frage war: '{prompt}'";

        var firstPreview = chunks.First().Substring(0, Math.Min(50, chunks.First().Length));
        return $"[Testmodus] Antwort basierend auf {chunks.Count} Textstelle(n).\nFrage: {prompt}\nAuszug: {firstPreview}...";
    }

    private async Task<string> AskOllamaAsync(AiConfig config, string prompt, List<string> chunks)
    {
        var url = $"{config.LocalEndpointUrl.TrimEnd('/')}/api/chat";
        var contextText = chunks.Any() ? string.Join("\n\n---\n\n", chunks) : string.Empty;

        var systemPrompt = "Du bist ein präziser Lern- und Studienassistent (StudyScope). Nutze die bereitgestellten Dokumentauszüge, um Fragen wahrheitsgemäß zu beantworten.";
        if (prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase))
        {
            systemPrompt += " Antworte AUSSCHLIESSLICH mit einem validen JSON-Array ohne Begleittext.";
        }

        var fullUserMessage = string.IsNullOrWhiteSpace(contextText)
            ? prompt
            : $"Kontext aus Dokumenten:\n{contextText}\n\nFrage/Aufgabe: {prompt}";

        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(config.SelectedModelName) ? "llama3.2" : config.SelectedModelName,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = fullUserMessage }
            }
        };

        var response = await _httpClient.PostAsync(url, new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private async Task<string> AskOpenAiCompatibleAsync(AiConfig config, string prompt, List<string> chunks, bool isLmStudio)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config?.LocalEndpointUrl)
            ? "http://localhost:1234"
            : config.LocalEndpointUrl.TrimEnd('/');

        var endpoint = isLmStudio
            ? $"{baseUrl}/v1/chat/completions"
            : "https://api.openai.com/v1/chat/completions";

        var contextText = (chunks != null && chunks.Any()) ? string.Join("\n\n---\n\n", chunks) : string.Empty;
        var fullUserMessage = string.IsNullOrWhiteSpace(contextText)
            ? prompt
            : $"Kontext:\n{contextText}\n\nAnfrage: {prompt}";

        var modelName = isLmStudio
            ? (string.IsNullOrWhiteSpace(config?.SelectedModelName) ? "local-model" : config.SelectedModelName)
            : "gpt-4o-mini";

        var requestBody = new
        {
            model = modelName,
            messages = new object[]
            {
            new { role = "system", content = "Du bist ein präziser Studienassistent. Falls nach JSON gefragt wird, antworte nur mit reinem JSON." },
            new { role = "user", content = fullUserMessage }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        if (!isLmStudio && !string.IsNullOrWhiteSpace(config?.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request);
        }
        catch (Exception ex)
        {
            return $"Verbindungsfehler zu {(isLmStudio ? "LM Studio" : "OpenAI")}: {ex.Message}";
        }

        var responseContent = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return $"API-Fehler ({response.StatusCode}): {responseContent}";
        }

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
            {
                return content.GetString() ?? string.Empty;
            }

            return "Unerwartetes Antwortformat vom Modell erhalten.";
        }
        catch (JsonException)
        {
            return $"Antwort konnte nicht als JSON geparst werden: {responseContent}";
        }
    }

    //private async Task<string> AskOpenAiCompatibleAsync(AiConfig config, string prompt, List<string> chunks, bool isLmStudio)
    //{
    //    var endpoint = isLmStudio
    //        ? $"{config.LocalEndpointUrl.TrimEnd('/')}/v1/chat/completions"
    //        : "https://api.openai.com/v1/chat/completions";

    //    var contextText = chunks.Any() ? string.Join("\n\n---\n\n", chunks) : string.Empty;
    //    var fullUserMessage = string.IsNullOrWhiteSpace(contextText)
    //        ? prompt
    //        : $"Kontext:\n{contextText}\n\nAnfrage: {prompt}";

    //    var modelName = isLmStudio
    //        ? (string.IsNullOrWhiteSpace(config.SelectedModelName) ? "local-model" : config.SelectedModelName)
    //        : "gpt-4o-mini";

    //    var requestBody = new
    //    {
    //        model = modelName,
    //        messages = new[]
    //        {
    //            new { role = "system", content = "Du bist ein präziser Studienassistent. Falls nach JSON gefragt wird, antworte nur mit reinem JSON." },
    //            new { role = "user", content = fullUserMessage }
    //        }
    //    };

    //    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    //    {
    //        Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
    //    };

    //    if (!isLmStudio && !string.IsNullOrWhiteSpace(config.ApiKey))
    //    {
    //        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
    //    }

    //    var response = await _httpClient.SendAsync(request);
    //    response.EnsureSuccessStatusCode();

    //    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    //    return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    //}

    private async Task<string> AskGeminiAsync(AiConfig config, string prompt, List<string> chunks)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={config.ApiKey}";
        var contextText = chunks.Any() ? string.Join("\n\n---\n\n", chunks) : string.Empty;

        var fullUserMessage = string.IsNullOrWhiteSpace(contextText)
            ? prompt
            : $"Kontext:\n{contextText}\n\nFrage: {prompt}";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = fullUserMessage } } }
            }
        };

        var response = await _httpClient.PostAsync(url, new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private async Task<bool> CheckHttpReachableAsync(string url)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var res = await _httpClient.GetAsync(url, cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}