using StudyScope.Core.Interfaces;
using StudyScope.Core.Localization;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>MP3-Umwandlung über ffmpeg (libmp3lame, 64 kbit/s mono) – für MP3-Export und kleinere HTML-Exporte.</summary>
public class FfmpegAudioTranscoder : IAudioTranscoder
{
    private readonly IFfmpegService _ffmpeg;

    public FfmpegAudioTranscoder(IFfmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    public async Task<bool> IsMp3AvailableAsync(CancellationToken cancellationToken = default) =>
        (await _ffmpeg.GetStatusAsync(cancellationToken: cancellationToken)).CanEncodeMp3;

    public async Task<string?> Mp3UnavailableReasonAsync(CancellationToken cancellationToken = default)
    {
        var status = await _ffmpeg.GetStatusAsync(cancellationToken: cancellationToken);
        if (!status.IsAvailable) return $"{status.Message} {status.Hint}";
        return status.HasMp3 ? null : Loc.Format(InfraStrings.Ffmpeg_NoMp3, ("hint", status.Hint ?? ""));
    }

    public async Task<byte[]> ConvertToMp3Async(string wavPath, CancellationToken cancellationToken = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"StudyScope_{Guid.NewGuid():N}.mp3");
        try
        {
            await _ffmpeg.RunAsync(FfmpegArguments.Mp3(wavPath, temp), 0, null, cancellationToken);
            return await File.ReadAllBytesAsync(temp, cancellationToken);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    public async Task ConvertToMp3FileAsync(string wavPath, string mp3Path, double durationSeconds, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Temporär im Zielordner (gleiches Laufwerk → Umbenennen ist atomar), dann verschieben
        var temp = mp3Path + ".part";
        try
        {
            await _ffmpeg.RunAsync(FfmpegArguments.Mp3(wavPath, temp), durationSeconds, progress, cancellationToken);
            File.Move(temp, mp3Path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Aufräumen darf den eigentlichen Fehler nicht verdecken
        }
    }
}
