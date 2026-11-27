using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Websuche über Tavily (POST https://api.tavily.com/search, Schlüssel im Header „Authorization: Bearer …“).
/// Immer <c>search_depth: basic</c> (1 Credit je Suche), ohne KI-Antwort von Tavily und ohne Volltext der Seiten.
/// </summary>
public sealed class TavilySearchService : IWebSearchProvider
{
    public const string Endpoint = "https://api.tavily.com/search";
    public const string ProviderName = "Tavily";

    /// <summary>Verbrauch und Grenzen des Schlüssels (GET, Schlüssel im Header) – Prüfweg beim Speichern ohne Suche.</summary>
    public const string UsageEndpoint = "https://api.tavily.com/usage";

    private readonly HttpClient _http;

    public TavilySearchService(HttpClient http) => _http = http;

    public string Name => ProviderName;

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(WebSearchSettings settings, WebSearchQuery query, CultureInfo language,
        int maxResults, CancellationToken cancellationToken)
    {
        var key = settings.TavilyApiKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
            throw new WebSearchException(InfraStrings.WebSearch_TavilyNoKey, ProviderName, kind: WebSearchErrorKind.NotConfigured);

        var body = BuildRequestBody(query, maxResults);
        var (status, responseBody) = await WebSearchHttp.SendAsync(_http, ProviderName, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }, cancellationToken);

        if (status is < 200 or >= 300)
            throw WebSearchException.FromHttpStatus(ProviderName, status, responseBody);

        return WebSearchHttp.Clean(ParseResults(responseBody, status), maxResults);
    }

    /// <summary>
    /// Zugangsprüfung über GET /usage (keine Suche, laut Credit-Tabelle ohne Credit). Ist der Endpunkt nicht verfügbar
    /// (404/405), prüft eine Suche nach „StudyScope“ mit einem Treffer (1 Credit, <see cref="WebSearchVerification.UsedCredit"/>).
    /// 401/403 → abgelehnt, 429/432/433 → Grenze, 5xx/Netzwerk/Zeitüberschreitung → nicht erreichbar (als Ausnahme).
    /// </summary>
    public async Task<WebSearchVerification> VerifyAsync(WebSearchSettings settings, CancellationToken cancellationToken)
    {
        var key = settings.TavilyApiKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
            throw new WebSearchException(InfraStrings.WebSearch_TavilyNoKey, ProviderName, kind: WebSearchErrorKind.NotConfigured);

        var (status, body) = await WebSearchHttp.SendAsync(_http, ProviderName, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }, cancellationToken);

        if (status is 404 or 405)
        {
            await SearchAsync(settings, WebSearchQuery.ConnectionTest(), CultureInfo.InvariantCulture, 1, cancellationToken);
            return WebSearchVerification.Ok(usedCredit: true);
        }
        if (status is < 200 or >= 300)
            throw WebSearchException.FromHttpStatus(ProviderName, status, body);

        return WebSearchVerification.Ok(ParseUsage(body));
    }

    /// <summary>
    /// Verbrauch aus der Antwort von GET /usage: bevorzugt der Tarif (<c>account.plan_usage</c>/<c>plan_limit</c>),
    /// sonst der Schlüssel (<c>key.usage</c>/<c>limit</c>, null = ohne Grenze). Unlesbare Antwort → null.
    /// </summary>
    public static WebSearchUsage? ParseUsage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object &&
                GetLong(account, "plan_usage") is { } planUsage && GetLong(account, "plan_limit") is { } planLimit)
                return new WebSearchUsage(planUsage, planLimit);

            if (root.TryGetProperty("key", out var keyInfo) && keyInfo.ValueKind == JsonValueKind.Object &&
                GetLong(keyInfo, "usage") is { } keyUsage)
                return new WebSearchUsage(keyUsage, GetLong(keyInfo, "limit"));

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? GetLong(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0
            ? number
            : null;

    /// <summary>
    /// Anfrage an Tavily. Enthält ausschließlich die Suchanfrage und feste Optionen – keine Auszüge, keine Dateinamen.
    /// Kein <c>max_age_hours</c> (kann im Gratis-Tarif HTTP 434 auslösen).
    /// </summary>
    public static string BuildRequestBody(WebSearchQuery query, int maxResults)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("query", query.Text);
            writer.WriteString("search_depth", "basic");
            writer.WriteNumber("max_results", WebSearchSettings.ClampMaxResults(maxResults));
            writer.WriteBoolean("include_answer", false);
            writer.WriteBoolean("include_raw_content", false);
            writer.WriteBoolean("include_images", false);
            writer.WriteBoolean("include_published_date", true);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Liest <c>results[].title/url/content/published_date</c>; ohne Treffer-Liste → unerwartete Antwort.</summary>
    public static IReadOnlyList<WebSearchResult> ParseResults(string responseBody, int status = 200)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                throw WebSearchException.UnexpectedResponse(ProviderName, status, responseBody);
            }

            var list = new List<WebSearchResult>();
            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                list.Add(new WebSearchResult(
                    GetString(item, "title"),
                    GetString(item, "url"),
                    GetString(item, "content"),
                    WebSearchHttp.ParseDate(GetString(item, "published_date"))));
            }
            return list;
        }
        catch (JsonException)
        {
            throw WebSearchException.UnexpectedResponse(ProviderName, status, responseBody);
        }
    }

    private static string GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
