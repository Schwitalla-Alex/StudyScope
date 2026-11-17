using System.ComponentModel;
using System.Runtime.CompilerServices;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels;

/// <summary>
/// Abschnitt „Sprachausgabe“ in den Einstellungen: eigener Gemini-Schlüssel (je Profil, verschlüsselt, Rückfall global),
/// Modellname und Stimmen für Gemini TTS sowie „Verbindung testen“ ohne Kosten. Die Wahl der Sprachausgabe selbst
/// (Systemstimme/Gemini), Systemstimmen und Tempo stehen im Audio-Reiter und bleiben beim Speichern erhalten.
/// </summary>
public class SpeechSettingsViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storage;
    private readonly IAudioFileSpeechService? _gemini;
    private AiConfig? _chatConfig;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Wird nach erfolgreichem Speichern ausgelöst (z. B. damit der Audio-Reiter neu prüft).</summary>
    public event Action? Saved;

    public SpeechSettingsViewModel(IStorageService storage, IEnumerable<ITextToSpeechService> speechServices)
    {
        _storage = storage;
        _gemini = speechServices.OfType<IAudioFileSpeechService>().FirstOrDefault(s => s.Provider == SpeechProviderType.GeminiTts);
    }

    private string _profileName = "Standard";
    public string ProfileName { get => _profileName; private set { _profileName = value; OnPropertyChanged(); } }

    private string _geminiApiKey = string.Empty;
    /// <summary>Eigener Schlüssel der Sprachausgabe (nur nötig, wenn die Chat-KI nicht Gemini mit Schlüssel ist).</summary>
    public string GeminiApiKey { get => _geminiApiKey; set { _geminiApiKey = value ?? string.Empty; ClearStatus(); OnPropertyChanged(); OnPropertyChanged(nameof(KeySourceText)); } }

    private string _geminiModel = GeminiTtsInfo.DefaultModel;
    public string GeminiModel { get => _geminiModel; set { _geminiModel = value ?? string.Empty; ClearStatus(); OnPropertyChanged(); } }

    private string _voicePrimary = GeminiTtsInfo.DefaultVoicePrimary;
    public string VoicePrimary { get => _voicePrimary; set { _voicePrimary = value; OnPropertyChanged(); } }

    private string _voiceSecondary = GeminiTtsInfo.DefaultVoiceSecondary;
    public string VoiceSecondary { get => _voiceSecondary; set { _voiceSecondary = value; OnPropertyChanged(); } }

    public IReadOnlyList<string> Voices => GeminiTtsInfo.Voices;

    public static string VoiceLabel(string voice) => $"{voice} ({(GeminiTtsInfo.IsFemaleVoice(voice) ? "weiblich" : "männlich")})";

    /// <summary>Die Chat-KI des Profils ist Gemini mit Schlüssel – dann wird dieser Schlüssel verwendet.</summary>
    public bool ChatProvidesGeminiKey => _chatConfig is { SelectedProvider: AiProviderType.GoogleGemini } c && !string.IsNullOrWhiteSpace(c.ApiKey);

    /// <summary>Welcher Schlüssel gilt (Anzeige unter dem Eingabefeld).</summary>
    public string KeySourceText =>
        ChatProvidesGeminiKey
            ? "Verwendet wird der Gemini-Schlüssel aus den KI-Einstellungen dieses Profils. Ein eigener Schlüssel ist nicht nötig."
            : string.IsNullOrWhiteSpace(GeminiApiKey)
                ? "Noch kein Schlüssel – Gemini TTS ist im Audio-Reiter ausgegraut. Einen Schlüssel gibt es kostenlos in Google AI Studio."
                : "Verwendet wird dieser eigene Schlüssel (verschlüsselt im Profil gespeichert). Die Chat-KI bleibt unverändert.";

    public string PrivacyHint => AudioViewModel.GeminiPrivacyHint;

    private string? _statusMessage;
    public string? StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    private bool _isSuccess;
    public bool IsSuccess { get => _isSuccess; private set { _isSuccess = value; OnPropertyChanged(); } }

    private bool _isTesting;
    public bool IsTesting { get => _isTesting; private set { _isTesting = value; OnPropertyChanged(); } }

    public bool CanTestConnection => _gemini != null && !IsTesting && (ChatProvidesGeminiKey || !string.IsNullOrWhiteSpace(GeminiApiKey));

    private void ClearStatus()
    {
        if (StatusMessage == null) return;
        StatusMessage = null;
    }

    public async Task InitializeAsync()
    {
        ProfileName = await _storage.GetLastActiveProfileNameAsync() ?? "Standard";
        var settings = await _storage.LoadSpeechSettingsAsync(ProfileName) ?? new SpeechSettings();
        _chatConfig = await _storage.LoadAiConfigForProfileAsync(ProfileName) ?? await _storage.LoadAiConfigAsync();

        _geminiApiKey = settings.GeminiApiKey ?? string.Empty;
        _geminiModel = string.IsNullOrWhiteSpace(settings.GeminiModel) ? GeminiTtsInfo.DefaultModel : settings.GeminiModel;
        _voicePrimary = settings.GeminiVoicePrimary;
        _voiceSecondary = settings.GeminiVoiceSecondary;
        StatusMessage = null;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Formularwerte in eine Kopie der gespeicherten Einstellungen übernehmen (Anbieter, Systemstimmen, Tempo bleiben).</summary>
    private SpeechSettings ApplyForm(SpeechSettings settings)
    {
        var result = settings.Clone();
        result.GeminiApiKey = GeminiApiKey.Trim();
        result.GeminiModel = string.IsNullOrWhiteSpace(GeminiModel) ? GeminiTtsInfo.DefaultModel : GeminiModel.Trim();
        result.GeminiVoicePrimary = VoicePrimary;
        result.GeminiVoiceSecondary = VoiceSecondary;
        return result;
    }

    public void ResetModel() => GeminiModel = GeminiTtsInfo.DefaultModel;

    public async Task<bool> SaveAsync()
    {
        try
        {
            var current = await _storage.LoadSpeechSettingsAsync(ProfileName) ?? new SpeechSettings();
            var updated = ApplyForm(current);
            await _storage.SaveSpeechSettingsAsync(ProfileName, updated);
            GeminiModel = updated.GeminiModel;
            IsSuccess = true;
            StatusMessage = $"✓ Sprachausgabe für das Profil „{ProfileName}“ gespeichert.";
            Saved?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"⚠️ Speichern fehlgeschlagen: {ex.Message}";
            return false;
        }
    }

    /// <summary>Prüft Schlüssel und Modell mit den Formularwerten (noch ungespeichert) – kostenlos über die Modellabfrage.</summary>
    public async Task TestConnectionAsync()
    {
        if (_gemini == null || IsTesting) return;
        IsTesting = true;
        OnPropertyChanged(nameof(CanTestConnection));
        try
        {
            var settings = ApplyForm(await _storage.LoadSpeechSettingsAsync(ProfileName) ?? new SpeechSettings());
            var message = await _gemini.TestConnectionAsync(new SpeechContext(settings, _chatConfig));
            IsSuccess = true;
            StatusMessage = message;
        }
        catch (LlmServiceException ex)
        {
            IsSuccess = false;
            StatusMessage = $"⚠️ {ex.Message}";
        }
        catch (Exception ex)
        {
            IsSuccess = false;
            StatusMessage = $"⚠️ Verbindungstest fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
            OnPropertyChanged(nameof(CanTestConnection));
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName is nameof(GeminiApiKey))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanTestConnection)));
    }
}
