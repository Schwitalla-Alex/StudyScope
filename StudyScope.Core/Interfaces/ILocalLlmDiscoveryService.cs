namespace StudyScope.Core.Interfaces;

public interface ILocalLlmDiscoveryService
{
    Task<List<string>> FetchOllamaModelsAsync(string endpoint);
    Task<List<string>> FetchLmStudioModelsAsync(string endpoint);
}