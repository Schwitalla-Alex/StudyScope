using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using StudyScope.Core.Common;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Websuche über eine SearXNG-Instanz (GET &lt;Adresse&gt;/search?format=json&amp;q=…&amp;language=…).
/// Das JSON-Format muss in der Instanz freigeschaltet sein (<c>search.formats</c> in settings.yml), sonst HTTP 403.
/// </summary>
public sealed class SearxngSearchService : IWebSearchProvider
{
    public const string ProviderName = "SearXNG";

    private readonly HttpClient _http;

    public SearxngSearchService(HttpClient http) => _http = http;

    public string Name => ProviderName;

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(WebSearchSettings settings, WebSearchQuery query, CultureInfo language,
        int maxResults, CancellationToken cancellationToken)
    {
        if (!TryBuildSearchUri(settings.SearxngUrl, query, language, out var uri))
            throw new WebSearchException(InfraStrings.WebSearch_SearxngInvalidUrl, ProviderName, kind: WebSearchErrorKind.Rejected);

        var (status, body) = await WebSearchHttp.SendAsync(_http, ProviderName, () =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }, cancellationToken);

        if (status == 403)
            throw new WebSearchException(InfraStrings.WebSearch_SearxngJsonDisabled, ProviderName, status, body, kind: WebSearchErrorKind.Rejected);
        if (status is < 200 or >= 300)
            throw WebSearchException.FromHttpStatus(ProviderName, status, body);

        return WebSearchHttp.Clean(ParseResults(body, status), maxResults);
    }

    /// <summary>
    /// Baut die Suchadresse aus der Basisadresse der Instanz. Eine angegebene Adresse mit „/search“ am Ende und
    /// Abfrageteile werden toleriert. Nur http/https ohne Benutzerdaten.
    /// </summary>
    public static bool TryBuildSearchUri(string? baseUrl, WebSearchQuery query, CultureInfo language, out Uri uri)
    {
        uri = null!;
        if (!ExternalUrl.TryParse(baseUrl, out var parsed)) return false;

        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/search", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/search".Length];

        var languageCode = string.IsNullOrEmpty(language.TwoLetterISOLanguageName) || language.TwoLetterISOLanguageName == "iv"
            ? "all"
            : language.TwoLetterISOLanguageName;

        var builder = new UriBuilder(parsed)
        {
            Path = path + "/search",
            Query = "format=json&q=" + Uri.EscapeDataString(query.Text) + "&language=" + Uri.EscapeDataString(languageCode),
            Fragment = string.Empty
        };
        uri = builder.Uri;
        return true;
    }

    /// <summary>Liest <c>results[].title/url/content/publishedDate</c>; ohne Treffer-Liste → unerwartete Antwort.</summary>
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
                    WebSearchHttp.ParseDate(GetString(item, "publishedDate"))));
            }
            return list;
        }
        catch (JsonException)
        {
            // Häufig eine HTML-Seite statt JSON (falsche Adresse oder Format nicht freigeschaltet)
            throw new WebSearchException(InfraStrings.WebSearch_SearxngNoJson, ProviderName, status, responseBody, kind: WebSearchErrorKind.Rejected);
        }
    }

    private static string GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
