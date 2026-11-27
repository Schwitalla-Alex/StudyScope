using System.Globalization;
using System.Net.Http;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Einzige <see cref="IWebSearchService"/>-Implementierung im Betrieb: lädt bei jedem Aufruf die gespeicherten
/// Einstellungen und wählt den Suchdienst. Mit <see cref="FakeEnvironmentVariable"/> = 1 (nur für Prüfungen) liefert
/// immer die Fake-Suche feste Treffer ohne Netzwerkzugriff – die App tut sonst nie so, als hätte sie gesucht.
/// </summary>
public sealed class WebSearchDispatcher : IWebSearchService
{
    /// <summary>Umgebungsvariable für Oberflächenprüfungen: „1“ = Fake-Suche statt eines echten Dienstes.</summary>
    public const string FakeEnvironmentVariable = "STUDYSCOPE_FAKE_WEBSEARCH";

    private readonly IStorageService _storage;
    private readonly IWebSearchProvider _tavily;
    private readonly IWebSearchProvider _searxng;
    private readonly IWebSearchProvider _fake = new FakeWebSearchService();
    private readonly Func<string, string?> _environment;

    public WebSearchDispatcher(IStorageService storage, HttpClient http, Func<string, string?>? environment = null)
        : this(storage, new TavilySearchService(http), new SearxngSearchService(http), environment)
    {
    }

    /// <summary>Für Tests: eigene Dienste und Umgebung.</summary>
    public WebSearchDispatcher(IStorageService storage, IWebSearchProvider tavily, IWebSearchProvider searxng, Func<string, string?>? environment = null)
    {
        _storage = storage;
        _tavily = tavily;
        _searxng = searxng;
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>Fake-Suche aktiv (nur über die Umgebungsvariable, nie über die Einstellungen).</summary>
    public bool IsFakeMode => _environment(FakeEnvironmentVariable) == "1";

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(WebSearchQuery query, CultureInfo language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var settings = await _storage.LoadWebSearchSettingsAsync();
        var maxResults = WebSearchSettings.ClampMaxResults(settings.MaxResults);
        if (IsFakeMode)
            return await _fake.SearchAsync(settings, query, language, maxResults, cancellationToken);

        return await ProviderFor(settings).SearchAsync(settings, query, language, maxResults, cancellationToken);
    }

    public async Task<int> TestAsync(WebSearchSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Fester Suchbegriff, keine Benutzerdaten; Trefferzahl wie eingestellt (bei Tavily trotzdem 1 Credit)
        var results = await ProviderFor(settings).SearchAsync(settings, WebSearchQuery.ConnectionTest(), CultureInfo.CurrentUICulture,
            WebSearchSettings.ClampMaxResults(settings.MaxResults), cancellationToken);
        return results.Count;
    }

    public async Task<bool> IsConfiguredAsync() =>
        IsFakeMode || (await _storage.LoadWebSearchSettingsAsync()).IsConfigured;

    private IWebSearchProvider ProviderFor(WebSearchSettings settings)
    {
        if (!settings.IsConfigured)
            throw WebSearchException.NotConfigured();

        return settings.Provider switch
        {
            WebSearchProviderType.Tavily => _tavily,
            WebSearchProviderType.Searxng => _searxng,
            _ => throw WebSearchException.NotConfigured()
        };
    }
}
