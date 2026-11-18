using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using StudyScope.Core.Common;
using StudyScope.Core.Interfaces;
using StudyScope.Core.Models;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;

namespace StudyScope.Tests.Services;

/// <summary>
/// Gemeinsamer HTML-Video-Player und HTML-Export eines Videos: Folien wie in der App, Zeitachse (Audio-Zeitmarken bzw.
/// geschätzter Takt), Ton nur bei aktueller Audiodatei (WAV oder MP3), Größenschätzung, Maskierung, Einbettung im Bericht.
/// </summary>
[TestFixture]
public class VideoHtmlExporterTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "StudyScopeVideoHtml_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static VideoDocument Video()
    {
        var presentation = VideoScriptServiceTests.SamplePresentation();
        var video = new VideoScriptService(new FakeLlmService()).CreateFromNotes(presentation, "Zellbiologie");
        video.Title = "Zellen <erklärt>";
        return video;
    }

    /// <summary>Video mit aktueller Audiodatei (WAV im Testordner, Zeitmarken je Szene).</summary>
    private (VideoDocument Video, string Path) VideoWithAudio()
    {
        var video = Video();
        var path = System.IO.Path.Combine(_tempDir, "video.wav");
        VideoScriptServiceTests.WriteWav(path, 6.0);
        video.AudioFileName = "video.wav";
        video.AudioDurationSeconds = 6.0;
        video.AudioSceneStarts = new List<double> { 0, 2.5, 4.0 };
        video.AudioCreatedWith = "Gemini TTS";
        video.AudioScenesHash = video.ComputeScenesHash();
        return (video, path);
    }

    private static JsonElement PlayerJson(string html)
    {
        var match = Regex.Match(html, "<script type=\"application/json\" class=\"ssv-data\">(.*?)</script>", RegexOptions.Singleline);
        Assert.That(match.Success, Is.True, "Datenblock fehlt");
        return JsonDocument.Parse(match.Groups[1].Value).RootElement;
    }

    [Test]
    public void Css_ContainsAppSlideStyle_AndPlayer()
    {
        Assert.That(VideoPlayerHtml.SlidesCss, Does.Contain(".pp-slide-content").And.Contain("cqw").And.Contain(".pp-bullet-hidden"));
        Assert.That(VideoPlayerHtml.Css, Does.Contain(".ssv-player").And.Contain(":fullscreen").And.Contain("transition: opacity 0.6s"));
        Assert.That(VideoPlayerHtml.Script, Does.Contain("requestFullscreen").And.Contain("visibleBullets"));
    }

    [Test]
    public async Task WithoutAudio_FixedTimeline_HintAndNoAudioBlock()
    {
        var video = Video();

        var data = await VideoHtmlExporter.BuildPlayerDataAsync(video, audioPath: null, mp3: null);
        var html = VideoHtmlExporter.Export(data);

        Assert.That(data.HasAudio, Is.False);
        Assert.That(data.NoAudioReason, Does.Contain("keine Gemini-Audiodatei").And.Contain("festen Takt"));
        var expected = VideoTimeline.FromEstimates(video.Scenes, 1.0);
        var json = PlayerJson(html);
        Assert.That(json.GetProperty("hasAudio").GetBoolean(), Is.False);
        Assert.That(json.GetProperty("starts").EnumerateArray().Select(e => e.GetDouble()), Is.EqualTo(expected.Starts).Within(0.001));
        Assert.That(json.GetProperty("total").GetDouble(), Is.EqualTo(expected.Total).Within(0.001));
        Assert.That(json.GetProperty("share").GetDouble(), Is.EqualTo(VideoTimeline.BulletRevealShare));
        Assert.That(html, Does.Contain("<div class=\"ssv-hint\">").And.Contain("🔇").And.Not.Contain("application/octet-stream"));
        Assert.That(html, Does.Contain("<!DOCTYPE html>").And.Not.Contain("<link").And.Not.Contain("src=\"http"), "eigenständig");
    }

    [Test]
    public void Slides_LikeSlideView_TitleAndDensityClasses_FooterAndEscaping()
    {
        var video = Video();
        video.Slides[2].Bullets = Enumerable.Range(1, 12).Select(i => $"Ein ziemlich langer Stichpunkt Nummer {i} mit </script> <b>Text</b>").ToList();

        var html = VideoHtmlExporter.Export(new VideoPlayerData { Video = video, Timeline = VideoTimeline.FromEstimates(video.Scenes, 1) });

        Assert.That(html, Does.Contain("<div class=\"pp-slide title ssv-active\""));
        Assert.That(html, Does.Contain("<div class=\"pp-slide\" aria-hidden=\"true\">"));
        Assert.That(html, Does.Contain("<div class=\"pp-slide very-dense\""));
        Assert.That(html, Does.Contain("<span>2 / 3</span>"));
        Assert.That(html, Does.Not.Contain("<b>Text</b>").And.Not.Contain("mit </script>"), "Folientext maskiert");
        Assert.That(html, Does.Contain("Zellen &lt;erkl&#228;rt&gt;"));
    }

    [Test]
    public async Task WithCurrentAudio_EmbedsWav_TimelineFromAudioStarts()
    {
        var (video, path) = VideoWithAudio();

        Assert.That(VideoHtmlExporter.HasCurrentAudio(video, path), Is.True);
        var data = await VideoHtmlExporter.BuildPlayerDataAsync(video, path, mp3: null);
        var html = VideoHtmlExporter.Export(data);

        Assert.That(data.Audio, Is.EqualTo(await File.ReadAllBytesAsync(path)));
        Assert.That(data.AudioMimeType, Is.EqualTo("audio/wav"));
        Assert.That(data.Timeline.Starts, Is.EqualTo(new[] { 0, 2.5, 4.0 }));
        Assert.That(data.Timeline.Total, Is.EqualTo(6.0));
        Assert.That(html, Does.Contain("class=\"ssv-audio\" data-mime=\"audio/wav\">UklGR"), "base64 von „RIFF“");
        Assert.That(html, Does.Not.Contain("<div class=\"ssv-hint\">"));
        Assert.That(VideoHtmlExporter.MetaLine(data), Does.Contain("mit Ton (Gemini TTS)").And.Contain("0:06 Min."));
    }

    [Test]
    public async Task OutdatedOrMismatchedAudio_IsNotEmbedded()
    {
        var (video, path) = VideoWithAudio();
        video.Scenes[1].Narration = "Geänderter Sprechtext.";

        Assert.That(VideoHtmlExporter.HasCurrentAudio(video, path), Is.False);
        var data = await VideoHtmlExporter.BuildPlayerDataAsync(video, path, mp3: null);
        Assert.That(data.HasAudio, Is.False);
        Assert.That(data.NoAudioReason, Does.Contain("veraltet"));

        var (other, otherPath) = VideoWithAudio();
        other.AudioSceneStarts = new List<double> { 0, 2.5 };
        Assert.That(VideoHtmlExporter.HasCurrentAudio(other, otherPath), Is.False, "Zeitmarken passen nicht zu den Szenen");
        File.Delete(otherPath);
        other.AudioSceneStarts = new List<double> { 0, 2.5, 4.0 };
        Assert.That(VideoHtmlExporter.HasCurrentAudio(other, otherPath), Is.False, "Datei fehlt");
    }

    [Test]
    public async Task Mp3_ViaTranscoder_SmallerEstimate()
    {
        var (video, path) = VideoWithAudio();
        var transcoder = new FakeTranscoder();

        var data = await VideoHtmlExporter.BuildPlayerDataAsync(video, path, transcoder);

        Assert.That(transcoder.Converted, Is.EqualTo(new[] { path }));
        Assert.That(data.AudioMimeType, Is.EqualTo("audio/mpeg"));
        Assert.That(VideoHtmlExporter.Export(data), Does.Contain("data-mime=\"audio/mpeg\">" + Convert.ToBase64String(FakeTranscoder.Bytes)));

        var wav = VideoHtmlExporter.EstimateAudioBytes(video, path, mp3: false);
        var mp3 = VideoHtmlExporter.EstimateAudioBytes(video, path, mp3: true);
        Assert.That(wav, Is.EqualTo(VideoPlayerHtml.Base64Length(new FileInfo(path).Length)));
        Assert.That(mp3, Is.EqualTo(VideoPlayerHtml.Base64Length(6 * 64000 / 8)));
        Assert.That(mp3, Is.LessThan(wav));
    }

    [Test]
    public void Estimate_WavAboutFourMegabytesPerMinute_SilentVideosSmall()
    {
        var (video, path) = VideoWithAudio();
        var silent = Video();

        var e = VideoHtmlExporter.Estimate(new List<(VideoDocument, string?)> { (video, path), (silent, null) }, mp3: false);

        Assert.That(e.VideoCount, Is.EqualTo(2));
        Assert.That(e.VideosWithAudio, Is.EqualTo(1));
        Assert.That(e.UsesMp3, Is.False);
        // 6 s WAV (24 kHz, 16 Bit) ≈ 288 kB → base64 ≈ 384 kB; je Minute also ≈ 3,84 MB
        Assert.That(e.AudioBytes, Is.EqualTo(384_000).Within(200));
        Assert.That(e.TotalBytes, Is.GreaterThan(e.AudioBytes).And.LessThan(e.AudioBytes + 100_000));
        Assert.That(e.SizeText, Is.EqualTo("ca. 0,4 MB"));
        Assert.That(new VideoHtmlSizeEstimate(23_400_000, 0, 1, 0, false).SizeText, Is.EqualTo("ca. 23 MB"));
    }

    [Test]
    public void SlideLayout_CssClassMatchesDensity()
    {
        Assert.That(SlideLayout.CssClass(new PresentationSlide { Kind = SlideKind.Title, Bullets = { new string('x', 900) } }), Is.EqualTo("pp-slide title"));
        Assert.That(SlideLayout.CssClass(new PresentationSlide { Bullets = { "kurz" } }), Is.EqualTo("pp-slide"));
        Assert.That(SlideLayout.CssClass(new PresentationSlide { Bullets = { new string('x', 400) } }), Is.EqualTo("pp-slide dense"));
        Assert.That(SlideLayout.CssClass(new PresentationSlide { Bullets = { new string('x', 600) } }), Is.EqualTo("pp-slide very-dense"));
    }

    // ---------- Bericht ----------

    private static Report Report() => new()
    {
        Title = "Biologie",
        Embeds = new ReportEmbeds { Mindmap = false, Flashcards = false, Quiz = false, Presentation = false, DataTables = false, AudioScripts = false, VideoScripts = true },
        Sections = new List<ReportSection> { new() { Heading = "Überblick", Paragraphs = new List<string> { "Text." } } }
    };

    [Test]
    public void Report_InApp_HasPlayButton_ExportWithoutOptionsHasNone()
    {
        var video = Video();
        var studio = new ReportStudioContent { VideoScripts = new[] { video } };
        var service = new ReportService(new FakeLlmService(), _tempDir);

        var inApp = service.ExportToHtml(Report(), studio, new ReportHtmlOptions { InApp = true });
        var file = service.ExportToHtml(Report(), studio);

        Assert.That(inApp, Does.Contain($"data-video-id=\"{video.Id}\"").And.Contain("studyscope-play-video").And.Contain("Video abspielen"));
        Assert.That(file, Does.Not.Contain("data-video-id").And.Not.Contain("ssv-player"));
        Assert.That(file, Is.EqualTo(service.ExportToHtml(Report(), studio, ReportHtmlOptions.Default)));
    }

    [Test]
    public async Task Report_WithPlayers_OnePlayerPerVideo_ScriptAndCssOnce()
    {
        var (withAudio, path) = VideoWithAudio();
        var silent = Video();
        var studio = new ReportStudioContent { VideoScripts = new[] { withAudio, silent } };
        var players = new Dictionary<string, VideoPlayerData>
        {
            [withAudio.Id] = await VideoHtmlExporter.BuildPlayerDataAsync(withAudio, path, null),
            [silent.Id] = await VideoHtmlExporter.BuildPlayerDataAsync(silent, null, null)
        };

        var html = new ReportService(new FakeLlmService(), _tempDir).ExportToHtml(Report(), studio, new ReportHtmlOptions { VideoPlayers = players });

        Assert.That(Regex.Matches(html, "<div class=\"ssv-player\"").Count, Is.EqualTo(2));
        Assert.That(Regex.Matches(html, "class=\"ssv-audio\"").Count, Is.EqualTo(1), "Ton nur beim Video mit aktueller Audiodatei");
        Assert.That(Regex.Matches(html, "<div class=\"ssv-hint\">").Count, Is.EqualTo(1), "Hinweis nur beim stummen Video");
        Assert.That(Regex.Matches(html, @"if \(window\.StudyScopeVideoPlayers\) return;").Count, Is.EqualTo(1), "Player-Skript nur einmal");
        Assert.That(Regex.Matches(html, @"(?m)^\.ssv-big-play \{").Count, Is.EqualTo(1), "Player-CSS nur einmal");
        Assert.That(WebUtility.HtmlDecode(html), Does.Contain("🎬 Videos").And.Contain("Folie 2: Aufbau der Zelle"), "Sprechtext bleibt");
        Assert.That(html, Does.Contain(".video-player-wrap { display: none !important; }"), "beim Drucken werden die Player ausgeblendet");
    }

    internal sealed class FakeTranscoder : IAudioTranscoder
    {
        public static readonly byte[] Bytes = { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x01, 0x02 };
        public List<string> Converted { get; } = new();
        public bool Available { get; set; } = true;
        public Task<bool> IsMp3AvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Available);
        public Task<byte[]> ConvertToMp3Async(string wavPath, CancellationToken cancellationToken = default)
        {
            Converted.Add(wavPath);
            return Task.FromResult(Bytes);
        }

        public async Task ConvertToMp3FileAsync(string wavPath, string mp3Path, double durationSeconds, IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Converted.Add(wavPath);
            progress?.Report(0.5);
            await File.WriteAllBytesAsync(mp3Path, Bytes, cancellationToken);
            progress?.Report(1);
        }

        public Task<string?> Mp3UnavailableReasonAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Available ? null : "ffmpeg wurde auf diesem Rechner nicht gefunden (Test).");
    }
}
