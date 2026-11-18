using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Argumente für ffmpeg als Liste (je Eintrag ein Argument – Pfade mit Leerzeichen und Umlauten brauchen kein Quoting,
/// <c>ProcessStartInfo.ArgumentList</c> übergibt sie unverändert) sowie das Auswerten der ffmpeg-Ausgaben. Rein
/// rechnerisch und ohne ffmpeg testbar.
/// </summary>
public static partial class FfmpegArguments
{
    /// <summary>Bildrate des MP4-Exports.</summary>
    public const int Fps = 25;

    /// <summary>Bitrate der AAC-Tonspur im MP4 (Sprache, mono).</summary>
    public const int AacKbps = 96;

    /// <summary>Gemeinsamer Anfang: keine Eingabe von der Konsole, überschreiben, nur Fehler ausgeben, Fortschritt auf stdout.</summary>
    public static List<string> Common() => new()
    {
        "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-nostats", "-progress", "pipe:1"
    };

    /// <summary>WAV → MP3 (libmp3lame, mono, <paramref name="kbps"/> kbit/s). Ausgabeformat ausdrücklich, weil die Zieldatei temporär „.part“ heißt.</summary>
    public static List<string> Mp3(string input, string output, int kbps = Core.Interfaces.IAudioTranscoder.Mp3Kbps)
    {
        var args = Common();
        args.AddRange(new[] { "-i", input, "-vn", "-ac", "1", "-codec:a", "libmp3lame", "-b:a", $"{kbps}k", "-f", "mp3", output });
        return args;
    }

    /// <summary>
    /// Folienbilder (concat-Liste) plus optionale Tonspur → MP4 (H.264 yuv420p für alle Player, AAC, „faststart“ für
    /// sofortiges Abspielen). <c>-t</c> begrenzt auf die exakte Gesamtdauer.
    /// </summary>
    public static List<string> Mp4(string concatList, string? audio, string output, double totalSeconds)
    {
        var args = Common();
        args.AddRange(new[] { "-f", "concat", "-safe", "0", "-i", concatList });
        if (audio != null)
            args.AddRange(new[] { "-i", audio });
        args.AddRange(new[] { "-map", "0:v:0" });
        if (audio != null)
            args.AddRange(new[] { "-map", "1:a:0" });
        args.AddRange(new[]
        {
            "-vf", $"fps={Fps},format=yuv420p",
            "-c:v", "libx264", "-preset", "veryfast", "-tune", "stillimage", "-crf", "22"
        });
        args.AddRange(audio != null ? new[] { "-c:a", "aac", "-b:a", $"{AacKbps}k", "-ac", "1" } : new[] { "-an" });
        args.AddRange(new[] { "-t", Seconds(totalSeconds), "-movflags", "+faststart", "-f", "mp4", output });
        return args;
    }

    /// <summary>
    /// concat-Liste für ffmpeg: Bilder mit Anzeigedauer. Das letzte Bild steht zusätzlich ohne Dauer am Ende (sonst
    /// ignoriert der concat-Demuxer die Dauer des letzten Eintrags). Dateinamen relativ zur Liste und nur ASCII.
    /// </summary>
    public static string ConcatList(IReadOnlyList<(string FileName, double Seconds)> frames)
    {
        var sb = new StringBuilder("ffconcat version 1.0\n");
        foreach (var (file, seconds) in frames)
        {
            sb.Append("file '").Append(EscapeConcat(file)).Append("'\n");
            sb.Append("duration ").Append(Seconds(seconds)).Append('\n');
        }
        if (frames.Count > 0)
            sb.Append("file '").Append(EscapeConcat(frames[^1].FileName)).Append("'\n");
        return sb.ToString();
    }

    private static string EscapeConcat(string file) => file.Replace("'", "'\\''");

    /// <summary>Sekunden mit Punkt (unabhängig von der Kultur), 6 Nachkommastellen.</summary>
    public static string Seconds(double seconds) => Math.Max(0, seconds).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Position aus einer Zeile von <c>-progress</c>: <c>out_time_us</c> bzw. <c>out_time_ms</c> (trotz Namen
    /// Mikrosekunden) oder <c>out_time=HH:MM:SS.ffffff</c>; sonst null.
    /// </summary>
    public static double? ParseProgressSeconds(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var parts = line.Split('=', 2);
        if (parts.Length != 2) return null;
        var key = parts[0].Trim();
        var value = parts[1].Trim();
        if (key is "out_time_us" or "out_time_ms")
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) && us >= 0 ? us / 1_000_000.0 : null;
        if (key == "out_time" && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var span) && span >= TimeSpan.Zero)
            return span.TotalSeconds;
        return null;
    }

    /// <summary>„progress=end“ meldet das Ende.</summary>
    public static bool IsProgressEnd(string? line) => line?.Trim() == "progress=end";

    /// <summary>Version aus „ffmpeg version 9.0.2-full_build-www.gyan.dev Copyright …“.</summary>
    public static string? ParseVersion(string? output)
    {
        var match = VersionRegex().Match(output ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Ob ein Encoder in der Ausgabe von <c>ffmpeg -encoders</c> steht (Zeile „ A....D aac  …“).</summary>
    public static bool HasEncoder(string? encodersOutput, string name) =>
        Regex.IsMatch(encodersOutput ?? string.Empty, $@"^\s*[VAS][.A-Z]{{5}}\s+{Regex.Escape(name)}\s", RegexOptions.Multiline);

    [GeneratedRegex(@"ffmpeg version (\S+)")]
    private static partial Regex VersionRegex();
}
