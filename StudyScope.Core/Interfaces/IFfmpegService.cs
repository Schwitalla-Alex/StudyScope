using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Zugriff auf ein vorhandenes ffmpeg (wird nicht mitgeliefert): Suche im PATH, unter Windows zusätzlich im
/// WinGet-Ordner, oder eigener Pfad aus <see cref="ExportSettings"/>. ffmpeg läuft als eigener Prozess; Abbrechen
/// beendet ihn.
/// </summary>
public interface IFfmpegService
{
    /// <summary>Status (zwischengespeichert; <paramref name="refresh"/> sucht und prüft neu).</summary>
    Task<FfmpegStatus> GetStatusAsync(bool refresh = false, CancellationToken cancellationToken = default);

    /// <summary>„ffmpeg suchen/testen“: prüft einen eingegebenen Pfad (leer = automatisch suchen), ohne zu speichern.</summary>
    Task<FfmpegStatus> TestAsync(string? customPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Führt ffmpeg mit den Argumenten aus (einzeln übergeben, nie als zusammengesetzter String). Fortschritt 0…1 aus
    /// <c>-progress</c> bezogen auf <paramref name="totalSeconds"/>. Fehler als <see cref="FfmpegException"/> mit deutscher
    /// Meldung (ffmpeg-Ausgabe in <see cref="FfmpegException.Detail"/>); Abbruch beendet den Prozess und wirft
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    Task RunAsync(IReadOnlyList<string> arguments, double totalSeconds, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Fehler beim Ausführen von ffmpeg: deutsche Meldung, technische Ausgabe in <see cref="Detail"/>.</summary>
public class FfmpegException : Exception
{
    public string? Detail { get; }

    public FfmpegException(string message, string? detail = null, Exception? inner = null) : base(message, inner)
    {
        Detail = detail;
    }
}
