using StudyScope.Core.Models;

namespace StudyScope.ViewModels.Video;

/// <summary>
/// Zeitachse eines Videos ohne Oberfläche: Startzeit und Dauer je Szene, Szene zu einer Position, Fortschritt und
/// Einblendzeitpunkte der Stichpunkte. Bei der Systemstimme werden die Dauern geschätzt (Wörter ÷ Sprechtempo) und nach
/// jeder gesprochenen Szene durch die gemessene Dauer ersetzt; bei einer Audiodatei gelten die exakten Zeitmarken.
/// </summary>
public sealed class VideoTimeline
{
    /// <summary>Mindestdauer einer Szene (auch ohne Sprechtext bleibt die Folie stehen).</summary>
    public const double MinSceneSeconds = 3.0;

    /// <summary>Anteil der Szene, über den die Stichpunkte nacheinander eingeblendet werden.</summary>
    public const double BulletRevealShare = 0.7;

    /// <summary>Der erste Stichpunkt erscheint kurz nach dem Folienwechsel.</summary>
    public const double FirstBulletDelaySeconds = 0.4;

    private readonly double[] _starts;
    private readonly double[] _durations;

    private VideoTimeline(double[] durations)
    {
        _durations = durations;
        _starts = new double[durations.Length];
        var t = 0.0;
        for (var i = 0; i < durations.Length; i++)
        {
            _starts[i] = t;
            t += durations[i];
        }
        Total = t;
    }

    public int Count => _durations.Length;
    public double Total { get; }
    public IReadOnlyList<double> Starts => _starts;
    public IReadOnlyList<double> Durations => _durations;

    /// <summary>Geschätzte Sprechdauer eines Texts in Sekunden beim Tempo <paramref name="rate"/> (mindestens <see cref="MinSceneSeconds"/>).</summary>
    public static double EstimateSeconds(string? narration, double rate)
    {
        var words = AudioScriptFormats.CountWords(narration);
        var seconds = words / AudioScriptFormats.WordsPerMinute * 60 / Math.Max(SpeechSettings.MinRate, rate);
        return Math.Max(MinSceneSeconds, seconds);
    }

    /// <summary>
    /// Zeitachse aus geschätzten Dauern; <paramref name="measured"/> ersetzt einzelne Szenen (gemessene Dauer, auf Tempo 1
    /// umgerechnet). Die übrigen Schätzungen werden mit dem beobachteten Sprechtempo korrigiert (<see cref="SpeedFactor"/>),
    /// weil Systemstimmen je nach Stimme und Tempo deutlich schneller oder langsamer sprechen als geschätzt.
    /// </summary>
    public static VideoTimeline FromEstimates(IReadOnlyList<VideoScene> scenes, double rate, IReadOnlyDictionary<int, double>? measured = null)
    {
        var factor = SpeedFactor(scenes, measured);
        var durations = new double[scenes.Count];
        for (var i = 0; i < scenes.Count; i++)
        {
            durations[i] = measured != null && measured.TryGetValue(i, out var m)
                ? Math.Max(MinSceneSeconds, m / Math.Max(SpeechSettings.MinRate, rate))
                : string.IsNullOrWhiteSpace(scenes[i].Narration)
                    ? MinSceneSeconds
                    : Math.Max(MinSceneSeconds, EstimateSeconds(scenes[i].Narration, rate) * factor);
        }
        return new VideoTimeline(durations);
    }

    /// <summary>
    /// Verhältnis gemessene zu geschätzter Sprechdauer (Szenen mit Text, beide bei Tempo 1), begrenzt auf 0,5…3;
    /// ohne Messung 1.
    /// </summary>
    public static double SpeedFactor(IReadOnlyList<VideoScene> scenes, IReadOnlyDictionary<int, double>? measured)
    {
        if (measured == null || measured.Count == 0) return 1;
        double estimated = 0, actual = 0;
        foreach (var (index, seconds) in measured)
        {
            if (index < 0 || index >= scenes.Count || string.IsNullOrWhiteSpace(scenes[index].Narration)) continue;
            var words = AudioScriptFormats.CountWords(scenes[index].Narration);
            estimated += words / AudioScriptFormats.WordsPerMinute * 60;
            actual += seconds;
        }
        return estimated <= 0 || actual <= 0 ? 1 : Math.Clamp(actual / estimated, 0.5, 3);
    }

    /// <summary>Zeitachse aus den Startzeiten einer Audiodatei (die letzte Szene endet mit der Datei).</summary>
    public static VideoTimeline FromStarts(IReadOnlyList<double> starts, double totalSeconds, int sceneCount)
    {
        var durations = new double[sceneCount];
        for (var i = 0; i < sceneCount; i++)
        {
            var start = i < starts.Count ? starts[i] : totalSeconds;
            var end = i + 1 < starts.Count && i + 1 < sceneCount ? starts[i + 1] : totalSeconds;
            durations[i] = Math.Max(0.1, end - start);
        }
        return new VideoTimeline(durations);
    }

    public double StartOf(int scene) => Count == 0 ? 0 : _starts[Math.Clamp(scene, 0, Count - 1)];

    public double DurationOf(int scene) => Count == 0 ? 0 : _durations[Math.Clamp(scene, 0, Count - 1)];

    /// <summary>Szene zur Position in Sekunden (vor dem Anfang → 0, nach dem Ende → letzte Szene).</summary>
    public int SceneAt(double seconds)
    {
        if (Count == 0) return -1;
        var index = 0;
        for (var i = 0; i < Count; i++)
        {
            if (_starts[i] <= seconds + 0.05) index = i;
            else break;
        }
        return index;
    }

    /// <summary>Fortschritt 0…1 für eine Position.</summary>
    public double Progress(double seconds) => Total <= 0 ? 0 : Math.Clamp(seconds / Total, 0, 1);

    /// <summary>Position zu einem Bruchteil der Gesamtlänge (Klick in die Fortschrittsleiste).</summary>
    public double PositionAt(double fraction) => Math.Clamp(fraction, 0, 1) * Total;

    /// <summary>
    /// Wie viele Stichpunkte nach <paramref name="elapsedInScene"/> Sekunden sichtbar sind: der erste nach
    /// <see cref="FirstBulletDelaySeconds"/>, die weiteren gleichmäßig verteilt über <see cref="BulletRevealShare"/> der Szene.
    /// </summary>
    public static int VisibleBullets(int bulletCount, double elapsedInScene, double sceneDuration)
    {
        if (bulletCount <= 0) return 0;
        if (elapsedInScene < FirstBulletDelaySeconds) return 0;
        if (bulletCount == 1) return 1;

        var window = Math.Max(0.5, sceneDuration * BulletRevealShare - FirstBulletDelaySeconds);
        var step = window / bulletCount;
        var shown = 1 + (int)Math.Floor((elapsedInScene - FirstBulletDelaySeconds) / step);
        return Math.Clamp(shown, 1, bulletCount);
    }
}
