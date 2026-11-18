using System.Text;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// MP4-Export: Bildplan (<see cref="VideoFramePlan"/>) → Folienbilder 1920 × 1080 (<see cref="SkiaSlideRenderer"/>,
/// erst hier geladen) in einem temporären Ordner → concat-Liste → ffmpeg (H.264/AAC) in eine temporäre Datei neben dem
/// Ziel → Umbenennen. Temporäre Bilder und halbe Dateien werden immer entfernt (auch bei Abbruch und Fehler).
/// </summary>
public class VideoMp4Exporter : IVideoMp4Exporter
{
    public const int FrameWidth = 1920;
    public const int FrameHeight = 1080;

    // Erfahrungswerte für die Schätzung (gemessen mit ffmpeg 9 / x264 „veryfast“, 4 Zeichen-Threads: ≈ 0,03 s je Bild);
    // bewusst eher großzügig. Größe: jedes Animationsbild ≈ 3,5 kB, Standbilder fast nichts, dazu ein Schlüsselbild
    // (≈ 70 kB) je 10 s und die AAC-Tonspur.
    public const double RenderSecondsPerImage = 0.05;
    public const double EncodeSecondsPerVideoSecond = 0.05;
    public const double StartupSeconds = 2;
    public const double BytesPerImage = 3500;
    public const double KeyframeBytesPer10Seconds = 70_000;

    private readonly IFfmpegService _ffmpeg;

    public VideoMp4Exporter(IFfmpegService ffmpeg)
    {
        _ffmpeg = ffmpeg;
    }

    public async Task<Mp4Availability> GetAvailabilityAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        var status = await _ffmpeg.GetStatusAsync(refresh, cancellationToken);
        if (!status.IsAvailable)
            return Mp4Availability.Unavailable(status.Message, status.Hint);
        if (!status.CanEncodeMp4)
            return Mp4Availability.Unavailable("Dieses ffmpeg kann kein MP4 mit H.264 und AAC schreiben.", status.Hint);

        var (ok, error) = await Task.Run(SlideRasterizerLoader.Probe, cancellationToken);
        if (!ok)
        {
            var hint = OperatingSystem.IsLinux()
                ? "Unter Linux braucht der MP4-Export die Bibliothek fontconfig: „sudo apt install libfontconfig1“, danach StudyScope neu starten."
                : "Bitte StudyScope neu installieren (die Datei libSkiaSharp fehlt oder ist beschädigt).";
            return Mp4Availability.Unavailable($"Die Grafikbibliothek für die Folienbilder (SkiaSharp) lässt sich nicht laden: {error}", hint);
        }
        return Mp4Availability.Ready($"Bereit – ffmpeg {status.Version}.");
    }

    /// <summary>Anzahl Stichpunkte je Szene (Folien ohne Stichpunkte bzw. Titelfolien: 0).</summary>
    public static IReadOnlyList<int> BulletCounts(VideoDocument video, int count) =>
        Enumerable.Range(0, count).Select(i => video.Slides[i].Kind == SlideKind.Title ? 0 : video.Slides[i].Bullets.Count).ToList();

    private static int SceneCount(VideoDocument video, VideoTimeline timeline) =>
        Math.Min(Math.Min(video.Slides.Count, video.Scenes.Count), timeline.Count);

    public Mp4ExportEstimate Estimate(VideoDocument video, VideoTimeline timeline, bool withAudio)
    {
        var count = SceneCount(video, timeline);
        var frames = count == 0 ? 0 : VideoFramePlan.Build(timeline, BulletCounts(video, count)).Count;
        var audio = withAudio ? timeline.Total * FfmpegArguments.AacKbps * 1000 / 8 : 0;
        var bytes = (long)(frames * BytesPerImage + Math.Ceiling(timeline.Total / 10) * KeyframeBytesPer10Seconds + audio + 20_000);
        var seconds = StartupSeconds + frames * RenderSecondsPerImage + timeline.Total * EncodeSecondsPerVideoSecond;
        return new Mp4ExportEstimate(frames, timeline.Total, bytes, seconds);
    }

    public async Task<Mp4ExportResult> ExportAsync(Mp4ExportRequest request, IProgress<Mp4ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var availability = await GetAvailabilityAsync(cancellationToken: cancellationToken);
        if (!availability.IsAvailable)
            throw new FfmpegException($"{availability.Message} {availability.Hint}");

        var video = request.Video;
        var timeline = request.Timeline;
        var count = SceneCount(video, timeline);
        if (count == 0) throw new InvalidOperationException("Das Video hat keine Folien.");

        var plan = VideoFramePlan.Build(timeline, BulletCounts(video, count));
        var renderShare = Math.Clamp(plan.Count * RenderSecondsPerImage / (plan.Count * RenderSecondsPerImage + timeline.Total * EncodeSecondsPerVideoSecond + 1), 0.2, 0.9);

        var tempDir = Path.Combine(Path.GetTempPath(), "StudyScope_mp4_" + Guid.NewGuid().ToString("N"));
        var targetDir = Path.GetDirectoryName(Path.GetFullPath(request.TargetPath)) ?? ".";
        var partFile = Path.Combine(targetDir, "." + Path.GetFileName(request.TargetPath) + ".part");
        Directory.CreateDirectory(tempDir);
        try
        {
            // 1. Folienbilder (parallel, je Arbeiter ein eigener Zeichner samt Layouts)
            var done = 0;
            progress?.Report(new Mp4ExportProgress(0, $"Folienbilder werden gezeichnet (0 von {plan.Count}) …"));
            var workers = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            await Task.Run(() => Parallel.ForEach(
                Partition(plan.Count, workers),
                new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken },
                range =>
                {
                    using var renderer = SlideRasterizerLoader.Create();
                    var layouts = new Dictionary<int, SlideFrameLayout>();
                    SlideFrameLayout LayoutOf(int i) => layouts.TryGetValue(i, out var l)
                        ? l
                        : layouts[i] = SlideFrameLayout.Compute(video.Slides[i], i + 1, video.Slides.Count, video.PresentationTitle, renderer, FrameWidth, FrameHeight);
                    for (var i = range.Start; i < range.End; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        using var bitmap = renderer.Render(plan[i].State, LayoutOf, FrameWidth, FrameHeight);
                        SkiaSlideRenderer.SavePng(bitmap, Path.Combine(tempDir, FrameName(i)));
                        var n = Interlocked.Increment(ref done);
                        if (n % 5 == 0 || n == plan.Count)
                            progress?.Report(new Mp4ExportProgress(renderShare * n / plan.Count, $"Folienbilder werden gezeichnet ({n} von {plan.Count}) …"));
                    }
                }), cancellationToken);

            // 2. concat-Liste (relative, reine ASCII-Namen) und ffmpeg
            var list = Path.Combine(tempDir, "frames.ffconcat");
            await File.WriteAllTextAsync(list, FfmpegArguments.ConcatList(plan.Select((s, i) => (FrameName(i), s.Seconds)).ToList()),
                new UTF8Encoding(false), cancellationToken);

            progress?.Report(new Mp4ExportProgress(renderShare, "Video wird kodiert …"));
            var encodeProgress = new Progress<double>(p =>
                progress?.Report(new Mp4ExportProgress(renderShare + (1 - renderShare) * p, $"Video wird kodiert ({(int)(p * 100)} %) …")));
            await _ffmpeg.RunAsync(FfmpegArguments.Mp4(list, request.AudioPath, partFile, timeline.Total), timeline.Total,
                encodeProgress, cancellationToken);

            File.Move(partFile, request.TargetPath, overwrite: true);
            progress?.Report(new Mp4ExportProgress(1, "Fertig."));
            return new Mp4ExportResult(request.TargetPath, new FileInfo(request.TargetPath).Length, timeline.Total, plan.Count, request.AudioPath != null);
        }
        finally
        {
            FfmpegAudioTranscoder.TryDelete(partFile);
            TryDeleteDirectory(tempDir);
        }
    }

    public static string FrameName(int index) => $"f{index:00000}.png";

    private static IEnumerable<(int Start, int End)> Partition(int count, int parts)
    {
        // Verschränkt wäre ausgewogener, zusammenhängende Bereiche halten aber die Layouts je Arbeiter klein
        var size = (int)Math.Ceiling(count / (double)Math.Max(1, parts * 4));
        for (var start = 0; start < count; start += size)
            yield return (start, Math.Min(count, start + size));
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(200); // ffmpeg gibt Dateien nach dem Beenden kurz verzögert frei
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
        }
    }
}
