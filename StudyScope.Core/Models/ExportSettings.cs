namespace StudyScope.Core.Models;

/// <summary>
/// Globale Export-Einstellungen (gehören zum Rechner, nicht zum Profil) als Eigenschaft <c>ExportSettings</c> in
/// <c>config.json</c>.
/// </summary>
public class ExportSettings
{
    /// <summary>Eigener Pfad zu ffmpeg (Programmdatei oder Ordner); leer = automatisch suchen.</summary>
    public string FfmpegPath { get; set; } = string.Empty;

    public ExportSettings Clone() => new() { FfmpegPath = FfmpegPath };
}

/// <summary>Ergebnis der ffmpeg-Suche bzw. -Prüfung.</summary>
public sealed record FfmpegStatus(
    bool IsAvailable,
    string? ExecutablePath,
    string? Version,
    bool HasH264,
    bool HasAac,
    bool HasMp3,
    string Message,
    string? Hint)
{
    /// <summary>MP4-Export möglich (H.264 und AAC vorhanden).</summary>
    public bool CanEncodeMp4 => IsAvailable && HasH264 && HasAac;

    /// <summary>MP3-Export möglich (libmp3lame vorhanden).</summary>
    public bool CanEncodeMp3 => IsAvailable && HasMp3;

    public static FfmpegStatus NotFound(string message, string hint) => new(false, null, null, false, false, false, message, hint);
}

/// <summary>Texte rund um ffmpeg (optionale Voraussetzung für MP4- und MP3-Export, wird nicht mitgeliefert).</summary>
public static class FfmpegInfo
{
    public const string WindowsInstall = "winget install Gyan.FFmpeg";
    public const string LinuxInstall = "sudo apt install ffmpeg";

    /// <summary>Installationshinweis für das laufende Betriebssystem.</summary>
    public static string InstallHint(bool isLinux) => isLinux
        ? $"ffmpeg installieren: im Terminal „{LinuxInstall}“ (Fedora: „sudo dnf install ffmpeg“), danach „ffmpeg suchen/testen“ in Einstellungen › Export."
        : $"ffmpeg installieren: in der Eingabeaufforderung „{WindowsInstall}“, danach StudyScope neu starten oder „ffmpeg suchen/testen“ in Einstellungen › Export. Alternativ den Pfad zur ffmpeg.exe dort eintragen.";

    public const string Purpose =
        "ffmpeg ist ein kostenloses Programm zum Umwandeln von Audio und Video. StudyScope liefert es nicht mit, nutzt es aber, wenn es installiert ist: für den MP4-Export der Videos und den MP3-Export der Audio-Beiträge (auch als kleinere Tonspur in HTML-Exporten).";
}
