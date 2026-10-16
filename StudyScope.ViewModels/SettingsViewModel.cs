using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;


namespace StudyScope.ViewModels;

/// <summary>
/// Das SettingsViewModel, um das die KI-Konfiguration das aus dem PDF-Konzept steuert (Modell-Auswahl zwischen Testmodus,
/// OpenAI GPT-4, Google Gemini, Ollama, Eingabe des API-Keys und die Schaltfläche „Speichern & Verbinden“ inklusive Validierung).
/// </summary>
public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storageService;
    private readonly ILlmService _llmService;
    private readonly ILocalLlmDiscoveryService _discoveryService;

    private string _apiKey = string.Empty;
    private AiProviderType _selectedProvider = AiProviderType.FakeAi;
    private string _localEndpointUrl = "http://localhost:11434";
    private string _selectedModelName = "llama3.2:latest";
    private string _connectionStatusMessage = string.Empty;
    private bool _isSaving = false;
    private bool _isTestingConnection = false;
    private bool _connectionSuccess = false;

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
            }
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
                ApplyDefaultEndpoint();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedModelDisplayName));
                OnPropertyChanged(nameof(IsApiKeyRequired));
                OnPropertyChanged(nameof(IsLocalProvider));
            }
        }
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
            }
        }
    }

    public ObservableCollection<string> AvailableModels { get; } = new();

    public bool IsLocalProvider => SelectedProvider is AiProviderType.OllamaLocal or AiProviderType.LmStudioLocal or AiProviderType.CustomOpenAi;

    public bool IsApiKeyRequired => SelectedProvider is AiProviderType.GoogleGemini or AiProviderType.OpenAiGpt4 or AiProviderType.CustomOpenAi;

    public bool IsCustomProvider => SelectedProvider == AiProviderType.CustomOpenAi;

    public string SelectedModelDisplayName => SelectedProvider switch
    {
        AiProviderType.FakeAi => "Fake-KI (Testmodus)",
        AiProviderType.OllamaLocal => "Ollama (Lokal)",
        AiProviderType.LmStudioLocal => "LM Studio (Lokal)",
        AiProviderType.OpenAiGpt4 => "OpenAI (GPT-4)",
        AiProviderType.GoogleGemini => "Google Gemini",
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
            }
        }
    }

    public async Task InitializeAsync()
    {
        var config = await _storageService.LoadAiConfigAsync() ?? new AiConfig();
        ApiKey = config.ApiKey;
        SelectedProvider = config.SelectedProvider;
        LocalEndpointUrl = string.IsNullOrWhiteSpace(config.LocalEndpointUrl)
            ? "http://localhost:11434"
            : config.LocalEndpointUrl;
        SelectedModelName = string.IsNullOrWhiteSpace(config.SelectedModelName)
            ? config.ModelName
            : config.SelectedModelName;

        ConnectionStatusMessage = string.Empty;
    }

    private void ApplyDefaultEndpoint()
    {
        if (SelectedProvider == AiProviderType.OllamaLocal &&
            (string.IsNullOrWhiteSpace(LocalEndpointUrl) || LocalEndpointUrl.Contains("1234")))
        {
            LocalEndpointUrl = "http://localhost:11434";
        }
        else if (SelectedProvider == AiProviderType.LmStudioLocal &&
            (string.IsNullOrWhiteSpace(LocalEndpointUrl) || LocalEndpointUrl.Contains("11434")))
        {
            LocalEndpointUrl = "http://localhost:1234";
        }
    }

    public async Task TestConnectionAndFetchModelsAsync()
    {
        IsTestingConnection = true;
        ConnectionStatusMessage = "Suche nach lokalen Modellen...";
        AvailableModels.Clear();

        try
        {
            List<string> models = SelectedProvider switch
            {
                AiProviderType.OllamaLocal => await _discoveryService.FetchOllamaModelsAsync(LocalEndpointUrl),
                AiProviderType.LmStudioLocal => await _discoveryService.FetchLmStudioModelsAsync(LocalEndpointUrl),
                _ => new List<string>()
            };

            if (models.Any())
            {
                foreach (var m in models)
                {
                    AvailableModels.Add(m);
                }

                if (!AvailableModels.Contains(SelectedModelName))
                {
                    SelectedModelName = AvailableModels.First();
                }

                ConnectionSuccess = true;
                ConnectionStatusMessage = $"✓ Verbunden! {models.Count} Modell(e) gefunden.";
            }
            else
            {
                ConnectionSuccess = false;
                ConnectionStatusMessage = $"⚠️ Keine Modelle unter {LocalEndpointUrl} erreichbar. Läuft der Server?";
            }
        }
        catch (Exception ex)
        {
            ConnectionSuccess = false;
            ConnectionStatusMessage = $"Fehler: {ex.Message}";
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    public async Task<bool> SaveAndConnectAsync()
    {
        IsSaving = true;
        ConnectionStatusMessage = "Verbindung wird geprüft...";

        try
        {
            // 1. Zuerst Verbindung validieren
            var isConnected = await _llmService.ValidateConnectionAsync();
            if (!isConnected)
            {
                ConnectionSuccess = false;
                ConnectionStatusMessage = "Fehler: Verbindung fehlgeschlagen. Bitte Einstellungen prüfen.";
                return false;
            }

            // 2. Nur bei erfolgreicher Verbindung persistieren
            var config = new AiConfig
            {
                ApiKey = ApiKey.Trim(),
                SelectedProvider = SelectedProvider,
                LocalEndpointUrl = LocalEndpointUrl.Trim(),
                SelectedModelName = SelectedModelName,
                ModelName = SelectedModelName
            };

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}