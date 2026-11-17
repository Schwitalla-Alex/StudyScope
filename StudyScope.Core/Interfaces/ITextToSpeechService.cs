using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>Ergebnis der Verfügbarkeitsprüfung einer Sprachausgabe.</summary>
public enum SpeechAvailabilityStatus
{
    /// <summary>Prüfung läuft noch (z. B. Oberfläche noch nicht geladen).</summary>
    Checking,

    Ready,

    /// <summary>Nutzbar, aber mit Einschränkung (z. B. keine deutsche Stimme installiert).</summary>
    ReadyWithWarning,

    Unavailable
}

/// <summary>Status samt deutschem Hinweistext (Grund und was zu tun ist).</summary>
public sealed record SpeechAvailability(SpeechAvailabilityStatus Status, string Message, string? Hint = null)
{
    public bool IsAvailable => Status is SpeechAvailabilityStatus.Ready or SpeechAvailabilityStatus.ReadyWithWarning;

    public static SpeechAvailability Checking(string message = "Sprachausgabe wird geprüft …") => new(SpeechAvailabilityStatus.Checking, message);
    public static SpeechAvailability Ready(string message) => new(SpeechAvailabilityStatus.Ready, message);
    public static SpeechAvailability Warning(string message, string? hint = null) => new(SpeechAvailabilityStatus.ReadyWithWarning, message, hint);
    public static SpeechAvailability Unavailable(string message, string? hint = null) => new(SpeechAvailabilityStatus.Unavailable, message, hint);
}

/// <summary>Eine auswählbare Stimme.</summary>
/// <param name="Id">Kennung (bei der Systemstimme die voiceURI).</param>
/// <param name="IsLocal">Läuft offline auf dem Rechner (bei Online-Stimmen geht der Text an den Hersteller).</param>
public sealed record SpeechVoice(string Id, string Name, string Language, bool IsLocal = true, bool IsDefault = false)
{
    public bool IsGerman => Language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Was zur Verfügbarkeitsprüfung gebraucht wird: Einstellungen der Sprachausgabe und Chat-KI des Profils.</summary>
public sealed record SpeechContext(SpeechSettings Settings, AiConfig? ChatConfig);

/// <summary>
/// Sprachausgabe für Audio-Beiträge. Mehrere Anbieter (Systemstimme, Gemini TTS, …); der Reiter wählt je Profil einen aus.
/// Die Verfügbarkeit wird VOR dem Abspielen/Erzeugen geprüft, damit Knöpfe deaktiviert und begründet werden können.
/// </summary>
public interface ITextToSpeechService
{
    SpeechProviderType Provider { get; }

    /// <summary>Anzeigename in der Auswahl, z. B. „Systemstimme (kostenlos, offline)“.</summary>
    string DisplayName { get; }

    /// <summary>Kann direkt abspielen (ohne Datei)?</summary>
    bool SupportsLivePlayback { get; }

    /// <summary>Kann eine Audiodatei erzeugen (Export)?</summary>
    bool SupportsFileExport { get; }

    Task<SpeechAvailability> GetAvailabilityAsync(SpeechContext context, CancellationToken cancellationToken = default);

    /// <summary>Verfügbare Stimmen (deutsche zuerst).</summary>
    Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Sprachausgabe, die Text direkt vorliest (Systemstimme); ein Segment nach dem anderen.</summary>
public interface ILiveSpeechService : ITextToSpeechService
{
    /// <summary>
    /// Spricht einen Text und kehrt zurück, wenn er zu Ende gesprochen ist (true) oder durch <see cref="StopAsync"/>
    /// bzw. ein neues <see cref="SpeakAsync"/> abgebrochen wurde (false). Fehler kommen als Ausnahme mit deutscher Meldung.
    /// </summary>
    /// <param name="voiceId">Kennung der Stimme; leer = Standardstimme (deutsch bevorzugt).</param>
    /// <param name="rate">Geschwindigkeit, 1 = normal.</param>
    Task<bool> SpeakAsync(string text, string voiceId, double rate, CancellationToken cancellationToken = default);

    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
}
