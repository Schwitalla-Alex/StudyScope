namespace StudyScope.ViewModels;

/// <summary>
/// Sorgt dafür, dass immer nur eine Wiedergabe läuft: Audio-Player und Systemstimme gibt es nur einmal. Startet der
/// Audio-Reiter, wird ein laufendes Video beendet und umgekehrt.
/// </summary>
public sealed class PlaybackCoordinator
{
    public const string AudioParticipant = "audio";
    public const string VideoParticipant = "video";

    private readonly Dictionary<string, (Func<bool> IsPlaying, Func<Task> Stop)> _participants = new();

    /// <summary>Meldet einen Teilnehmer an (gleicher Name ersetzt die alte Anmeldung).</summary>
    public void Register(string name, Func<bool> isPlaying, Func<Task> stop) => _participants[name] = (isPlaying, stop);

    /// <summary>Beendet die Wiedergabe aller anderen Teilnehmer, die gerade spielen.</summary>
    public async Task StopOthersAsync(string name)
    {
        foreach (var (other, participant) in _participants.ToList())
        {
            if (other == name || !participant.IsPlaying()) continue;
            try { await participant.Stop(); }
            catch { /* Stopp ist immer erlaubt */ }
        }
    }
}
