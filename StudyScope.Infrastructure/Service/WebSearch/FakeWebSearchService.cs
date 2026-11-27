using System.Globalization;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Feste Testtreffer unter https://example.org/… ohne Netzwerkzugriff – nur für Tests und Oberflächenprüfungen
/// (<see cref="WebSearchDispatcher.FakeEnvironmentVariable"/> = 1). Titel und Ausschnitte in der Ergebnissprache
/// (Englisch bei „en“, sonst Deutsch). Wie die Fake-KI-Antworten bewusst nicht in einer .resx.
/// </summary>
public sealed class FakeWebSearchService : IWebSearchProvider
{
    public const string ProviderName = "Fake";

    public string Name => ProviderName;

    public Task<IReadOnlyList<WebSearchResult>> SearchAsync(WebSearchSettings settings, WebSearchQuery query, CultureInfo language,
        int maxResults, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Create(query, language, maxResults));
    }

    public static IReadOnlyList<WebSearchResult> Create(WebSearchQuery query, CultureInfo language, int maxResults)
    {
        var english = language.TwoLetterISOLanguageName == "en";
        var topic = query.Text.Length <= 60 ? query.Text : query.Text[..60].TrimEnd() + " …";
        var published = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

        var all = english
            ? new[]
            {
                new WebSearchResult($"Overview: {topic}", "https://example.org/overview", $"Test result (fake search) on “{topic}”: a short overview from an invented web page.", published),
                new WebSearchResult("Background and current figures", "https://example.org/background", "Test result (fake search): invented figures for checking the display of web sources.", published),
                new WebSearchResult("Frequently asked questions", "https://example.org/faq", "Test result (fake search): questions and answers on the topic, without real content.")
            }
            : new[]
            {
                new WebSearchResult($"Überblick: {topic}", "https://example.org/ueberblick", $"Testtreffer (Fake-Suche) zu „{topic}“: kurzer Überblick von einer erfundenen Webseite.", published),
                new WebSearchResult("Hintergrund und aktuelle Zahlen", "https://example.org/hintergrund", "Testtreffer (Fake-Suche): erfundene Zahlen zur Prüfung der Anzeige von Web-Quellen.", published),
                new WebSearchResult("Häufige Fragen", "https://example.org/fragen", "Testtreffer (Fake-Suche): Fragen und Antworten zum Thema, ohne echten Inhalt.")
            };

        return WebSearchHttp.Clean(all, maxResults);
    }
}
