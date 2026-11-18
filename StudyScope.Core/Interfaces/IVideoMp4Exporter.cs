using StudyScope.Core.Common;
using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// MP4-Export eines Videos: Folienbilder (1920 × 1080, wie in der App, Stichpunkte nacheinander, Überblendung) werden
/// gezeichnet und mit ffmpeg samt Gemini-Tonspur zu H.264/AAC kodiert; ohne Ton läuft das Video im festen Takt.
/// Der Zeichner (SkiaSharp) wird erst beim Export geladen – fehlt die native Bibliothek, ist nur der Export gesperrt.
/// </summary>
public interface IVideoMp4Exporter
{
    /// <summary>Ob der Export möglich ist (ffmpeg mit H.264/AAC und Folienzeichner), sonst mit deutschem Grund und Hinweis.</summary>
    Task<Mp4Availability> GetAvailabilityAsync(bool refresh = false, CancellationToken cancellationToken = default);

    /// <summary>Schätzung vor dem Export: Bilderzahl, Dauer, Dateigröße und Exportdauer.</summary>
    Mp4ExportEstimate Estimate(VideoDocument video, VideoTimeline timeline, bool withAudio);

    /// <summary>
    /// Exportiert nach <see cref="Mp4ExportRequest.TargetPath"/>: erst in eine temporäre Datei, dann umbenennen; temporäre
    /// Bilder werden immer gelöscht. Abbruch beendet ffmpeg und hinterlässt keine halbe Datei.
    /// </summary>
    Task<Mp4ExportResult> ExportAsync(Mp4ExportRequest request, IProgress<Mp4ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record Mp4Availability(bool IsAvailable, string Message, string? Hint)
{
    public static Mp4Availability Ready(string message) => new(true, message, null);
    public static Mp4Availability Unavailable(string message, string? hint) => new(false, message, hint);
}

/// <summary>Auftrag: Video, Zeitachse (Zeitmarken der Audiodatei bzw. geschätzt), Tonspur (null = ohne Ton), Ziel.</summary>
public sealed record Mp4ExportRequest(VideoDocument Video, VideoTimeline Timeline, string? AudioPath, string TargetPath);

public sealed record Mp4ExportProgress(double Fraction, string Text);

public sealed record Mp4ExportResult(string Path, long Bytes, double DurationSeconds, int FrameCount, bool HasAudio);

/// <summary>Schätzung vor dem Export.</summary>
public sealed record Mp4ExportEstimate(int FrameCount, double DurationSeconds, long FileBytes, double ExportSeconds)
{
    public double Megabytes => FileBytes / 1_000_000.0;

    /// <summary>„ca. 4,2 MB“.</summary>
    public string SizeText
    {
        get
        {
            var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            if (Megabytes < 0.1) return "unter 0,1 MB";
            return Megabytes < 10 ? $"ca. {Megabytes.ToString("0.0", de)} MB" : $"ca. {Megabytes.ToString("0", de)} MB";
        }
    }

    /// <summary>„unter 1 Min.“, „ca. 3 Min.“ (aufgerundet, damit der Export eher früher fertig ist als angekündigt).</summary>
    public string ExportTimeText => ExportSeconds < 45 ? "unter 1 Min." : $"ca. {Math.Max(1, (int)Math.Ceiling(ExportSeconds / 60))} Min.";
}
