using System.Net;
using System.Text;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Core.Localization;
using StudyScope.Infrastructure.Resources;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// HTML-Export eines Videos: eine eigenständige Seite (offline, ohne externe Dateien) mit dem gemeinsamen
/// <see cref="VideoPlayerHtml"/> und den Sprechtexten zum Aufklappen. Außerdem die gemeinsamen Hilfen für Player-Daten
/// (Video-Reiter und Bericht): Ton nur bei aktueller Gemini-Audiodatei, Zeitachse aus deren Zeitmarken, sonst fester
/// Takt der geschätzten Sprechdauer; Größenschätzung vor dem Export; MP3 über <see cref="IAudioTranscoder"/>, falls verfügbar.
/// </summary>
public static class VideoHtmlExporter
{
    /// <summary>
    /// Die zusammengesetzte Audiodatei passt zum aktuellen Stand: vorhanden, nicht veraltet, mit Zeitmarke je Szene.
    /// </summary>
    public static bool HasCurrentAudio(VideoDocument video, string? audioPath) =>
        video.HasAudioFile && !video.IsAudioOutdated && audioPath != null && File.Exists(audioPath)
        && video.AudioDurationSeconds > 0 && video.AudioSceneStarts.Count == video.Scenes.Count;

    /// <summary>Grund für fehlenden Ton (für den Hinweis im Player und vor dem Export); null = Ton vorhanden.</summary>
    public static string? NoAudioReason(VideoDocument video, string? audioPath)
    {
        if (HasCurrentAudio(video, audioPath)) return null;
        var minutes = MediaFormat.Minutes(TimelineFor(video, withAudio: false).Total / 60);
        return Loc.Format(video.HasAudioFile && video.IsAudioOutdated ? InfraStrings.Video_NoAudioOutdated : InfraStrings.Video_NoAudioMissing,
            ("minutes", minutes));
    }

    /// <summary>Zeitachse: exakte Zeitmarken der Audiodatei bzw. geschätzte Dauern bei Tempo 1.</summary>
    public static VideoTimeline TimelineFor(VideoDocument video, bool withAudio) =>
        withAudio
            ? VideoTimeline.FromStarts(video.AudioSceneStarts, video.AudioDurationSeconds, video.Scenes.Count)
            : VideoTimeline.FromEstimates(video.Scenes, 1.0);

    /// <summary>Größe der eingebetteten Tonspur (base64): WAV aus der Dateigröße, MP3 aus Dauer und Bitrate.</summary>
    public static long EstimateAudioBytes(VideoDocument video, string? audioPath, bool mp3)
    {
        if (!HasCurrentAudio(video, audioPath)) return 0;
        var raw = mp3
            ? (long)Math.Ceiling(video.AudioDurationSeconds * IAudioTranscoder.Mp3Kbps * 1000 / 8)
            : new FileInfo(audioPath!).Length;
        return VideoPlayerHtml.Base64Length(raw);
    }

    /// <summary>Ungefähre Größe eines Players (Folien, Daten und Ton).</summary>
    public static long EstimatePlayerBytes(VideoDocument video, string? audioPath, bool mp3) =>
        video.Slides.Count * VideoPlayerHtml.BytesPerSlide
        + video.Scenes.Sum(s => (long)Encoding.UTF8.GetByteCount(s.Narration ?? string.Empty))
        + EstimateAudioBytes(video, audioPath, mp3);

    /// <summary>Größenschätzung für mehrere Videos (Bericht) bzw. eines (Video-Export) samt Seitenrahmen.</summary>
    public static VideoHtmlSizeEstimate Estimate(IReadOnlyList<(VideoDocument Video, string? AudioPath)> videos, bool mp3)
    {
        long audio = 0, total = VideoPlayerHtml.BaseOverheadBytes;
        var withAudio = 0;
        foreach (var (video, path) in videos)
        {
            var a = EstimateAudioBytes(video, path, mp3);
            if (a > 0) withAudio++;
            audio += a;
            total += EstimatePlayerBytes(video, path, mp3);
        }
        return new VideoHtmlSizeEstimate(total, audio, videos.Count, withAudio, mp3 && withAudio > 0);
    }

    /// <summary>
    /// Stellt die Player-Daten zusammen: mit Ton (WAV bzw. MP3, falls <paramref name="mp3"/> gesetzt und verfügbar) bei
    /// aktueller Audiodatei, sonst stumm im festen Takt mit Hinweis.
    /// </summary>
    public static async Task<VideoPlayerData> BuildPlayerDataAsync(VideoDocument video, string? audioPath, IAudioTranscoder? mp3,
        CancellationToken cancellationToken = default)
    {
        if (!HasCurrentAudio(video, audioPath))
        {
            return new VideoPlayerData
            {
                Video = video,
                Timeline = TimelineFor(video, withAudio: false),
                NoAudioReason = NoAudioReason(video, audioPath)
            };
        }

        byte[] audio;
        var mime = "audio/wav";
        if (mp3 != null)
        {
            audio = await mp3.ConvertToMp3Async(audioPath!, cancellationToken);
            mime = "audio/mpeg";
        }
        else
        {
            audio = await File.ReadAllBytesAsync(audioPath!, cancellationToken);
        }

        return new VideoPlayerData { Video = video, Timeline = TimelineFor(video, withAudio: true), Audio = audio, AudioMimeType = mime };
    }

    /// <summary>Eigenständige HTML-Seite eines Videos.</summary>
    public static string Export(VideoPlayerData data)
    {
        var video = data.Video;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"de\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append($"<meta name=\"generator\" content=\"StudyScope\">\n<title>{H(video.Title)}</title>\n");
        sb.Append("<style>\n").Append(PageCss).Append('\n').Append(VideoPlayerHtml.Css).Append("\n</style>\n</head>\n<body>\n<main class=\"page\">\n");

        sb.Append($"<header><div class=\"badge\">🎬 Video</div><h1>{H(video.Title)}</h1>");
        sb.Append($"<div class=\"meta\">{H(MetaLine(data))}</div></header>\n");

        VideoPlayerHtml.AppendPlayer(sb, data);

        sb.Append("<div class=\"keys\">Tastatur (nach Klick in den Player): Leertaste Play/Pause · ← → Folie zurück/vor · Pos1/Ende · F Vollbild</div>\n");

        var scenes = video.Scenes.Select((s, i) => (Scene: s, Index: i)).Where(x => x.Scene.HasNarration).ToList();
        if (scenes.Count > 0)
        {
            sb.Append("<details class=\"script\"><summary>📝 Sprechtexte je Folie</summary><div class=\"script-body\">");
            foreach (var (scene, index) in scenes)
                sb.Append($"<p><b>{H(ReportVideoScripts.SceneHeading(video, index))}</b><br>{H(scene.Narration.Trim()).Replace("\n", "<br>")}</p>");
            sb.Append("</div></details>\n");
        }

        sb.Append("<footer>Erstellt mit StudyScope</footer>\n</main>\n<script>\n").Append(VideoPlayerHtml.Script).Append("\n</script>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>„8 Folien · 3:12 Min. · mit Ton (Gemini TTS) · Quellen: …“</summary>
    public static string MetaLine(VideoPlayerData data)
    {
        var video = data.Video;
        var parts = new List<string>
        {
            $"{video.Slides.Count} {(video.Slides.Count == 1 ? "Folie" : "Folien")}",
            $"{VideoPlayerHtml.Clock(data.Timeline.Total)} Min."
        };
        parts.Add(data.HasAudio
            ? $"mit Ton{(string.IsNullOrWhiteSpace(video.AudioCreatedWith) ? string.Empty : $" ({video.AudioCreatedWith})")}"
            : "ohne Ton");
        if (video.SourceDocuments.Count > 0)
            parts.Add($"Quellen: {string.Join(", ", video.SourceDocuments)}");
        return string.Join(" · ", parts);
    }

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    private const string PageCss = """
* { box-sizing: border-box; }
body { margin: 0; background: #f1f3f5; color: #212529; font: 15px/1.6 "Segoe UI", -apple-system, Roboto, "Helvetica Neue", Arial, sans-serif; }
.page { max-width: 1100px; margin: 24px auto; padding: 0 16px; }
.badge { display: inline-block; background: #e7f1ff; color: #0b5ed7; border-radius: 999px; padding: 2px 12px; font-size: 13px; font-weight: 600; }
h1 { font-size: 26px; line-height: 1.25; margin: 8px 0 4px; }
.meta { color: #6c757d; font-size: 14px; margin-bottom: 14px; }
.page > .ssv-player { max-width: max(480px, calc((100vh - 170px) * 16 / 9)); margin: 0 auto; }
.keys { color: #6c757d; font-size: 12px; margin: 8px 2px 16px; }
details.script { background: #fff; border: 1px solid #dee2e6; border-radius: 8px; }
details.script > summary { cursor: pointer; padding: 10px 14px; font-weight: 600; }
.script-body { padding: 0 16px 8px; border-top: 1px solid #dee2e6; }
.script-body p { margin: 10px 0; }
.script-body b { color: #007bff; }
footer { color: #adb5bd; font-size: 12px; text-align: center; margin: 24px 0; }
@media print { details.script > * { display: block; } details.script { border: none; } }
""";
}
