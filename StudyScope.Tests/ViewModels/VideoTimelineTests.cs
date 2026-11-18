using StudyScope.Core.Models;
using StudyScope.Core.Common;

namespace StudyScope.Tests.ViewModels;

/// <summary>Zeitachse des Videos: Schätzung, Messung, Zeitmarken, Szene zur Position, Sprünge, Stichpunkte.</summary>
[TestFixture]
public class VideoTimelineTests
{
    private static string Words(int count) => string.Join(" ", Enumerable.Repeat("Wort", count));

    [Test]
    public void Estimate_WordsAndRate_MinimumForShortScenes()
    {
        // 135 Wörter = 1 Minute bei Tempo 1
        Assert.That(VideoTimeline.EstimateSeconds(Words(135), 1.0), Is.EqualTo(60).Within(0.01));
        Assert.That(VideoTimeline.EstimateSeconds(Words(135), 2.0), Is.EqualTo(30).Within(0.01));
        Assert.That(VideoTimeline.EstimateSeconds("Kurz.", 1.0), Is.EqualTo(VideoTimeline.MinSceneSeconds));
        Assert.That(VideoTimeline.EstimateSeconds("", 1.0), Is.EqualTo(VideoTimeline.MinSceneSeconds));
    }

    [Test]
    public void FromEstimates_StartsAddUp_MeasuredReplacesEstimate()
    {
        var scenes = new List<VideoScene> { new() { Narration = Words(27) }, new() { Narration = Words(54) }, new() { Narration = "" } };
        var timeline = VideoTimeline.FromEstimates(scenes, 1.0);

        Assert.That(timeline.Durations, Is.EqualTo(new[] { 12.0, 24.0, 3.0 }).Within(0.01));
        Assert.That(timeline.Starts, Is.EqualTo(new[] { 0.0, 12.0, 36.0 }).Within(0.01));
        Assert.That(timeline.Total, Is.EqualTo(39).Within(0.01));

        // Gemessen bei Tempo 1: 20 s; bei Tempo 2 halbiert
        var measured = new Dictionary<int, double> { [0] = 20 };
        Assert.That(VideoTimeline.FromEstimates(scenes, 1.0, measured).DurationOf(0), Is.EqualTo(20));
        Assert.That(VideoTimeline.FromEstimates(scenes, 2.0, measured).DurationOf(0), Is.EqualTo(10));
    }

    [Test]
    public void SpeedFactor_CorrectsUnmeasuredEstimates()
    {
        // Szene 0 (27 Wörter, geschätzt 12 s) dauerte gemessen 24 s → Stimme spricht halb so schnell wie geschätzt
        var scenes = new List<VideoScene> { new() { Narration = Words(27) }, new() { Narration = Words(54) }, new() { Narration = "" } };
        var measured = new Dictionary<int, double> { [0] = 24 };

        Assert.That(VideoTimeline.SpeedFactor(scenes, measured), Is.EqualTo(2).Within(0.001));
        var timeline = VideoTimeline.FromEstimates(scenes, 1.0, measured);
        Assert.That(timeline.Durations, Is.EqualTo(new[] { 24.0, 48.0, 3.0 }).Within(0.01), "Folie ohne Text bleibt bei der Mindestdauer");
        Assert.That(VideoTimeline.SpeedFactor(scenes, new Dictionary<int, double> { [0] = 1000 }), Is.EqualTo(3), "begrenzt");
        Assert.That(VideoTimeline.SpeedFactor(scenes, null), Is.EqualTo(1));
    }

    [Test]
    public void FromStarts_UsesExactMarks_LastSceneEndsWithFile()
    {
        var timeline = VideoTimeline.FromStarts(new[] { 0.0, 4.2, 9.0 }, 15.5, 3);

        Assert.That(timeline.Durations, Is.EqualTo(new[] { 4.2, 4.8, 6.5 }).Within(0.001));
        Assert.That(timeline.Total, Is.EqualTo(15.5).Within(0.001));
    }

    [Test]
    public void SceneAt_ProgressAndSeek()
    {
        var timeline = VideoTimeline.FromStarts(new[] { 0.0, 10.0, 20.0 }, 30, 3);

        Assert.That(timeline.SceneAt(-1), Is.EqualTo(0));
        Assert.That(timeline.SceneAt(9.9), Is.EqualTo(0));
        Assert.That(timeline.SceneAt(10.0), Is.EqualTo(1));
        Assert.That(timeline.SceneAt(99), Is.EqualTo(2));
        Assert.That(timeline.Progress(15), Is.EqualTo(0.5));
        Assert.That(timeline.Progress(45), Is.EqualTo(1));
        Assert.That(timeline.PositionAt(0.75), Is.EqualTo(22.5));
        Assert.That(timeline.PositionAt(2), Is.EqualTo(30));
    }

    [Test]
    public void VisibleBullets_RevealOneAfterAnother_WithinFirstPartOfScene()
    {
        // 4 Stichpunkte, Szene 10 s: Fenster 7 s - 0,4 s = 6,6 s → alle 1,65 s ein weiterer
        Assert.That(VideoTimeline.VisibleBullets(4, 0.0, 10), Is.EqualTo(0));
        Assert.That(VideoTimeline.VisibleBullets(4, 0.5, 10), Is.EqualTo(1));
        Assert.That(VideoTimeline.VisibleBullets(4, 2.1, 10), Is.EqualTo(2));
        Assert.That(VideoTimeline.VisibleBullets(4, 5.5, 10), Is.EqualTo(4));
        Assert.That(VideoTimeline.VisibleBullets(4, 9.0, 10), Is.EqualTo(4));
        Assert.That(VideoTimeline.VisibleBullets(0, 5, 10), Is.EqualTo(0));
        Assert.That(VideoTimeline.VisibleBullets(1, 0.5, 10), Is.EqualTo(1));
    }
}
