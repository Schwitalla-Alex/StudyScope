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
    public string SelectedModelName { get; set; } = "Fake-KI (Testmodus)";
}