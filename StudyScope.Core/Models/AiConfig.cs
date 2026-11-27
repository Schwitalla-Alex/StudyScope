using System.Text.Json.Serialization;

namespace StudyScope.Core.Models;

/// <summary>
/// Hält die Konfiguration für die verschiedenen KI-Anbindungen (Fake-KI, Ollama, LM Studio, Gemini, OpenAI).
/// </summary>
public enum AiProviderType
{
    FakeAi,        // Testmodus
    OllamaLocal,   // Lokale KI (Ollama)
    LmStudioLocal, // Lokale KI (LM Studio)
    GoogleGemini,  // Öffentliche KI
    OpenAiGpt4,    // Öffentliche KI
    CustomOpenAi,   // Benutzerdefiniert (OpenAI-kompatibel: Groq, DeepSeek, Mistral, vLLM, OpenRouter etc.)
    MistralAi       // Öffentliche KI (Mistral AI)
}

public class AiConfig
{
    public AiProviderType SelectedProvider { get; set; } = AiProviderType.FakeAi;
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "llama3.2:latest";
    public string LocalEndpointUrl { get; set; } = "http://localhost:11434"; // Default für Ollama
    /// <summary>Gespeicherter Modellname der Fake-KI (Daten, bleibt deutsch; angezeigt wird ein übersetzter Name).</summary>
    public const string FakeModelName = "Fake-KI (Testmodus)";

    public string SelectedModelName { get; set; } = FakeModelName;

    /// <summary>
    /// Nur im Speicher: Der gespeicherte Schlüssel konnte nicht gelesen werden (anderer Computer oder anderes Konto).
    /// <see cref="ApiKey"/> ist dann leer; der gespeicherte Wert bleibt auf der Platte unverändert, bis ein neuer
    /// Schlüssel eingetragen oder er ausdrücklich entfernt wird.
    /// </summary>
    [JsonIgnore]
    public bool ApiKeyUnreadable { get; set; }

    /// <summary>Schlüssel muss neu eingetragen werden (nicht lesbar und kein neuer eingetragen).</summary>
    [JsonIgnore]
    public bool ApiKeyNeedsReentry => ApiKeyUnreadable && string.IsNullOrWhiteSpace(ApiKey);
}