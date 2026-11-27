using System.Globalization;
using System.Net.Http;
using System.Text;
using StudyScope.Core.Common;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>Ein konkreter Suchdienst (Tavily, SearXNG, Fake). Die Auswahl trifft <see cref="WebSearchDispatcher"/>.</summary>
public interface IWebSearchProvider
{
    /// <summary>Eigenname des Dienstes für Meldungen („Tavily“, „SearXNG“).</summary>
    string Name { get; }

    Task<IReadOnlyList<WebSearchResult>> SearchAsync(WebSearchSettings settings, WebSearchQuery query, CultureInfo language,
        int maxResults, CancellationToken cancellationToken);

    /// <summary>
    /// Zugangsprüfung beim Speichern. Standard: eine Suche nach <see cref="WebSearchQuery.ConnectionTestText"/> mit einem
    /// Treffer (SearXNG: eigene Instanz, kostenlos). Fehler kommen als <see cref="WebSearchException"/>.
    /// </summary>
    async Task<WebSearchVerification> VerifyAsync(WebSearchSettings settings, CancellationToken cancellationToken)
    {
        await SearchAsync(settings, WebSearchQuery.ConnectionTest(), CultureInfo.InvariantCulture, 1, cancellationToken);
        return WebSearchVerification.Ok();
    }
}

/// <summary>Gemeinsame Hilfen der Suchdienste: Senden mit Zeitlimit und Fehlerübersetzung, Bereinigen der Treffer.</summary>
public static class WebSearchHttp
{
    /// <summary>Wartezeit je Suchanfrage.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>Höchstlänge eines Ausschnitts (Zeichen); längere Texte werden an einer Wortgrenze gekürzt.</summary>
    public const int MaxSnippetLength = 1500;

    /// <summary>Höchstlänge eines Titels (Zeichen).</summary>
    public const int MaxTitleLength = 200;

    /// <summary>
    /// Sendet die Anfrage mit eigenem Zeitlimit (<see cref="Timeout"/>) und liefert Statuscode und Antworttext.
    /// Abbruch durch den Benutzer bleibt eine <see cref="OperationCanceledException"/>; Zeitüberschreitung und
    /// Netzwerkfehler werden zur <see cref="WebSearchException"/>.
    /// </summary>
    public static async Task<(int Status, string Body)> SendAsync(HttpClient http, string providerName,
        Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var limit = timeout ?? Timeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(limit);
        try
        {
            using var request = createRequest();
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return ((int)response.StatusCode, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw WebSearchException.Timeout(providerName, (int)limit.TotalSeconds, ex);
        }
        catch (HttpRequestException ex)
        {
            throw WebSearchException.FromNetworkError(providerName, ex);
        }
    }

    /// <summary>
    /// Bereinigt die Treffer: nur http/https-Adressen ohne Benutzerdaten, ohne doppelte Adressen, Titel und Ausschnitt
    /// ohne Steuerzeichen und Zeilenumbrüche, begrenzte Länge, höchstens <paramref name="maxResults"/> Treffer.
    /// </summary>
    public static IReadOnlyList<WebSearchResult> Clean(IEnumerable<WebSearchResult> results, int maxResults)
    {
        var list = new List<WebSearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (list.Count >= maxResults) break;
            if (!ExternalUrl.TryParse(result.Url, out var uri)) continue;
            var url = uri.AbsoluteUri;
            if (!seen.Add(url)) continue;

            var snippet = Shorten(OneLine(result.Snippet), MaxSnippetLength);
            var title = Shorten(OneLine(result.Title), MaxTitleLength);
            var cleaned = new WebSearchResult(title, url, snippet, result.PublishedAt);
            if (title.Length == 0) cleaned = cleaned with { Title = cleaned.Domain };
            list.Add(cleaned);
        }
        return list;
    }

    /// <summary>Liest ein Datum tolerant (ISO 8601 bzw. RFC 1123); sonst null.</summary>
    public static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var date) ? date : null;
    }

    /// <summary>Steuerzeichen und Zeilenumbrüche zu Leerzeichen, mehrfache Leerzeichen zusammengefasst.</summary>
    public static string OneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c) || c is '﻿' or '​')
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                builder.Append(c);
                lastWasSpace = false;
            }
        }
        return builder.ToString().Trim();
    }

    private static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var cut = text.LastIndexOf(' ', maxLength - 1);
        var end = cut >= maxLength / 2 ? cut : maxLength - 1;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end].TrimEnd() + " …";
    }
}
