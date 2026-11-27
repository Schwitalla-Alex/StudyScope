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
/// „Verbindung testen“ nutzt die ungespeicherten Formularwerte. „Speichern“ prüft vorher den Zugang, wenn Dienst,
/// Schlüssel oder Adresse gegenüber dem gespeicherten Stand geändert sind: abgelehnt → nicht gespeichert, nicht
/// erreichbar → nur über „Trotzdem speichern“, Kontingent erreicht → gespeichert mit Warnung.
/// </summary>
public class WebSearchSettingsViewModel : INotifyPropertyChanged
{
    /// <summary>Anmeldeseite für einen eigenen Tavily-Schlüssel.</summary>
    public const string TavilySignupUrl = "https://app.tavily.com";

    /// <summary>Konto bei Tavily (Schlüssel einsehen bzw. neu erzeugen) – angeboten, wenn der Schlüssel abgelehnt wurde.</summary>
    public const string TavilyAccountUrl = "https://app.tavily.com/home";

    /// <summary>Übliche Vorsilbe der Tavily-Schlüssel (nur Hinweis, die Prüfung entscheidet).</summary>
    public const string TavilyKeyPrefix = "tvly-";

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
            ResetVerificationOffers();
            if (!IsTavily) KeyFormatWarning = null;
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
    public string TavilyApiKey
    {
        get => _tavilyApiKey;
        set
        {
            var newValue = value ?? string.Empty;
            if (_tavilyApiKey == newValue) return;
            _tavilyApiKey = newValue;
            ResetVerificationOffers();
            // Die Warnung verschwindet sofort, sobald der Schlüssel passt; neu gesetzt wird sie erst beim Verlassen des Feldes
            if (KeyFormatWarning != null && FormatWarningFor(newValue) == null) KeyFormatWarning = null;
            OnPropertyChanged();
        }
    }

    private string _searxngUrl = string.Empty;
    public string SearxngUrl
    {
        get => _searxngUrl;
        set
        {
            var newValue = value ?? string.Empty;
            if (_searxngUrl == newValue) return;
            _searxngUrl = newValue;
            ResetVerificationOffers();
            OnPropertyChanged();
        }
    }

    private int _maxResults = WebSearchSettings.DefaultMaxResults;
    /// <summary>Treffer je Frage (1–10).</summary>
    public int MaxResults { get => _maxResults; set { _maxResults = WebSearchSettings.ClampMaxResults(value); OnPropertyChanged(); } }

    public IReadOnlyList<int> MaxResultsOptions { get; } =
        Enumerable.Range(WebSearchSettings.MinMaxResults, WebSearchSettings.MaxMaxResults - WebSearchSettings.MinMaxResults + 1).ToList();

    private bool _isTesting;
    public bool IsTesting { get => _isTesting; private set { _isTesting = value; OnPropertyChanged(); OnBusyChanged(); } }

    private bool _isVerifying;
    /// <summary>Die Zugangsprüfung beim Speichern läuft (Knöpfe gesperrt; Schließen bricht ab, nichts wird gespeichert).</summary>
    public bool IsVerifying { get => _isVerifying; private set { _isVerifying = value; OnPropertyChanged(); OnBusyChanged(); } }

    public bool IsBusy => IsTesting || IsVerifying;

    public bool CanTest => !IsBusy && Provider != WebSearchProviderType.None;

    public bool CanSave => !IsBusy;

    private bool _canSaveAnyway;
    /// <summary>Die Prüfung war nicht möglich (Netzwerk, Zeitüberschreitung, 5xx) – „Trotzdem speichern“ anbieten.</summary>
    public bool CanSaveAnyway { get => _canSaveAnyway; private set { _canSaveAnyway = value; OnPropertyChanged(); } }

    private bool _showTavilyAccountLink;
    /// <summary>Der Tavily-Schlüssel wurde abgelehnt – Knopf „Tavily-Konto öffnen“ anbieten.</summary>
    public bool ShowTavilyAccountLink { get => _showTavilyAccountLink; private set { _showTavilyAccountLink = value; OnPropertyChanged(); } }

    private string? _keyFormatWarning;
    /// <summary>Hinweis, wenn der Tavily-Schlüssel nicht mit „tvly-“ beginnt (ohne Netz, beim Verlassen des Feldes).</summary>
    public string? KeyFormatWarning { get => _keyFormatWarning; private set { _keyFormatWarning = value; OnPropertyChanged(); } }

    private bool _isWarning;
    /// <summary>Die Statusmeldung ist eine Warnung (gespeichert mit Einschränkung bzw. nicht prüfbar).</summary>
    public bool IsWarning { get => _isWarning; private set { _isWarning = value; OnPropertyChanged(); } }

    private bool _keepOpenAfterSave;
    /// <summary>Nach dem Speichern gibt es etwas zu lesen (Verbrauch, Warnung) – der Dialog bleibt offen.</summary>
    public bool KeepOpenAfterSave { get => _keepOpenAfterSave; private set { _keepOpenAfterSave = value; OnPropertyChanged(); } }

    private WebSearchSettings _saved = new();
    private CancellationTokenSource? _verifyCts;

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
        _saved = settings.Clone();

        Provider = settings.Provider;
        TavilyApiKey = settings.TavilyApiKey;
        SearxngUrl = settings.SearxngUrl;
        MaxResults = settings.MaxResults;
        StatusMessage = null;
        KeyFormatWarning = null;
        ResetVerificationOffers();
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

    /// <summary>
    /// Speichert die Formularwerte (global). Ohne Schlüssel bzw. mit ungültiger Adresse wird nicht gespeichert. Sind
    /// Dienst, Schlüssel oder Adresse geändert, wird vorher der Zugang geprüft (siehe Klassenbeschreibung); die
    /// Trefferzahl allein prüft nicht. Liefert true, wenn gespeichert wurde.
    /// </summary>
    public Task<bool> SaveAsync() => SaveCoreAsync(force: false);

    /// <summary>„Trotzdem speichern“ nach einer Prüfung, die nicht möglich war (nur dann angeboten).</summary>
    public Task<bool> SaveAnywayAsync() => CanSaveAnyway ? SaveCoreAsync(force: true) : Task.FromResult(false);

    /// <summary>Bricht eine laufende Zugangsprüfung ab (Schließen des Dialogs); es wird nichts gespeichert.</summary>
    public void CancelVerification()
    {
        try { _verifyCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task<bool> SaveCoreAsync(bool force)
    {
        if (IsBusy) return false;
        KeepOpenAfterSave = false;
        var settings = ToSettings();
        if (Validate(settings) is { } problem)
        {
            ShowMessage(problem, success: false);
            return false;
        }

        var keyWarning = settings.Provider == WebSearchProviderType.Tavily ? FormatWarningFor(settings.TavilyApiKey) : null;
        KeyFormatWarning = keyWarning;

        if (force)
            return await StoreAsync(settings, VmStrings.WebSearch_SavedUnverified, warning: true, keepOpen: true);

        if (!NeedsVerification(settings))
            return await StoreAsync(settings, VmStrings.WebSearch_Saved, warning: false, keepOpen: false);

        ResetVerificationOffers();
        using var cts = new CancellationTokenSource();
        _verifyCts = cts;
        IsVerifying = true;
        ShowMessage(Loc.Format(VmStrings.WebSearch_Verifying, ("provider", settings.ProviderName)), success: false, warning: false, progress: true);
        WebSearchVerification verification;
        try
        {
            verification = await _webSearch.VerifyAsync(settings, cts.Token)
                ?? new WebSearchVerification(WebSearchVerificationStatus.Unreachable);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = null;
            return false;
        }
        catch (Exception ex)
        {
            verification = new WebSearchVerification(WebSearchVerificationStatus.Unreachable,
                Loc.Format(VmStrings.WebSearch_TestFailed, ("error", ex.Message)));
        }
        finally
        {
            _verifyCts = null;
            IsVerifying = false;
        }
        if (cts.IsCancellationRequested)
        {
            StatusMessage = null;
            return false;
        }

        var error = verification.Message ?? string.Empty;
        switch (verification.Status)
        {
            case WebSearchVerificationStatus.Ok:
                var lines = new List<string> { Loc.Format(VmStrings.WebSearch_SavedVerified, ("provider", settings.ProviderName)) };
                if (verification.Usage is { } usage) lines.Add(UsageText(usage));
                if (verification.UsedCredit) lines.Add(VmStrings.WebSearch_CheckUsedCredit);
                if (keyWarning != null) lines.Add(keyWarning);
                return await StoreAsync(settings, string.Join(Environment.NewLine, lines), warning: false, keepOpen: lines.Count > 1);

            case WebSearchVerificationStatus.LimitReached:
                return await StoreAsync(settings, Loc.Format(VmStrings.WebSearch_SavedLimit, ("error", error)), warning: true, keepOpen: true);

            case WebSearchVerificationStatus.Rejected:
                ShowTavilyAccountLink = settings.Provider == WebSearchProviderType.Tavily
                    && verification.ErrorKind != WebSearchErrorKind.NotConfigured;
                ShowMessage(WithKeyWarning(Loc.Format(VmStrings.WebSearch_VerifyRejected, ("error", error)), keyWarning), success: false);
                return false;

            default:
                CanSaveAnyway = true;
                ShowMessage(WithKeyWarning(Loc.Format(VmStrings.WebSearch_VerifyUnreachable, ("error", error)), keyWarning), success: false, warning: true);
                return false;
        }
    }

    private async Task<bool> StoreAsync(WebSearchSettings settings, string message, bool warning, bool keepOpen)
    {
        try
        {
            await _storage.SaveWebSearchSettingsAsync(settings);
            _saved = settings.Clone();
            ResetVerificationOffers();
            ShowMessage(message, success: !warning, warning: warning);
            KeepOpenAfterSave = keepOpen;
            return true;
        }
        catch (Exception ex)
        {
            ShowMessage(Loc.Format(VmStrings.Common_SaveFailed, ("error", ex.Message)), success: false);
            return false;
        }
    }

    /// <summary>Prüfen nur, wenn Dienst, Schlüssel bzw. Adresse gegenüber dem gespeicherten Stand geändert sind.</summary>
    public bool NeedsVerification(WebSearchSettings settings) => settings.Provider switch
    {
        WebSearchProviderType.None => false,
        _ when settings.Provider != _saved.Provider => true,
        WebSearchProviderType.Tavily => !string.Equals(settings.TavilyApiKey, _saved.TavilyApiKey?.Trim(), StringComparison.Ordinal),
        WebSearchProviderType.Searxng => !string.Equals(settings.SearxngUrl, _saved.SearxngUrl?.Trim(), StringComparison.Ordinal),
        _ => true
    };

    /// <summary>Beim Verlassen des Schlüsselfeldes: Hinweis, wenn der Schlüssel nicht mit „tvly-“ beginnt.</summary>
    public void CheckKeyFormat() => KeyFormatWarning = IsTavily ? FormatWarningFor(TavilyApiKey) : null;

    /// <summary>Öffnet das Tavily-Konto im System-Browser (nach abgelehntem Schlüssel).</summary>
    public bool OpenTavilyAccount() => _linkOpener?.OpenUrl(TavilyAccountUrl) ?? false;

    /// <summary>Verbrauch in der Formatkultur der Oberfläche, z. B. „Diesen Monat verbraucht: 3 von 1.000 Credits.“</summary>
    public static string UsageText(WebSearchUsage usage) => usage.Limit is { } limit
        ? Loc.Format(VmStrings.WebSearch_Usage, ("used", usage.Used.ToString("N0")), ("limit", limit.ToString("N0")), ("limitCount", limit))
        : Loc.Format(VmStrings.WebSearch_UsageNoLimit, ("used", usage.Used.ToString("N0")), ("usedCount", usage.Used));

    private static string? FormatWarningFor(string? key)
    {
        var trimmed = key?.Trim() ?? string.Empty;
        return trimmed.Length == 0 || trimmed.StartsWith(TavilyKeyPrefix, StringComparison.Ordinal) ? null : VmStrings.WebSearch_KeyFormatWarning;
    }

    private static string WithKeyWarning(string message, string? keyWarning) =>
        keyWarning == null ? message : message + Environment.NewLine + keyWarning;

    private void ResetVerificationOffers()
    {
        CanSaveAnyway = false;
        ShowTavilyAccountLink = false;
    }

    private void OnBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanTest));
        OnPropertyChanged(nameof(CanSave));
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

    private void ShowMessage(string message, bool success, bool warning = false, bool progress = false)
    {
        IsSuccess = success;
        IsWarning = warning;
        IsProgress = progress;
        StatusMessage = message;
    }

    private bool _isProgress;
    /// <summary>Die Statusmeldung beschreibt eine laufende Prüfung („Zugang wird geprüft …“).</summary>
    public bool IsProgress { get => _isProgress; private set { _isProgress = value; OnPropertyChanged(); } }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
