using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;

namespace StudyScope.Infrastructure.Services;

/// <summary>HTML-Player des Videos (Export und Bericht) – Logik in <see cref="VideoHtmlExporter"/>.</summary>
public partial class VideoScriptService
{
    public bool HasCurrentAudio(string profileName, VideoDocument video) =>
        VideoHtmlExporter.HasCurrentAudio(video, GetAudioFilePath(profileName, video));

    public string? NoAudioReason(string profileName, VideoDocument video) =>
        VideoHtmlExporter.NoAudioReason(video, GetAudioFilePath(profileName, video));

    public VideoHtmlSizeEstimate EstimateHtml(string profileName, IReadOnlyList<VideoDocument> videos, bool mp3) =>
        VideoHtmlExporter.Estimate(videos.Select(v => (v, GetAudioFilePath(profileName, v))).ToList(), mp3);

    public Task<VideoPlayerData> BuildPlayerDataAsync(string profileName, VideoDocument video, IAudioTranscoder? mp3 = null,
        CancellationToken cancellationToken = default) =>
        VideoHtmlExporter.BuildPlayerDataAsync(video, GetAudioFilePath(profileName, video), mp3, cancellationToken);

    public string ExportToHtml(VideoPlayerData data) => VideoHtmlExporter.Export(data);
}
