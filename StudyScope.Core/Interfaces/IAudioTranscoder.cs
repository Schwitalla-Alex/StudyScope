namespace StudyScope.Core.Interfaces;

/// <summary>
/// Wandelt Audiodateien um (z. B. WAV → MP3 über ein vorhandenes ffmpeg), damit eingebettete Tonspuren in HTML-Exporten
/// kleiner werden. Optional: Ist kein Dienst registriert oder <see cref="IsMp3AvailableAsync"/> false, bleibt es bei WAV.
/// </summary>
public interface IAudioTranscoder
{
    /// <summary>Bitrate der MP3-Ausgabe (Sprache, mono).</summary>
    const int Mp3Kbps = 64;

    /// <summary>MP3-Umwandlung ist auf diesem System möglich (z. B. ffmpeg gefunden und lauffähig).</summary>
    Task<bool> IsMp3AvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Liest eine WAV-Datei und liefert sie als MP3 (<see cref="Mp3Kbps"/> kbit/s, mono).</summary>
    Task<byte[]> ConvertToMp3Async(string wavPath, CancellationToken cancellationToken = default);
}
