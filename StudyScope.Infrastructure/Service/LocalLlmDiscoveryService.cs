using System.Text.Json;
using StudyScope.Core.Interfaces;

namespace StudyScope.Infrastructure.Services;

public class LocalLlmDiscoveryService : ILocalLlmDiscoveryService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

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
}
