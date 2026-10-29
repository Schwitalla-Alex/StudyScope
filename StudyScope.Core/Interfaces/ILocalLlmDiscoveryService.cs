namespace StudyScope.Core.Interfaces;

public interface ILocalLlmDiscoveryService
{
    Task<List<string>> FetchOllamaModelsAsync(string endpoint);
    Task<List<string>> FetchLmStudioModelsAsync(string endpoint);
        
    // Neu für Cloud und OpenAI-kompatible Provider:
    Task<List<string>> FetchGeminiModelsAsync(string apiKey);
    Task<List<string>> FetchOpenAiModelsAsync(string apiKey, string? customEndpoint = null);
}