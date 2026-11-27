using System.Globalization;
using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Websuche für den Chat (nur nach ausdrücklichem Einschalten je Profil). Gesucht wird ausschließlich mit einer
/// <see cref="WebSearchQuery"/> – also nur mit Text, den der Benutzer selbst eingegeben hat. Fehler (Schlüssel,
/// Kontingent, Netzwerk, Zeitüberschreitung, kein Dienst eingerichtet) kommen als
/// <see cref="Common.WebSearchException"/> mit verständlicher Meldung.
/// </summary>
public interface IWebSearchService
{
    /// <summary>Sucht mit dem eingerichteten Dienst und der gespeicherten Trefferzahl.</summary>
    /// <param name="language">Ergebnissprache; SearXNG und die Fake-Suche nutzen sie, Tavily kennt keinen Sprachparameter.</param>
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(WebSearchQuery query, CultureInfo language, CancellationToken cancellationToken = default);

    /// <summary>
    /// „Verbindung testen“ mit den übergebenen (noch nicht gespeicherten) Einstellungen: eine Suche nach
    /// <see cref="WebSearchQuery.ConnectionTestText"/> (bei Tavily 1 Credit). Liefert die Zahl der Treffer.
    /// </summary>
    Task<int> TestAsync(WebSearchSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Ein Suchdienst ist eingerichtet (bzw. die Fake-Suche für Prüfungen ist aktiv).</summary>
    Task<bool> IsConfiguredAsync();
}
