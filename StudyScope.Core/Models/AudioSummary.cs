using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace StudyScope.Core.Models;

/// <summary>Art des Hörbeitrags.</summary>
public enum AudioScriptFormat
{
    /// <summary>Zusammenfassung mit einer Sprecherin.</summary>
    Summary,

    /// <summary>Podcast-Gespräch zwischen Moderatorin und Experte.</summary>
    Podcast,

    /// <summary>Kurz-Briefing: das Wichtigste knapp auf den Punkt.</summary>
    Briefing
}

/// <summary>Gewünschte Länge des Hörbeitrags.</summary>
public enum AudioScriptLength
{
    Short,
    Medium,
    Long
}

/// <summary>Ein Abschnitt des Sprechskripts: wer spricht und was.</summary>
public class AudioSegment
{
    public string Speaker { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>Ein Audio-Beitrag (Sprechskript, optional mit erzeugter Audiodatei).</summary>
public class AudioSummary
{
    public const int MaxSegments = 400;
    public const int MaxSegmentLength = 4000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public AudioScriptFormat Format { get; set; } = AudioScriptFormat.Summary;
    public AudioScriptLength Length { get; set; } = AudioScriptLength.Medium;
    public string Topic { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<AudioSegment> Segments { get; set; } = new();
    public List<string> SourceDocuments { get; set; } = new();

    /// <summary>Dateiname der erzeugten Audiodatei im Ordner <c>Audio/&lt;Profil&gt;/</c>; leer = keine Datei.</summary>
    public string AudioFileName { get; set; } = string.Empty;

    /// <summary>Dauer der Audiodatei in Sekunden (0 = unbekannt bzw. keine Datei).</summary>
    public double AudioDurationSeconds { get; set; }

    /// <summary>Startzeit jedes Segments in der Audiodatei (Sekunden) – für Hervorhebung und Springen.</summary>
    public List<double> AudioSegmentStarts { get; set; } = new();

    /// <summary>Wann die Audiodatei erzeugt wurde.</summary>
    public DateTime? AudioCreatedAt { get; set; }

    /// <summary>Prüfsumme des Skripts beim Erzeugen der Audiodatei (erkennt spätere Änderungen).</summary>
    public string AudioScriptHash { get; set; } = string.Empty;

    /// <summary>Womit die Datei erzeugt wurde, z. B. „Gemini TTS (gemini-3.8-flash-tts)“.</summary>
    public string AudioCreatedWith { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasAudioFile => !string.IsNullOrWhiteSpace(AudioFileName);

    /// <summary>Das Skript wurde nach dem Erzeugen der Audiodatei geändert.</summary>
    [JsonIgnore]
    public bool IsAudioOutdated => HasAudioFile && !string.IsNullOrEmpty(AudioScriptHash) && AudioScriptHash != ComputeScriptHash();

    /// <summary>Prüfsumme über Sprecher und Texte aller Segmente.</summary>
    public string ComputeScriptHash()
    {
        var text = string.Join("\u001e", Segments.Select(s => $"{s.Speaker}\u001f{s.Text}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>Entfernt die Angaben zur Audiodatei (die Datei selbst löscht der Dienst).</summary>
    public void ClearAudio()
    {
        AudioFileName = string.Empty;
        AudioDurationSeconds = 0;
        AudioSegmentStarts = new List<double>();
        AudioCreatedAt = null;
        AudioScriptHash = string.Empty;
        AudioCreatedWith = string.Empty;
    }

    /// <summary>Wörter des ganzen Skripts.</summary>
    public int WordCount => Segments.Sum(s => AudioScriptFormats.CountWords(s.Text));

    /// <summary>Zeichen des ganzen Skripts (Grundlage der Kostenschätzung).</summary>
    public int CharacterCount => Segments.Sum(s => s.Text.Length);

    /// <summary>Ungefähre Sprechdauer in Minuten.</summary>
    public double EstimatedMinutes => AudioScriptFormats.EstimateMinutes(WordCount);

    /// <summary>Sprecher in der Reihenfolge ihres ersten Auftretens.</summary>
    public List<string> Speakers => Segments
        .Select(s => s.Speaker.Trim())
        .Where(s => s.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}

/// <summary>Alle Audio-Beiträge eines Profils.</summary>
public class AudioLibrary
{
    public List<AudioSummary> Items { get; set; } = new();
}

/// <summary>Ergebnis einer Skripterstellung durch die KI (Misserfolge wie bei der Mindmap eingeordnet).</summary>
public class AudioScriptResult
{
    public AudioSummary? Summary { get; init; }
    public string RawResponse { get; init; } = string.Empty;
    public bool WasRepaired { get; init; }

    /// <summary>Die KI hat reinen Text statt JSON geliefert; das Skript wurde aus den Absätzen gebildet.</summary>
    public bool WasPlainText { get; init; }

    public MindmapFailureReason FailureReason { get; init; }
    public bool IsSuccess => Summary != null;
}

/// <summary>Anzeigenamen, Beschreibungen, Sprecher und Längenvorgaben der Formate.</summary>
public static class AudioScriptFormats
{
    /// <summary>Durchschnittliches Sprechtempo im Deutschen (Wörter je Minute) für Schätzungen.</summary>
    public const double WordsPerMinute = 135;

    public const string NarratorName = "Sprecherin";
    public const string HostName = "Moderatorin";
    public const string ExpertName = "Experte";

    public static IReadOnlyList<AudioScriptFormat> All { get; } =
        new[] { AudioScriptFormat.Summary, AudioScriptFormat.Podcast, AudioScriptFormat.Briefing };

    public static IReadOnlyList<AudioScriptLength> AllLengths { get; } =
        new[] { AudioScriptLength.Short, AudioScriptLength.Medium, AudioScriptLength.Long };

    public static string DisplayName(AudioScriptFormat format) => format switch
    {
        AudioScriptFormat.Podcast => "Podcast-Gespräch",
        AudioScriptFormat.Briefing => "Kurz-Briefing",
        _ => "Zusammenfassung"
    };

    public static string Description(AudioScriptFormat format) => format switch
    {
        AudioScriptFormat.Podcast => "Zwei Stimmen: Eine Moderatorin fragt nach, ein Experte erklärt – locker im Dialog.",
        AudioScriptFormat.Briefing => "Das Wichtigste kompakt: Kernaussagen und Merksätze, ideal zum Wiederholen.",
        _ => "Eine Sprecherin fasst die Unterlagen verständlich und zusammenhängend zusammen."
    };

    public static string Icon(AudioScriptFormat format) => format switch
    {
        AudioScriptFormat.Podcast => "🎙️",
        AudioScriptFormat.Briefing => "⏱️",
        _ => "🎧"
    };

    /// <summary>Feste Sprechernamen je Format (die KI soll genau diese verwenden).</summary>
    /// <summary>
    /// Sprecher-Platz (0/1) eines Segments: nach der Rolle des Formats (Moderatorin 0, Experte 1), sonst nach der
    /// Reihenfolge des ersten Auftretens (abwechselnd). Gilt für Systemstimme und Gemini TTS gleichermaßen.
    /// </summary>
    public static int SpeakerSlot(AudioSummary summary, string speaker)
    {
        var roles = Speakers(summary.Format);
        for (var i = 0; i < roles.Count; i++)
        {
            if (string.Equals(roles[i], speaker?.Trim(), StringComparison.OrdinalIgnoreCase))
                return i % 2;
        }

        var index = summary.Speakers.FindIndex(s => string.Equals(s, speaker?.Trim(), StringComparison.OrdinalIgnoreCase));
        return index <= 0 ? 0 : index % 2;
    }

    /// <summary>Ob im Beitrag zwei Sprecher-Plätze vorkommen (dann zwei Stimmen).</summary>
    public static bool HasTwoSpeakers(AudioSummary summary) =>
        summary.Segments.Select(s => SpeakerSlot(summary, s.Speaker)).Distinct().Count() > 1;

    public static IReadOnlyList<string> Speakers(AudioScriptFormat format) => format == AudioScriptFormat.Podcast
        ? new[] { HostName, ExpertName }
        : new[] { NarratorName };

    public static string LengthName(AudioScriptLength length) => length switch
    {
        AudioScriptLength.Short => "kurz (ca. 3 Min.)",
        AudioScriptLength.Long => "lang (ca. 15 Min.)",
        _ => "mittel (ca. 8 Min.)"
    };

    public static int TargetMinutes(AudioScriptLength length) => length switch
    {
        AudioScriptLength.Short => 3,
        AudioScriptLength.Long => 15,
        _ => 8
    };

    /// <summary>Zielumfang in Wörtern (Minuten × Sprechtempo, gerundet).</summary>
    public static int TargetWords(AudioScriptLength length) =>
        (int)(Math.Round(TargetMinutes(length) * WordsPerMinute / 50.0) * 50);

    public static int CountWords(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static double EstimateMinutes(int words) => words / WordsPerMinute;

    /// <summary>Dauer als „ca. 7 Min.“ bzw. „unter 1 Min.“.</summary>
    public static string FormatMinutes(double minutes) =>
        minutes < 1 ? "unter 1 Min." : $"ca. {Math.Max(1, (int)Math.Round(minutes))} Min.";
}
