using StudyScope.Core.Localization;
using StudyScope.Core.Resources;
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
        ? Loc.Format(CoreStrings.Ffmpeg_InstallLinux, ("command", LinuxInstall))
        : Loc.Format(CoreStrings.Ffmpeg_InstallWindows, ("command", WindowsInstall));

    public static string Purpose => CoreStrings.Ffmpeg_Purpose;
}
