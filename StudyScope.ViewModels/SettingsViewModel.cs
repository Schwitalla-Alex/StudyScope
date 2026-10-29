using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels;

/// <summary>
/// Das SettingsViewModel steuert die profilbezogene KI-Konfiguration
/// (Auswahl zwischen Fake-KI, Ollama, LM Studio, Google Gemini, OpenAI GPT-4 und Custom).
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storageService;
    private readonly ILlmService _llmService;
    private readonly ILocalLlmDiscoveryService _discoveryService;

    private string _apiKey = string.Empty;
    private AiProviderType _selectedProvider = AiProviderType.FakeAi;
    private string _localEndpointUrl = "http://localhost:11434";
    private string _selectedModelName = string.Empty;
    private string _connectionStatusMessage = string.Empty;
    private bool _isSaving = false;
    private bool _isTestingConnection = false;
    private bool _connectionSuccess = false;

    public bool IsGeminiProvider => SelectedProvider == AiProviderType.GoogleGemini;

    public SettingsViewModel(
        IStorageService storageService,
        ILlmService llmService,
        ILocalLlmDiscoveryService discoveryService)
    {
        _storageService = storageService;
        _llmService = llmService;
        _discoveryService = discoveryService;
    }

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (_apiKey != value)
            {
                _apiKey = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSaveAndConnect));
            }
        }
    }

    public bool CanSaveAndConnect
    {
        get
        {
            if (IsSaving || IsTestingConnection)
                return false;

            return SelectedProvider switch
            {
                AiProviderType.FakeAi => true,

                // Lokale KIs: Server-Endpunkt muss da sein und ein gefundenes Modell ausgewählt sein
                AiProviderType.OllamaLocal or AiProviderType.LmStudioLocal =>
                    !string.IsNullOrWhiteSpace(LocalEndpointUrl) &&
                    !string.IsNullOrWhiteSpace(SelectedModelName) &&
                    AvailableModels.Contains(SelectedModelName),

                // Cloud-KIs: API-Schlüssel muss da sein und ein Online-Modell gewählt sein
                AiProviderType.GoogleGemini or AiProviderType.OpenAiGpt4 or AiProviderType.MistralAi =>
                    !string.IsNullOrWhiteSpace(ApiKey) &&
                    !string.IsNullOrWhiteSpace(SelectedModelName) &&
                    AvailableModels.Contains(SelectedModelName),

                // Custom: Endpunkt und Modellname reichen aus (Key ist optional)
                AiProviderType.CustomOpenAi =>
                    !string.IsNullOrWhiteSpace(LocalEndpointUrl) &&
                    !string.IsNullOrWhiteSpace(SelectedModelName),

                _ => false
            };
        }
    }

    public AiProviderType SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (_selectedProvider != value)
            {
                _selectedProvider = value;

                // Dynamisch geladene Modelle des vorherigen Providers verwerfen
                AvailableModels.Clear();
                ConnectionStatusMessage = string.Empty;

                // Sinnvolle Standardbelegungen je nach neu gewähltem Provider setzen
                switch (_selectedProvider)
                {
                    case AiProviderType.GoogleGemini:
                        SelectedModelName = "gemini-2.5-flash";
                        break;
                    case AiProviderType.OpenAiGpt4:
                        SelectedModelName = "gpt-4o-mini";
                        break;
                    case AiProviderType.MistralAi:
                        LocalEndpointUrl = "https://api.mistral.ai/v1";
                        SelectedModelName = "mistral-small-latest";
                        break;
                    case AiProviderType.OllamaLocal:
                        LocalEndpointUrl = "http://localhost:11434";
                        SelectedModelName = string.Empty;
                        break;
                    case AiProviderType.LmStudioLocal:
                        LocalEndpointUrl = "http://localhost:1234";
                        SelectedModelName = string.Empty;
                        break;
                    case AiProviderType.CustomOpenAi:
                        LocalEndpointUrl = string.Empty;
                        SelectedModelName = string.Empty;
                        break;
                    default:
                        SelectedModelName = string.Empty;
                        break;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedModelDisplayName));
                OnPropertyChanged(nameof(IsApiKeyRequired));
                OnPropertyChanged(nameof(IsLocalProvider));
                OnPropertyChanged(nameof(IsCustomProvider));
                OnPropertyChanged(nameof(IsGeminiProvider));
                OnPropertyChanged(nameof(SelectedModelName));
                OnPropertyChanged(nameof(LocalEndpointUrl));
                OnPropertyChanged(nameof(CanSaveAndConnect));
            }
        }
    }

    /// <summary>
    /// Setzt alle Felder des Dialogs zurück.
    /// </summary>
    public void ResetForm()
    {
        ApiKey = string.Empty;
        LocalEndpointUrl = string.Empty;
        SelectedModelName = string.Empty;
        AvailableModels.Clear();
        ConnectionStatusMessage = string.Empty;
        ConnectionSuccess = false;

        OnPropertyChanged(nameof(ApiKey));
        OnPropertyChanged(nameof(LocalEndpointUrl));
        OnPropertyChanged(nameof(SelectedModelName));
        OnPropertyChanged(nameof(ConnectionStatusMessage));
        OnPropertyChanged(nameof(ConnectionSuccess));
        OnPropertyChanged(nameof(CanSaveAndConnect));
    }

    public string LocalEndpointUrl
    {
        get => _localEndpointUrl;
        set
        {
            if (_localEndpointUrl != value)
            {
                _localEndpointUrl = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSaveAndConnect));
            }
        }
    }

    public string SelectedModelName
    {
        get => _selectedModelName;
        set
        {
            if (_selectedModelName != value)
            {
                _selectedModelName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSaveAndConnect));
            }
        }
    }

    public ObservableCollection<string> AvailableModels { get; } = new();

    public bool IsLocalProvider => SelectedProvider is AiProviderType.OllamaLocal or AiProviderType.LmStudioLocal or AiProviderType.CustomOpenAi;

    public bool IsApiKeyRequired => SelectedProvider is AiProviderType.GoogleGemini or AiProviderType.OpenAiGpt4 or AiProviderType.MistralAi or AiProviderType.CustomOpenAi;

    public bool IsCustomProvider => SelectedProvider == AiProviderType.CustomOpenAi;

    public string SelectedModelDisplayName => SelectedProvider switch
    {
        AiProviderType.FakeAi => "Fake-KI (Testmodus)",
        AiProviderType.OllamaLocal => "Ollama (Lokal)",
        AiProviderType.LmStudioLocal => "LM Studio (Lokal)",
        AiProviderType.OpenAiGpt4 => "OpenAI (GPT-4)",
        AiProviderType.GoogleGemini => "Google Gemini",
        AiProviderType.MistralAi => "Mistral AI",
        AiProviderType.CustomOpenAi => "Benutzerdefiniert",
        _ => "Unbekannt"
    };

    public string ConnectionStatusMessage
    {
        get => _connectionStatusMessage;
        private set
        {
            if (_connectionStatusMessage != value)
            {
                _connectionStatusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ConnectionSuccess
    {
        get => _connectionSuccess;
        private set
        {
            if (_connectionSuccess != value)
            {
                _connectionSuccess = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (_isSaving != value)
            {
                _isSaving = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSaveAndConnect));
            }
        }
    }

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set
        {
            if (_isTestingConnection != value)
            {
                _isTestingConnection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSaveAndConnect));
            }
        }
    }

    public async Task InitializeAsync()
    {
        // 1. Felder zurücksetzen
        ResetForm();

        // 2. Aktives Profil ermitteln
        var activeProfile = await _storageService.GetLastActiveProfileNameAsync() ?? "Standard";

        // 3. Profilspezifische Config laden
        var config = await _storageService.LoadAiConfigForProfileAsync(activeProfile);

        // 4. Wenn keine Konfiguration existiert oder kein Modell/Key hinterlegt ist: Maske bleibt leer
        if (config == null || (string.IsNullOrWhiteSpace(config.SelectedModelName) && string.IsNullOrWhiteSpace(config.ModelName) && string.IsNullOrWhiteSpace(config.ApiKey)))
        {
            return;
        }

        // 5. Daten des Profils übernehmen
        ApiKey = config.ApiKey ?? string.Empty;
        LocalEndpointUrl = string.IsNullOrWhiteSpace(config.LocalEndpointUrl)
            ? "http://localhost:11434"
            : config.LocalEndpointUrl;
        SelectedProvider = config.SelectedProvider;

        var modelName = !string.IsNullOrWhiteSpace(config.SelectedModelName)
            ? config.SelectedModelName
            : config.ModelName;

        SelectedModelName = modelName ?? string.Empty;

        // In AvailableModels eintragen, damit das Dropdown es sofort anzeigen und selektieren kann:
        if (!string.IsNullOrWhiteSpace(SelectedModelName) && !AvailableModels.Contains(SelectedModelName))
        {
            AvailableModels.Add(SelectedModelName);
        }

        ConnectionStatusMessage = string.Empty;

        // Wenn Gemini konfiguriert ist und ein Key vorliegt: Modelle abrufen, aber Auswahl behalten!
        if (SelectedProvider == AiProviderType.GoogleGemini && !string.IsNullOrWhiteSpace(ApiKey))
        {
            _ = TestConnectionAndFetchModelsAsync();
        }
    }

    public async Task<bool> SaveAndConnectAsync()
    {
        IsSaving = true;
        ConnectionStatusMessage = "Verbindung wird geprüft...";

        try
        {
            // 1. ZUERST Verbindung prüfen:
            var isConnected = await _llmService.ValidateConnectionAsync();
            if (!isConnected)
            {
                ConnectionSuccess = false;
                ConnectionStatusMessage = "Fehler: Verbindung fehlgeschlagen. Bitte Einstellungen prüfen.";
                return false;
            }

            // 2. ERST BEI ERFOLG in Profil und Fallback speichern:
            var config = new AiConfig
            {
                ApiKey = ApiKey?.Trim() ?? string.Empty,
                SelectedProvider = SelectedProvider,
                LocalEndpointUrl = LocalEndpointUrl?.Trim() ?? string.Empty,
                SelectedModelName = SelectedModelName?.Trim() ?? string.Empty,
                ModelName = SelectedModelName?.Trim() ?? string.Empty
            };

            var activeProfile = await _storageService.GetLastActiveProfileNameAsync() ?? "Standard";
            await _storageService.SaveAiConfigForProfileAsync(activeProfile, config);
            await _storageService.SaveAiConfigAsync(config);

            ConnectionSuccess = true;
            ConnectionStatusMessage = "Erfolgreich verbunden und gespeichert!";
            return true;
        }
        catch (Exception ex)
        {
            ConnectionSuccess = false;
            ConnectionStatusMessage = $"Verbindung fehlgeschlagen: {ex.Message}";
            return false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task TestConnectionAndFetchModelsAsync()
    {
        IsTestingConnection = true;
        ConnectionStatusMessage = string.Empty;

        // Vorherige Modellauswahl merken!
        var previousModel = SelectedModelName;

        AvailableModels.Clear();

        try
        {
            List<string> models = SelectedProvider switch
            {
                AiProviderType.OllamaLocal => await _discoveryService.FetchOllamaModelsAsync(LocalEndpointUrl),
                AiProviderType.LmStudioLocal => await _discoveryService.FetchLmStudioModelsAsync(LocalEndpointUrl),
                AiProviderType.GoogleGemini => await _discoveryService.FetchGeminiModelsAsync(ApiKey),
                AiProviderType.OpenAiGpt4 => await _discoveryService.FetchOpenAiModelsAsync(ApiKey),
                AiProviderType.MistralAi => await _discoveryService.FetchOpenAiModelsAsync(ApiKey, "https://api.mistral.ai/v1"),
                AiProviderType.CustomOpenAi => await _discoveryService.FetchOpenAiModelsAsync(ApiKey, LocalEndpointUrl),
                _ => new List<string>()
            };

            if (models.Any())
            {
                foreach (var m in models) AvailableModels.Add(m);

                // WICHTIG: Wenn das zuvor im Profil gespeicherte Modell in der Liste existiert, beibehalten!
                if (!string.IsNullOrWhiteSpace(previousModel) && AvailableModels.Contains(previousModel))
                {
                    SelectedModelName = previousModel;
                }
                else
                {
                    // Fallback: Erstes Modell der Liste vorwählen
                    SelectedModelName = AvailableModels.First();
                }

                ConnectionSuccess = true;
                ConnectionStatusMessage = $"✓ Verbunden! {models.Count} Modell(e) gefunden.";
            }
            else
            {
                ConnectionSuccess = false;
                ConnectionStatusMessage = "⚠️ Keine Modelle gefunden. Bitte API-Key bzw. Server-URL prüfen.";
            }
            OnPropertyChanged(nameof(CanSaveAndConnect));
        }
        catch (Exception ex)
        {
            ConnectionSuccess = false;
            ConnectionStatusMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
            OnPropertyChanged(nameof(IsTestingConnection));
            OnPropertyChanged(nameof(ConnectionStatusMessage));
            OnPropertyChanged(nameof(ConnectionSuccess));
            OnPropertyChanged(nameof(SelectedModelName));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}