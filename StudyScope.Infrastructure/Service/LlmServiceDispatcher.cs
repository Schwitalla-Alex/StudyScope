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
            AiProviderType.OllamaLocal => await CheckHttpReachableAsync($"{config.LocalEndpointUrl?.TrimEnd('/')}/api/tags"),
            AiProviderType.LmStudioLocal => await CheckHttpReachableAsync($"{config.LocalEndpointUrl?.TrimEnd('/')}/v1/models"),
            AiProviderType.GoogleGemini => !string.IsNullOrWhiteSpace(config.ApiKey),
            AiProviderType.OpenAiGpt4 => !string.IsNullOrWhiteSpace(config.ApiKey),
            AiProviderType.MistralAi => !string.IsNullOrWhiteSpace(config.ApiKey),
            AiProviderType.CustomOpenAi => !string.IsNullOrWhiteSpace(config.LocalEndpointUrl),
            _ => false
        };
    }

    public async Task<string> AskQuestionAsync(string prompt, IEnumerable<string> contextChunks, CancellationToken cancellationToken = default)
    {
        var config = await _storageService.LoadAiConfigAsync() ?? new AiConfig();
        var chunks = contextChunks?.ToList() ?? new List<string>();

        try
        {
            return config.SelectedProvider switch
            {
                AiProviderType.FakeAi => AskFakeAi(prompt, chunks),
                AiProviderType.OllamaLocal => await AskOllamaAsync(config, prompt, chunks, cancellationToken),
                AiProviderType.LmStudioLocal => await AskOpenAiCompatibleAsync(config, prompt, chunks, isLmStudio: true, cancellationToken: cancellationToken),
                AiProviderType.OpenAiGpt4 => await AskOpenAiCompatibleAsync(config, prompt, chunks, isLmStudio: false, cancellationToken: cancellationToken),
                AiProviderType.MistralAi => await AskMistralAsync(config, prompt, chunks, cancellationToken),
                AiProviderType.CustomOpenAi => await AskOpenAiCompatibleAsync(config, prompt, chunks, isLmStudio: false, cancellationToken: cancellationToken),
                AiProviderType.GoogleGemini => await AskGeminiAsync(config, prompt, chunks, cancellationToken),
                _ => "Kein gültiger KI-Provider konfiguriert."
            };
        }
        catch (OperationCanceledException)
        {
            return "[Anfrage durch Benutzer abgebrochen]";
        }
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

    private async Task<string> AskOllamaAsync(AiConfig config, string prompt, List<string> chunks, CancellationToken cancellationToken)
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

        var response = await _httpClient.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private async Task<string> AskMistralAsync(AiConfig config, string prompt, List<string> chunks, CancellationToken cancellationToken)
    {
        var mistralConfig = new AiConfig
        {
            ApiKey = config.ApiKey,
            LocalEndpointUrl = "https://api.mistral.ai/v1",
            SelectedModelName = string.IsNullOrWhiteSpace(config.SelectedModelName) ? "mistral-small-latest" : config.SelectedModelName
        };

        return await AskOpenAiCompatibleAsync(mistralConfig, prompt, chunks, isLmStudio: false, explicitBaseUrl: "https://api.mistral.ai/v1", cancellationToken: cancellationToken);
    }

    private async Task<string> AskOpenAiCompatibleAsync(
        AiConfig config,
        string prompt,
        List<string> chunks,
        bool isLmStudio,
        string? explicitBaseUrl = null,
        CancellationToken cancellationToken = default)
    {
        string endpoint;
        if (!string.IsNullOrWhiteSpace(explicitBaseUrl))
        {
            endpoint = $"{explicitBaseUrl.TrimEnd('/')}/chat/completions";
        }
        else if (isLmStudio)
        {
            var baseUrl = string.IsNullOrWhiteSpace(config?.LocalEndpointUrl) ? "http://localhost:1234" : config.LocalEndpointUrl.TrimEnd('/');
            endpoint = $"{baseUrl}/v1/chat/completions";
        }
        else if (config.SelectedProvider == AiProviderType.CustomOpenAi && !string.IsNullOrWhiteSpace(config.LocalEndpointUrl))
        {
            var baseCustom = config.LocalEndpointUrl.TrimEnd('/');
            endpoint = baseCustom.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? baseCustom
                : $"{baseCustom}/chat/completions";
        }
        else
        {
            endpoint = "https://api.openai.com/v1/chat/completions";
        }

        var contextText = (chunks != null && chunks.Any()) ? string.Join("\n\n---\n\n", chunks) : string.Empty;
        var fullUserMessage = string.IsNullOrWhiteSpace(contextText)
            ? prompt
            : $"Kontext:\n{contextText}\n\nAnfrage: {prompt}";

        var modelName = isLmStudio
            ? (string.IsNullOrWhiteSpace(config?.SelectedModelName) ? "local-model" : config.SelectedModelName)
            : (string.IsNullOrWhiteSpace(config?.SelectedModelName) ? "gpt-4o-mini" : config.SelectedModelName);

        // Dynamischer System-Prompt: JSON nur verlangen, wenn es explizit gefordert wurde!
        var systemContent = "Du bist ein präziser Studien- und Lernassistent (StudyScope). Beantworte Fragen auf Basis des bereitgestellten Kontexts verständlich und als Fließtext.";
        if (prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase) || prompt.Contains("Lernkarteikarten", StringComparison.OrdinalIgnoreCase))
        {
            systemContent = "Du bist ein präziser Studienassistent. Antworte AUSSCHLIESSLICH mit einem validen JSON-Array ohne Begleittext oder Markdown.";
        }

        var requestBody = new
        {
            model = modelName,
            messages = new object[]
            {
            new { role = "system", content = systemContent },
            new { role = "user", content = fullUserMessage }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        if (!isLmStudio && !string.IsNullOrWhiteSpace(config?.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Verbindungsfehler: {ex.Message}";
        }

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

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

    private async Task<string> AskGeminiAsync(AiConfig config, string prompt, List<string> chunks, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return "Gemini-Fehler: Es ist kein API-Schlüssel in den Einstellungen hinterlegt.";
        }

        var model = string.IsNullOrWhiteSpace(config.SelectedModelName) || config.SelectedModelName.Equals("Google Gemini", StringComparison.OrdinalIgnoreCase)
            ? "gemini-2.0-flash"
            : config.SelectedModelName.Trim();

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(config.ApiKey.Trim())}";

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

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(url, content, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"Netzwerkfehler zu Google Gemini: {ex.Message}";
        }

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = string.IsNullOrWhiteSpace(responseString)
                ? $"Das Modell '{model}' wurde unter der angegebenen URL nicht gefunden (404)."
                : responseString;

            return $"Gemini-API-Fehler ({response.StatusCode}): {detail}";
        }

        try
        {
            using var doc = JsonDocument.Parse(responseString);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var candContent) &&
                candContent.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var text))
            {
                return text.GetString() ?? string.Empty;
            }

            return "Keine Textantwort von Gemini im Rückgabepaket enthalten.";
        }
        catch (JsonException)
        {
            return $"Antwort konnte nicht als JSON verarbeitet werden: {responseString}";
        }
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