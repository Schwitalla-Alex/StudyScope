using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;

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

    /// <summary>
    /// Lädt die KI-Konfiguration des aktiven Profils (so wie sie auch die Statusleiste anzeigt).
    /// Nur wenn das Profil gar keine Einstellung enthält, gilt die globale config.json als Rückfall.
    /// </summary>
    private async Task<AiConfig> LoadActiveConfigAsync()
    {
        var activeProfile = await _storageService.GetLastActiveProfileNameAsync();
        if (!string.IsNullOrWhiteSpace(activeProfile))
        {
            var profileConfig = await _storageService.LoadAiConfigForProfileAsync(activeProfile);
            if (profileConfig != null)
                return profileConfig;
        }

        return await _storageService.LoadAiConfigAsync() ?? new AiConfig();
    }

    public async Task<bool> ValidateConnectionAsync(AiConfig? config = null)
    {
        config ??= await LoadActiveConfigAsync();

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
        var config = await LoadActiveConfigAsync();
        var chunks = contextChunks?.ToList() ?? new List<string>();

        try
        {
            return config.SelectedProvider switch
            {
                AiProviderType.FakeAi => AskFakeAi(prompt, chunks),
                AiProviderType.OllamaLocal => await AskOllamaAsync(config, prompt, chunks, cancellationToken),
                AiProviderType.LmStudioLocal => await AskOpenAiCompatibleAsync(config, prompt, chunks, isLmStudio: true,
                    providerName: "LM Studio", cancellationToken: cancellationToken),
                AiProviderType.OpenAiGpt4 => await AskOpenAiCompatibleAsync(config, prompt, chunks, isLmStudio: false,
                    providerName: "OpenAI", useJsonMode: IsStructuredJsonPrompt(prompt), cancellationToken: cancellationToken),
                AiProviderType.MistralAi => await AskMistralAsync(config, prompt, chunks, cancellationToken),
                AiProviderType.CustomOpenAi => await AskOpenAiCompatibleAsync(config, prompt, chunks, isLmStudio: false,
                    providerName: "Der benutzerdefinierte KI-Dienst", cancellationToken: cancellationToken),
                AiProviderType.GoogleGemini => await AskGeminiAsync(config, prompt, chunks, cancellationToken),
                _ => "Kein gültiger KI-Provider konfiguriert."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Nur ein echter Abbruch durch den Benutzer; Zeitüberschreitungen melden die Dienste als LlmServiceException
            return CancelledResponse;
        }
    }

    /// <summary>Antwort bei Abbruch durch den Benutzer (die Dienste werten sie nicht als Inhalt aus).</summary>
    public const string CancelledResponse = "[Anfrage durch Benutzer abgebrochen]";

    private static string AskFakeAi(string prompt, List<string> chunks)
    {
        // Bericht vor allem anderen: eine eigene Vorgabe des Benutzers kann "Mindmap" o. Ä. enthalten
        if (FakeReportResponse.IsReportPrompt(prompt))
            return FakeReportResponse.Create(prompt, chunks);

        // Mindmap zuerst prüfen: Ihr Prompt enthält ebenfalls "JSON"
        if (FakeMindmapResponse.IsMindmapPrompt(prompt))
            return FakeMindmapResponse.Create(chunks);

        if (FakePresentationResponse.IsPresentationPrompt(prompt))
            return FakePresentationResponse.Create(chunks);

        if (FakeQuizResponse.IsTopicPrompt(prompt))
            return FakeQuizResponse.CreateTopics(chunks);

        if (FakeQuizResponse.IsQuestionPrompt(prompt))
            return FakeQuizResponse.CreateQuestions(prompt, chunks);

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

        var systemPrompt = WantsJson(prompt) ? JsonSystemPrompt : RagSystemPrompt;

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

        const string providerName = "Ollama";
        using var response = await SendAsync(providerName, () => _httpClient.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
            cancellationToken), cancellationToken);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw LlmServiceException.FromHttpStatus(providerName, (int)response.StatusCode, responseContent);

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                return content.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            // unten als unerwartete Antwort melden
        }

        throw UnexpectedResponse(providerName, (int)response.StatusCode, responseContent);
    }

    private async Task<string> AskMistralAsync(AiConfig config, string prompt, List<string> chunks, CancellationToken cancellationToken)
    {
        var mistralConfig = new AiConfig
        {
            ApiKey = config.ApiKey,
            LocalEndpointUrl = "https://api.mistral.ai/v1",
            SelectedModelName = string.IsNullOrWhiteSpace(config.SelectedModelName) ? "mistral-small-latest" : config.SelectedModelName
        };

        return await AskOpenAiCompatibleAsync(mistralConfig, prompt, chunks, isLmStudio: false, explicitBaseUrl: "https://api.mistral.ai/v1",
            providerName: "Mistral AI", useJsonMode: IsStructuredJsonPrompt(prompt), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Systemanweisung für Chat-Antworten: nur aus den Dokumentauszügen antworten, nichts erfinden.
    /// </summary>
    public const string RagSystemPrompt =
        "Du bist ein präziser Lern- und Studienassistent (StudyScope). Beantworte Fragen ausschließlich auf Grundlage der bereitgestellten Dokumentauszüge. " +
        "Steht die Antwort nicht in den Auszügen, sage das ausdrücklich (z. B. „Dazu enthalten Ihre Unterlagen keine Angaben.“) und erfinde nichts – " +
        "keine Schritte, Zahlen, Namen oder Adressen, die nicht in den Auszügen stehen. " +
        "Ergänzendes Allgemeinwissen nur, wenn ausdrücklich danach gefragt wird, und dann deutlich als solches kennzeichnen. " +
        "Antworte verständlich als Fließtext auf Deutsch.";

    /// <summary>Systemanweisung für strukturierte Antworten (Karteikarten, Mindmap, Quiz, Präsentation, Bericht).</summary>
    public const string JsonSystemPrompt =
        "Du bist ein präziser Studienassistent. Antworte AUSSCHLIESSLICH mit einem validen JSON-Array ohne Begleittext oder Markdown. " +
        "Verwende nur Inhalte aus den bereitgestellten Auszügen.";

    /// <summary>Systemanweisung im JSON-Modus (Mistral/OpenAI): Der Dienst erzwingt ein JSON-Objekt, das Array steht in „items“.</summary>
    public const string JsonModeSystemPrompt =
        "Du bist ein präziser Studienassistent. Antworte AUSSCHLIESSLICH mit einem gültigen JSON-Objekt der Form {\"items\": [...]}. " +
        "\"items\" enthält genau das JSON-Array, das in der Aufgabe verlangt wird. Verwende nur Inhalte aus den bereitgestellten Auszügen.";

    /// <summary>Strukturierte Aufträge der Dienste verlangen „als gültiges JSON-Array“ – Chatfragen mit dem Wort „JSON“ nicht.</summary>
    public static bool IsStructuredJsonPrompt(string prompt) =>
        prompt.Contains("JSON-Array", StringComparison.OrdinalIgnoreCase);

    private static bool WantsJson(string prompt) =>
        prompt.Contains("JSON", StringComparison.OrdinalIgnoreCase) || prompt.Contains("Lernkarteikarten", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Holt im JSON-Modus das Array aus {"items": [...]} (bzw. aus der einzigen Array-Eigenschaft) heraus,
    /// damit die Dienste wie gewohnt ein JSON-Array erhalten. Andere Antworten bleiben unverändert.
    /// </summary>
    public static string UnwrapJsonItems(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return content;

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                return items.GetRawText();

            var properties = root.EnumerateObject().ToList();
            if (properties.Count == 1 && properties[0].Value.ValueKind == JsonValueKind.Array)
                return properties[0].Value.GetRawText();

            // Ein einzelnes Objekt (z. B. Mindmap-Wurzel) als Array mit einem Element weitergeben
            return "[" + root.GetRawText() + "]";
        }
        catch (JsonException)
        {
            return content; // nachsichtige Auswertung (Reparatur) übernehmen die Dienste
        }
    }

    private async Task<string> AskOpenAiCompatibleAsync(
        AiConfig config,
        string prompt,
        List<string> chunks,
        bool isLmStudio,
        string? explicitBaseUrl = null,
        string providerName = "Der KI-Dienst",
        bool useJsonMode = false,
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
        var systemContent = useJsonMode ? JsonModeSystemPrompt
            : WantsJson(prompt) ? JsonSystemPrompt
            : RagSystemPrompt;

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = modelName,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemContent },
                new { role = "user", content = fullUserMessage }
            }
        };

        // JSON-Modus (Mistral, OpenAI): Der Dienst liefert garantiert gültiges JSON – verhindert kaputte Strukturen mitten in der Antwort
        if (useJsonMode)
            requestBody["response_format"] = new { type = "json_object" };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };

        if (!isLmStudio && !string.IsNullOrWhiteSpace(config?.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        }

        using var response = await SendAsync(providerName, () => _httpClient.SendAsync(request, cancellationToken), cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw LlmServiceException.FromHttpStatus(providerName, (int)response.StatusCode, responseContent);

        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("content", out var content))
            {
                var text = content.GetString() ?? string.Empty;
                return useJsonMode ? UnwrapJsonItems(text) : text;
            }
        }
        catch (JsonException)
        {
            // unten als unerwartete Antwort melden
        }

        throw UnexpectedResponse(providerName, (int)response.StatusCode, responseContent);
    }

    private static LlmServiceException UnexpectedResponse(string providerName, int statusCode, string responseContent) =>
        new($"{providerName} hat eine unerwartete Antwort geliefert. Bitte Modell und Server-URL in den Einstellungen prüfen.",
            providerName, statusCode, responseContent.Length > 500 ? responseContent[..500] + " …" : responseContent);

    /// <summary>
    /// Sendet eine Anfrage und übersetzt Netzwerkfehler und Zeitüberschreitung in eine <see cref="LlmServiceException"/>.
    /// Ein Abbruch durch den Benutzer bleibt eine <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(string providerName, Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new LlmServiceException(
                $"{providerName} hat nicht rechtzeitig geantwortet (Zeitüberschreitung nach {_httpClient.Timeout.TotalSeconds:0} s). Bitte erneut versuchen oder weniger Dokumente aktivieren.",
                providerName, null, ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            throw LlmServiceException.FromNetworkError(providerName, ex);
        }
    }

    private async Task<string> AskGeminiAsync(AiConfig config, string prompt, List<string> chunks, CancellationToken cancellationToken = default)
    {
        const string providerName = "Google Gemini";
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new LlmServiceException("Für Google Gemini ist kein API-Schlüssel hinterlegt. Bitte in den Einstellungen eintragen.", providerName);
        }

        var model = string.IsNullOrWhiteSpace(config.SelectedModelName) || config.SelectedModelName.Equals("Google Gemini", StringComparison.OrdinalIgnoreCase)
            ? "gemini-2.0-flash"
            : config.SelectedModelName.Trim();

        // Schlüssel im Header statt in der URL (landet so nicht in Fehlermeldungen oder Protokollen)
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

        var contextText = chunks.Any() ? string.Join("\n\n---\n\n", chunks) : string.Empty;
        var fullUserMessage = string.IsNullOrWhiteSpace(contextText)
            ? prompt
            : $"Kontext:\n{contextText}\n\nFrage: {prompt}";

        var systemText = WantsJson(prompt) ? JsonSystemPrompt : RagSystemPrompt;
        var requestBody = new
        {
            system_instruction = new { parts = new[] { new { text = systemText } } },
            contents = new[]
            {
                new { parts = new[] { new { text = fullUserMessage } } }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", config.ApiKey.Trim());

        using var response = await SendAsync(providerName, () => _httpClient.SendAsync(request, cancellationToken), cancellationToken);
        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw LlmServiceException.FromHttpStatus(providerName, (int)response.StatusCode,
                string.IsNullOrWhiteSpace(responseString) ? $"Modell '{model}' nicht gefunden." : responseString);
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
        }
        catch (JsonException)
        {
            // unten als unerwartete Antwort melden
        }

        throw UnexpectedResponse(providerName, (int)response.StatusCode, responseString);
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