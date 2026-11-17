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

/// <summary>Fortschritt beim Erzeugen einer Audiodatei (Teil x von y).</summary>
public sealed record SpeechSynthesisProgress(int CompletedParts, int TotalParts)
{
    public double Fraction => TotalParts <= 0 ? 0 : Math.Clamp(CompletedParts / (double)TotalParts, 0, 1);
}

/// <summary>Ergebnis einer erzeugten Audiodatei: Dauer und Startzeit je Segment (für Hervorhebung und Springen).</summary>
public sealed record AudioFileResult(double DurationSeconds, IReadOnlyList<double> SegmentStartSeconds, string CreatedWith);

/// <summary>Sprachausgabe, die eine Audiodatei erzeugt (Gemini TTS, später weitere).</summary>
public interface IAudioFileSpeechService : ITextToSpeechService
{
    /// <summary>
    /// Erzeugt die Audiodatei (WAV) für das ganze Skript. Die Datei entsteht erst am Ende vollständig (über eine
    /// temporäre Datei); bei Abbruch oder Fehler bleibt keine halbe Datei zurück und eine vorhandene Datei unverändert.
    /// Fehler kommen als <see cref="Common.LlmServiceException"/> mit deutscher Meldung samt Handlungsempfehlung.
    /// </summary>
    Task<AudioFileResult> SynthesizeToFileAsync(AudioSummary summary, SpeechContext context, string filePath,
        IProgress<SpeechSynthesisProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Prüft Schlüssel und Modell ohne kostenpflichtigen Aufruf; liefert eine Erfolgsmeldung, sonst Ausnahme.</summary>
    Task<string> TestConnectionAsync(SpeechContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Spielt eine Audiodatei ab (in der Oberfläche über ein HTML-Audio-Element). Die Position meldet der Player über
/// <see cref="PositionChanged"/>; so kann das ViewModel den gerade gesprochenen Abschnitt hervorheben.
/// </summary>
public interface IAudioFilePlayer
{
    /// <summary>Ob der Player einsatzbereit ist (Oberfläche geladen).</summary>
    bool IsReady { get; }

    /// <summary>Lädt die Datei (falls nicht schon geladen) und spielt ab der Position <paramref name="startSeconds"/>.</summary>
    Task PlayAsync(string filePath, double startSeconds, double rate);

    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task SetRateAsync(double rate);

    /// <summary>Position in Sekunden; <c>Ended</c> = Datei zu Ende gespielt.</summary>
    event Action<double, bool>? PositionChanged;
}
