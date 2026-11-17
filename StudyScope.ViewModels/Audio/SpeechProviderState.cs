using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.ViewModels.Audio;

/// <summary>
/// Gemeinsame Sprachausgabe-Logik für die Reiter „Audio“ und „Video“: Einstellungen und Chat-KI des Profils laden,
/// Verfügbarkeit aller Anbieter prüfen, Anbieter wählen (nicht verfügbare sind gesperrt), Stimmen der Systemstimme je
/// Sprecherrolle zuordnen und Begründungen für deaktivierte Knöpfe liefern. Hält keine Oberfläche; Änderungen werden
/// über <see cref="Changed"/> gemeldet.
/// </summary>
public sealed class SpeechProviderState
{
    public const string LocalAiHint =
        "Das Skript schreibt Ihre lokale KI. Zum Vorlesen nutzen Sie die Systemstimme (offline) oder Gemini TTS (Audiodatei, Podcast mit zwei Stimmen).";

    public const string GeminiPrivacyHint =
        "Datenschutz: Bei Gemini TTS wird der Skripttext an Google gesendet. In der kostenlosen Stufe kann Google die Eingaben zur Verbesserung seiner Dienste verwenden.";

    public const string NoFileExportHint =
        "Mit der Systemstimme ist kein Dateiexport möglich – sie liest nur direkt vor. Für eine Audiodatei (WAV) wählen Sie Gemini TTS.";

    private readonly IStorageService _storage;
    private readonly IReadOnlyList<ITextToSpeechService> _services;
    private readonly Dictionary<SpeechProviderType, SpeechAvailability> _availability = new();
    private IReadOnlyList<SpeechVoice> _systemVoices = Array.Empty<SpeechVoice>();

    public SpeechProviderState(IStorageService storage, IEnumerable<ITextToSpeechService> services)
    {
        _storage = storage;
        _services = services.ToList();
    }

    /// <summary>Etwas an Einstellungen, Verfügbarkeit oder Stimmen hat sich geändert.</summary>
    public event Action? Changed;

    public string ProfileName { get; private set; } = string.Empty;
    public SpeechSettings Settings { get; private set; } = new();
    public AiConfig? ChatConfig { get; private set; }
    public SpeechContext Context => new(Settings, ChatConfig);

    /// <summary>Alle Anbieter (auch nicht verfügbare – sie werden ausgegraut angezeigt).</summary>
    public IReadOnlyList<ITextToSpeechService> Services => _services;

    public SpeechProviderType SelectedProvider => Settings.Provider;
    public ITextToSpeechService? CurrentService => _services.FirstOrDefault(s => s.Provider == SelectedProvider);

    public IReadOnlyList<SpeechVoice> SystemVoices => _systemVoices;

    /// <summary>Chat-KI des Profils ist lokal (Ollama/LM Studio).</summary>
    public bool IsLocalChatAi => ChatConfig?.SelectedProvider is AiProviderType.OllamaLocal or AiProviderType.LmStudioLocal;

    public bool ShowPrivacyHint => SelectedProvider == SpeechProviderType.GeminiTts;

    /// <summary>Lädt Einstellungen der Sprachausgabe und Chat-KI des Profils.</summary>
    public async Task LoadAsync(string profileName)
    {
        ProfileName = profileName;
        Settings = await _storage.LoadSpeechSettingsAsync(profileName) ?? new SpeechSettings();
        ChatConfig = await _storage.LoadAiConfigForProfileAsync(profileName) ?? await _storage.LoadAiConfigAsync();
        Changed?.Invoke();
    }

    /// <summary>Liest Einstellungen und Chat-KI neu (z. B. nach dem Schließen der Einstellungen); bisherige Werte bleiben, wenn nichts gespeichert ist.</summary>
    public async Task ReloadAsync()
    {
        Settings = await _storage.LoadSpeechSettingsAsync(ProfileName) ?? Settings;
        ChatConfig = await _storage.LoadAiConfigForProfileAsync(ProfileName) ?? await _storage.LoadAiConfigAsync();
        Changed?.Invoke();
    }

    public SpeechAvailability AvailabilityOf(SpeechProviderType provider) =>
        _availability.TryGetValue(provider, out var a)
            ? a
            : _services.Any(s => s.Provider == provider)
                ? SpeechAvailability.Checking()
                : SpeechAvailability.Unavailable("Diese Sprachausgabe ist in dieser Version nicht enthalten.");

    public SpeechAvailability CurrentAvailability => AvailabilityOf(SelectedProvider);

    /// <summary>Auswählbar ist ein Anbieter, der verfügbar ist oder noch geprüft wird (der aktuelle bleibt immer gewählt).</summary>
    public bool CanSelectProvider(SpeechProviderType provider) =>
        provider == SelectedProvider || AvailabilityOf(provider).Status != SpeechAvailabilityStatus.Unavailable;

    /// <summary>Prüft alle Anbieter (vor dem Abspielen/Erzeugen, damit Knöpfe vorab deaktiviert werden können).</summary>
    public async Task RefreshAvailabilityAsync()
    {
        var context = Context;
        foreach (var service in _services)
        {
            try
            {
                _availability[service.Provider] = await service.GetAvailabilityAsync(context);
                if (service is ILiveSpeechService && service.Provider == SpeechProviderType.SystemVoice)
                    _systemVoices = await service.GetVoicesAsync();
            }
            catch (Exception ex)
            {
                _availability[service.Provider] = SpeechAvailability.Unavailable($"Prüfung fehlgeschlagen: {ex.Message}");
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Wählt den Anbieter (nur auswählbare) und speichert; true = geändert.</summary>
    public async Task<bool> SetProviderAsync(SpeechProviderType provider)
    {
        if (provider == SelectedProvider || !CanSelectProvider(provider)) return false;
        Settings.Provider = provider;
        await SaveAsync();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Stimme der Systemstimme für Sprecher 1 (0) bzw. 2 (1); leer = automatisch.</summary>
    public async Task SetSystemVoiceAsync(int speakerIndex, string voiceId)
    {
        if (speakerIndex == 0) Settings.SystemVoicePrimary = voiceId ?? string.Empty;
        else Settings.SystemVoiceSecondary = voiceId ?? string.Empty;
        await SaveAsync();
        Changed?.Invoke();
    }

    /// <summary>Setzt das Tempo (begrenzt, auf 2 Stellen gerundet) und speichert; true = geändert.</summary>
    public async Task<bool> SetRateAsync(double rate)
    {
        var clamped = Math.Round(SpeechSettings.ClampRate(rate), 2);
        if (Math.Abs(clamped - Settings.Rate) < 0.001) return false;
        Settings.Rate = clamped;
        await SaveAsync();
        return true;
    }

    /// <summary>Speichert die Einstellungen der Sprachausgabe (Fehler als Ausnahme).</summary>
    public Task SaveAsync() => _storage.SaveSpeechSettingsAsync(ProfileName, Settings);

    /// <summary>
    /// Gewählte Stimme eines Sprechers (bei automatischer Wahl die tatsächlich verwendete): passend zur Rolle
    /// (Moderatorin/Sprecherin weiblich, Experte männlich), soweit installiert; Sprecher 2 möglichst andere Stimme.
    /// </summary>
    public string VoiceForSpeakerIndex(int speakerIndex, IReadOnlyList<string> roles)
    {
        var primary = SystemVoiceRules.ResolveVoice(SystemVoices, Settings.SystemVoicePrimary, 0, null,
            SystemVoiceRules.SpeakerGender(RoleName(roles, 0)));
        return speakerIndex == 0
            ? primary
            : SystemVoiceRules.ResolveVoice(SystemVoices, Settings.SystemVoiceSecondary, 1, primary,
                SystemVoiceRules.SpeakerGender(RoleName(roles, 1)));
    }

    public static string RoleName(IReadOnlyList<string> roles, int speakerIndex) =>
        roles.Count == 0 ? $"Sprecher {speakerIndex + 1}" : roles[Math.Clamp(speakerIndex, 0, roles.Count - 1)];

    /// <summary>
    /// Hinweis zur automatischen Stimme des zweiten Sprechers, wenn keine passende zweite deutsche Stimme installiert ist
    /// – samt Installationsweg. Bei manueller Auswahl, nur einem Sprecher oder anderem Anbieter kein Hinweis.
    /// </summary>
    public string? VoiceAssignmentHint(IReadOnlyList<string> roles)
    {
        if (SelectedProvider != SpeechProviderType.SystemVoice || roles.Count < 2 || SystemVoices.Count == 0)
            return null;
        if (!string.IsNullOrWhiteSpace(Settings.SystemVoiceSecondary) && SystemVoices.Any(v => v.Id == Settings.SystemVoiceSecondary))
            return null;

        var role = RoleName(roles, 1);
        return SystemVoiceRules.SecondVoiceHint(SystemVoices, VoiceForSpeakerIndex(0, roles), VoiceForSpeakerIndex(1, roles), role,
            SystemVoiceRules.SpeakerGender(role), OperatingSystem.IsLinux());
    }

    /// <summary>Begründung aus der Verfügbarkeit (null = nutzbar).</summary>
    public static string? AvailabilityReason(SpeechAvailability availability) => availability.Status switch
    {
        SpeechAvailabilityStatus.Checking => "Die Sprachausgabe wird noch geprüft …",
        SpeechAvailabilityStatus.Unavailable => string.IsNullOrWhiteSpace(availability.Hint) ? availability.Message : $"{availability.Message} {availability.Hint}",
        _ => null
    };

    /// <summary>Beendet jede Live-Wiedergabe (Stopp ist immer erlaubt).</summary>
    public async Task StopAllLiveAsync()
    {
        foreach (var live in _services.OfType<ILiveSpeechService>())
        {
            try { await live.StopAsync(); }
            catch { /* Stopp ist immer erlaubt */ }
        }
    }
}
