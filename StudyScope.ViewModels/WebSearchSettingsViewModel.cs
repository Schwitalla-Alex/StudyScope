using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Core.Models;
using StudyScope.ViewModels.Resources;

namespace StudyScope.ViewModels;

/// <summary>
/// Einstellungen › „🌐 Websuche“: Suchdienst (Tavily/SearXNG), Schlüssel bzw. Adresse, Trefferzahl – global, für alle
/// Profile. Ob ein Profil im Internet sucht, entscheidet allein der Haken in der Seitenleiste (standardmäßig aus).
/// „Verbindung testen“ nutzt die ungespeicherten Formularwerte.
/// </summary>
public class WebSearchSettingsViewModel : INotifyPropertyChanged
{
    /// <summary>Anmeldeseite für einen eigenen Tavily-Schlüssel.</summary>
    public const string TavilySignupUrl = "https://app.tavily.com";

    private readonly IStorageService _storage;
    private readonly IWebSearchService _webSearch;
    private readonly IExternalLinkOpener? _linkOpener;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WebSearchSettingsViewModel(IStorageService storage, IWebSearchService webSearch, IExternalLinkOpener? linkOpener = null)
    {
        _storage = storage;
        _webSearch = webSearch;
        _linkOpener = linkOpener;
    }

    private WebSearchProviderType _provider = WebSearchProviderType.None;
    public WebSearchProviderType Provider
    {
        get => _provider;
        set
        {
            if (_provider == value) return;
            _provider = value;
            StatusMessage = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTavily));
            OnPropertyChanged(nameof(IsSearxng));
            OnPropertyChanged(nameof(CanTest));
        }
    }

    public bool IsTavily => Provider == WebSearchProviderType.Tavily;
    public bool IsSearxng => Provider == WebSearchProviderType.Searxng;

    /// <summary>Auswahl der Dienste mit Anzeigetext (als Eigenschaft, damit ein Sprachwechsel neu liest).</summary>
    public IReadOnlyList<(WebSearchProviderType Value, string Label)> ProviderOptions =>
    [
        (WebSearchProviderType.None, VmStrings.WebSearch_ProviderNone),
        (WebSearchProviderType.Tavily, WebSearchSettings.NameOf(WebSearchProviderType.Tavily)),
        (WebSearchProviderType.Searxng, WebSearchSettings.NameOf(WebSearchProviderType.Searxng))
    ];

    private string _tavilyApiKey = string.Empty;
    public string TavilyApiKey { get => _tavilyApiKey; set { _tavilyApiKey = value ?? string.Empty; OnPropertyChanged(); } }

    private string _searxngUrl = string.Empty;
    public string SearxngUrl { get => _searxngUrl; set { _searxngUrl = value ?? string.Empty; OnPropertyChanged(); } }

    private int _maxResults = WebSearchSettings.DefaultMaxResults;
    /// <summary>Treffer je Frage (1–10).</summary>
    public int MaxResults { get => _maxResults; set { _maxResults = WebSearchSettings.ClampMaxResults(value); OnPropertyChanged(); } }

    public IReadOnlyList<int> MaxResultsOptions { get; } =
        Enumerable.Range(WebSearchSettings.MinMaxResults, WebSearchSettings.MaxMaxResults - WebSearchSettings.MinMaxResults + 1).ToList();

    private bool _isTesting;
    public bool IsTesting { get => _isTesting; private set { _isTesting = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanTest)); } }

    public bool CanTest => !IsTesting && Provider != WebSearchProviderType.None;

    private string? _statusMessage;
    public string? StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    private bool _isSuccess;
    public bool IsSuccess { get => _isSuccess; private set { _isSuccess = value; OnPropertyChanged(); } }

    /// <summary>Beim Öffnen: gespeicherte Werte anzeigen.</summary>
    public async Task LoadAsync()
    {
        StatusMessage = null;
        WebSearchSettings settings;
        try { settings = await _storage.LoadWebSearchSettingsAsync(); }
        catch { settings = new WebSearchSettings(); }

        Provider = settings.Provider;
        TavilyApiKey = settings.TavilyApiKey;
        SearxngUrl = settings.SearxngUrl;
        MaxResults = settings.MaxResults;
        StatusMessage = null;
    }

    /// <summary>Die Formularwerte als Einstellungen (Schlüssel und Adresse ohne Leerraum).</summary>
    public WebSearchSettings ToSettings() => new()
    {
        Provider = Provider,
        TavilyApiKey = TavilyApiKey.Trim(),
        SearxngUrl = SearxngUrl.Trim(),
        MaxResults = MaxResults
    };

    /// <summary>
    /// „Verbindung testen“ mit den eingegebenen Werten (ohne zu speichern): eine Suche nach „StudyScope“,
    /// bei Tavily 1 Credit. Es wird keine Frage und kein Dokumentinhalt gesendet.
    /// </summary>
    public async Task TestAsync()
    {
        if (!CanTest) return;
        var settings = ToSettings();
        if (Validate(settings) is { } problem)
        {
            ShowMessage(problem, success: false);
            return;
        }

        IsTesting = true;
        StatusMessage = null;
        try
        {
            var count = await _webSearch.TestAsync(settings);
            ShowMessage(Loc.Format(VmStrings.WebSearch_TestOk, ("provider", settings.ProviderName), ("count", count)), success: true);
        }
        catch (WebSearchException ex)
        {
            ShowMessage(ex.UserMessage, success: false);
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.Format(VmStrings.WebSearch_TestFailed, ("error", ex.Message)), success: false);
        }
        finally
        {
            IsTesting = false;
        }
    }

    /// <summary>Speichert die Formularwerte (global). Ohne Schlüssel bzw. mit ungültiger Adresse wird nicht gespeichert.</summary>
    public async Task<bool> SaveAsync()
    {
        var settings = ToSettings();
        if (Validate(settings) is { } problem)
        {
            ShowMessage(problem, success: false);
            return false;
        }

        try
        {
            await _storage.SaveWebSearchSettingsAsync(settings);
            ShowMessage(VmStrings.WebSearch_Saved, success: true);
            return true;
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.Format(VmStrings.Common_SaveFailed, ("error", ex.Message)), success: false);
            return false;
        }
    }

    /// <summary>Öffnet die Anmeldeseite von Tavily im System-Browser.</summary>
    public bool OpenTavilySignup() => _linkOpener?.OpenUrl(TavilySignupUrl) ?? false;

    /// <summary>Fehlende Angabe für den gewählten Dienst, sonst null. „Kein Suchdienst“ ist immer gültig.</summary>
    public static string? Validate(WebSearchSettings settings) => settings.Provider switch
    {
        WebSearchProviderType.Tavily when string.IsNullOrWhiteSpace(settings.TavilyApiKey) => VmStrings.WebSearch_KeyMissing,
        WebSearchProviderType.Searxng when !WebSearchSettings.IsValidSearxngUrl(settings.SearxngUrl) => VmStrings.WebSearch_UrlInvalid,
        _ => null
    };

    private void ShowMessage(string message, bool success)
    {
        IsSuccess = success;
        StatusMessage = message;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
