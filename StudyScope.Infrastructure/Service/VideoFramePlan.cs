using System.Globalization;
using System.Text;
using StudyScope.Core.Common;

namespace StudyScope.Infrastructure.Services;

/// <summary>
/// Sichtbarer Zustand eines Bilds: aktuelle Folie mit Einblend-Fortschritt je Stichpunkt (0 = verborgen, 1 = sichtbar,
/// bereits mit CSS-„ease“ gewichtet) und – während der Überblendung – die vorige Folie mit Deckkraft der neuen
/// (<see cref="Blend"/>, 1 = keine Überblendung).
/// </summary>
public sealed record VideoFrameState(int Scene, IReadOnlyList<double> Reveal, int Previous, IReadOnlyList<double> PreviousReveal, double Blend)
{
    /// <summary>Schlüssel zum Zusammenfassen gleicher Bilder (auf 1/1000 gerundet).</summary>
    public string Key
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(Scene).Append('|');
            foreach (var r in Reveal) sb.Append(Round(r)).Append(',');
            if (Previous >= 0 && Blend < 1)
            {
                sb.Append('|').Append(Previous).Append('|').Append(Round(Blend)).Append('|');
                foreach (var r in PreviousReveal) sb.Append(Round(r)).Append(',');
            }
            return sb.ToString();
        }
    }

    private static string Round(double value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
}

/// <summary>Ein Bild der concat-Liste: Zustand, erstes Bild (bei <see cref="VideoFramePlan.Fps"/>) und Anzahl Bilder.</summary>
public sealed record VideoFrameSegment(VideoFrameState State, int StartFrame, int FrameCount, int Fps)
{
    public double StartSeconds => StartFrame / (double)Fps;
    public double Seconds => FrameCount / (double)Fps;
}

/// <summary>
/// Bildplan des MP4-Exports, rein rechnerisch (ohne Zeichnen und ohne ffmpeg testbar): Zeitpunkte wie in der App aus
/// <see cref="VideoTimeline"/> (Stichpunkt k erscheint ab <see cref="BulletRevealStart"/>), Einblendung der Stichpunkte
/// und Überblendung zwischen Folien über je 0,6 s mit der CSS-Kurve „ease“. Jedes Bild der festen Bildrate bekommt einen
/// Zustand; aufeinanderfolgende gleiche Zustände werden zu einem Standbild mit Dauer zusammengefasst – gezeichnet werden
/// also nur Standbilder und die Bilder während der Animationen.
/// </summary>
public static class VideoFramePlan
{
    public const int Fps = FfmpegArguments.Fps;

    /// <summary>Dauer der Überblendung zwischen Folien (wie die Einblendung in der App).</summary>
    public const double TransitionSeconds = 0.6;

    /// <summary>Dauer der Einblendung eines Stichpunkts (CSS: transition 0.6s ease).</summary>
    public const double RevealSeconds = 0.6;

    /// <summary>Beginn der Einblendung von Stichpunkt <paramref name="index"/> (ab Szenenbeginn), passend zu <see cref="VideoTimeline.VisibleBullets"/>.</summary>
    public static double BulletRevealStart(int index, int bulletCount, double sceneDuration)
    {
        if (index <= 0 || bulletCount <= 1) return VideoTimeline.FirstBulletDelaySeconds;
        var window = Math.Max(0.5, sceneDuration * VideoTimeline.BulletRevealShare - VideoTimeline.FirstBulletDelaySeconds);
        return VideoTimeline.FirstBulletDelaySeconds + index * window / bulletCount;
    }

    /// <summary>Einblend-Fortschritt (gewichtet) je Stichpunkt nach <paramref name="elapsed"/> Sekunden in der Szene.</summary>
    public static double[] RevealAt(int bulletCount, double elapsed, double sceneDuration)
    {
        var result = new double[bulletCount];
        for (var k = 0; k < bulletCount; k++)
            result[k] = CssEase((elapsed - BulletRevealStart(k, bulletCount, sceneDuration)) / RevealSeconds);
        return result;
    }

    /// <summary>CSS-Zeitkurve „ease“ = cubic-bezier(0.25, 0.1, 0.25, 1.0); außerhalb 0…1 begrenzt.</summary>
    public static double CssEase(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        const double x1 = 0.25, y1 = 0.1, x2 = 0.25, y2 = 1.0;
        // x(s) = t nach s auflösen (Newton, Rückfall Bisektion), dann y(s)
        double s = t;
        for (var i = 0; i < 8; i++)
        {
            var x = Bezier(s, x1, x2) - t;
            var dx = BezierDerivative(s, x1, x2);
            if (Math.Abs(x) < 1e-7) break;
            if (Math.Abs(dx) < 1e-6) break;
            s -= x / dx;
        }
        if (s < 0 || s > 1 || Math.Abs(Bezier(s, x1, x2) - t) > 1e-5)
        {
            double lo = 0, hi = 1;
            s = t;
            for (var i = 0; i < 40; i++)
            {
                if (Bezier(s, x1, x2) < t) lo = s; else hi = s;
                s = (lo + hi) / 2;
            }
        }
        return Bezier(s, y1, y2);
    }

    private static double Bezier(double s, double p1, double p2) => 3 * (1 - s) * (1 - s) * s * p1 + 3 * (1 - s) * s * s * p2 + s * s * s;
    private static double BezierDerivative(double s, double p1, double p2) => 3 * (1 - s) * (1 - s) * p1 + 6 * (1 - s) * s * (p2 - p1) + 3 * s * s * (1 - p2);

    /// <summary>Szene zur Zeit (exakt, ohne die Toleranz der Anzeige).</summary>
    public static int SceneAt(VideoTimeline timeline, double t)
    {
        var index = 0;
        for (var i = 0; i < timeline.Count; i++)
        {
            if (timeline.Starts[i] <= t + 1e-9) index = i;
            else break;
        }
        return index;
    }

    /// <summary>Zustand zur Zeit <paramref name="t"/>.</summary>
    public static VideoFrameState StateAt(double t, VideoTimeline timeline, IReadOnlyList<int> bulletCounts)
    {
        var scene = SceneAt(timeline, t);
        var reveal = RevealAt(Count(bulletCounts, scene), t - timeline.Starts[scene], timeline.Durations[scene]);
        var sinceStart = t - timeline.Starts[scene];
        if (scene > 0 && sinceStart < TransitionSeconds - 1e-9) // Toleranz: 4,6 s darf nicht als 4,5999… zählen
        {
            var previous = scene - 1;
            var previousReveal = RevealAt(Count(bulletCounts, previous), t - timeline.Starts[previous], timeline.Durations[previous]);
            return new VideoFrameState(scene, reveal, previous, previousReveal, CssEase(sinceStart / TransitionSeconds));
        }
        return new VideoFrameState(scene, reveal, -1, Array.Empty<double>(), 1);
    }

    private static int Count(IReadOnlyList<int> counts, int index) => index >= 0 && index < counts.Count ? counts[index] : 0;

    /// <summary>Gesamtzahl der Bilder (mindestens 1).</summary>
    public static int TotalFrames(VideoTimeline timeline, int fps = Fps) => Math.Max(1, (int)Math.Round(timeline.Total * fps));

    /// <summary>Bildplan: Abschnitte mit gleichem Zustand, zusammen genau <see cref="TotalFrames"/> Bilder.</summary>
    public static List<VideoFrameSegment> Build(VideoTimeline timeline, IReadOnlyList<int> bulletCounts, int fps = Fps)
    {
        var segments = new List<VideoFrameSegment>();
        if (timeline.Count == 0) return segments;
        var total = TotalFrames(timeline, fps);
        VideoFrameState? current = null;
        string? currentKey = null;
        var start = 0;
        for (var f = 0; f < total; f++)
        {
            var state = StateAt(f / (double)fps, timeline, bulletCounts);
            var key = state.Key;
            if (key == currentKey) continue;
            if (current != null)
                segments.Add(new VideoFrameSegment(current, start, f - start, fps));
            current = state;
            currentKey = key;
            start = f;
        }
        segments.Add(new VideoFrameSegment(current!, start, total - start, fps));
        return segments;
    }
}
