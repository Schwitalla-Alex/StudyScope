using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace StudyScope.Core.Models;

/// <summary>Woher die Sprechtexte eines Videos stammen.</summary>
public enum VideoNarrationSource
{
    /// <summary>Von der Chat-KI des Profils formuliert.</summary>
    Ai,

    /// <summary>Direkt aus den Sprechernotizen der Präsentation übernommen (ohne KI).</summary>
    Notes
}

/// <summary>Eine Szene des Videos: eine Folie mit ihrem Sprechtext (und optional der erzeugten Audiodatei).</summary>
public class VideoScene
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Kennung der Folie in <see cref="VideoDocument.Slides"/> (Zuordnung beim „Folien aktualisieren“).</summary>
    public string SlideId { get; set; } = string.Empty;

    public string Narration { get; set; } = string.Empty;

    /// <summary>Dateiname der Szenen-Audiodatei (WAV) im Ordner des Videos; leer = keine.</summary>
    public string AudioFileName { get; set; } = string.Empty;

    /// <summary>Prüfsumme des Sprechtexts beim Erzeugen der Szenen-Audiodatei.</summary>
    public string AudioTextHash { get; set; } = string.Empty;

    public double AudioDurationSeconds { get; set; }

    [JsonIgnore]
    public bool HasNarration => !string.IsNullOrWhiteSpace(Narration);

    [JsonIgnore]
    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioFileName);

    /// <summary>Die Szene braucht eine (neue) Audiodatei: Sprechtext vorhanden, aber keine passende Datei.</summary>
    [JsonIgnore]
    public bool NeedsAudio => HasNarration && (!HasAudio || AudioTextHash != ComputeTextHash());

    public string ComputeTextHash() => VideoHashing.Hash((Narration ?? string.Empty).Trim());

    public void ClearAudio()
    {
        AudioFileName = string.Empty;
        AudioTextHash = string.Empty;
        AudioDurationSeconds = 0;
    }
}

/// <summary>
/// Vertontes Erklärvideo aus einer Präsentation. Die Folien werden als Kopie gespeichert, damit das Video abspielbar
/// bleibt, wenn die Präsentation später geändert, ersetzt oder gelöscht wird; <see cref="PresentationHash"/> erkennt
/// Änderungen („Präsentation wurde geändert“). Zu jeder Folie gehört genau eine Szene (gleiche Reihenfolge).
/// </summary>
public class VideoDocument
{
    public const int MaxScenes = 60;
    public const int MaxNarrationLength = 3000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Kopie der Folien.</summary>
    public List<PresentationSlide> Slides { get; set; } = new();

    /// <summary>Titel der Präsentation (Fußzeile der Folien).</summary>
    public string PresentationTitle { get; set; } = string.Empty;

    /// <summary>Prüfsumme der Präsentation, aus der die Folien stammen.</summary>
    public string PresentationHash { get; set; } = string.Empty;

    /// <summary>Die Folien wurden im Video-Reiter neu erzeugt (nicht aus der Präsentation des Profils übernommen).</summary>
    public bool OwnSlides { get; set; }

    public List<VideoScene> Scenes { get; set; } = new();
    public VideoNarrationSource NarrationSource { get; set; } = VideoNarrationSource.Ai;
    public List<string> SourceDocuments { get; set; } = new();

    /// <summary>Zusammengesetzte Audiodatei aller Szenen (WAV) im Ordner des Videos; leer = keine.</summary>
    public string AudioFileName { get; set; } = string.Empty;

    public double AudioDurationSeconds { get; set; }

    /// <summary>Startzeit jeder Szene in der zusammengesetzten Audiodatei (Sekunden).</summary>
    public List<double> AudioSceneStarts { get; set; } = new();

    public DateTime? AudioCreatedAt { get; set; }
    public string AudioCreatedWith { get; set; } = string.Empty;

    /// <summary>Prüfsumme über Szenen und Sprechtexte beim Zusammensetzen der Audiodatei.</summary>
    public string AudioScenesHash { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasAudioFile => !string.IsNullOrWhiteSpace(AudioFileName);

    /// <summary>Szenen wurden nach dem Erzeugen der Audiodatei geändert (oder Folien hinzugefügt/entfernt).</summary>
    [JsonIgnore]
    public bool IsAudioOutdated => HasAudioFile && AudioScenesHash != ComputeScenesHash();

    /// <summary>Szenen, deren Audio (neu) erzeugt werden muss.</summary>
    [JsonIgnore]
    public List<VideoScene> ScenesNeedingAudio => Scenes.Where(s => s.NeedsAudio).ToList();

    public PresentationSlide? SlideAt(int index) => index >= 0 && index < Slides.Count ? Slides[index] : null;

    public int WordCount => Scenes.Sum(s => AudioScriptFormats.CountWords(s.Narration));
    public int CharacterCount => Scenes.Sum(s => (s.Narration ?? string.Empty).Length);

    /// <summary>Prüfsumme über Reihenfolge und Sprechtexte der Szenen.</summary>
    public string ComputeScenesHash() =>
        VideoHashing.Hash(string.Join("\u001e", Scenes.Select(s => $"{s.Id}\u001f{(s.Narration ?? string.Empty).Trim()}")));

    public void ClearCombinedAudio()
    {
        AudioFileName = string.Empty;
        AudioDurationSeconds = 0;
        AudioSceneStarts = new List<double>();
        AudioCreatedAt = null;
        AudioCreatedWith = string.Empty;
        AudioScenesHash = string.Empty;
    }

    /// <summary>Prüfsumme einer Präsentation über Titel und Folieninhalte (nicht über Kennungen oder Datum).</summary>
    public static string ComputePresentationHash(PresentationDocument? presentation)
    {
        if (presentation == null) return string.Empty;
        var sb = new StringBuilder(presentation.Title ?? string.Empty);
        foreach (var slide in presentation.Slides)
        {
            sb.Append('\u001e').Append(slide.Kind).Append('\u001f').Append(slide.Title).Append('\u001f').Append(slide.Subtitle)
              .Append('\u001f').Append(string.Join("\u001d", slide.Bullets)).Append('\u001f').Append(slide.Notes);
        }
        return VideoHashing.Hash(sb.ToString());
    }
}

/// <summary>Alle Videos eines Profils.</summary>
public class VideoLibrary
{
    public List<VideoDocument> Items { get; set; } = new();
}

/// <summary>Ergebnis der Erzeugung eines Video-Skripts (Misserfolge wie bei der Mindmap eingeordnet).</summary>
public class VideoScriptResult
{
    public VideoDocument? Video { get; init; }
    public string RawResponse { get; init; } = string.Empty;
    public bool WasRepaired { get; init; }

    /// <summary>Anzahl Szenen, die mangels KI-Text aus den Sprechernotizen bzw. Stichpunkten aufgefüllt wurden.</summary>
    public int FilledFromNotes { get; init; }

    public MindmapFailureReason FailureReason { get; init; }
    public bool IsSuccess => Video != null;
}

/// <summary>Ergebnis des Zusammensetzens der Szenen-Audiodateien zu einer Datei.</summary>
public sealed record VideoAudioAssembly(string FileName, double DurationSeconds, IReadOnlyList<double> SceneStarts);

/// <summary>Sprechtexte ohne KI: aus Sprechernotizen bzw. Titel und Stichpunkten einer Folie.</summary>
public static class VideoNarration
{
    /// <summary>Mindestlänge einer Notiz (Wörter), damit sie als brauchbarer Sprechtext gilt.</summary>
    public const int MinNoteWords = 6;

    /// <summary>Brauchbare Sprechernotizen: mindestens 80 % der Folien mit mindestens <see cref="MinNoteWords"/> Wörtern.</summary>
    public static bool HasUsableNotes(PresentationDocument? presentation)
    {
        if (presentation == null || presentation.Slides.Count == 0) return false;
        var good = presentation.Slides.Count(s => AudioScriptFormats.CountWords(s.Notes) >= MinNoteWords);
        return good >= Math.Ceiling(presentation.Slides.Count * 0.8);
    }

    /// <summary>Sprechernotiz der Folie (bereinigt), sonst ein aus Titel, Untertitel und Stichpunkten gebildeter Text.</summary>
    public static string FromSlide(PresentationSlide slide, Func<string?, string> clean)
    {
        var notes = clean(slide.Notes);
        if (notes.Length > 0) return notes;

        var title = (slide.Title ?? string.Empty).Trim().TrimEnd('.', ':');
        if (slide.Kind == SlideKind.Title)
        {
            var subtitle = (slide.Subtitle ?? string.Empty).Trim().TrimEnd('.');
            return clean(subtitle.Length > 0 ? $"Willkommen! Es geht um {title}. {subtitle}." : $"Willkommen! Es geht um {title}.");
        }

        var bullets = (slide.Bullets ?? new List<string>())
            .Select(b => b.Trim().TrimEnd('.', ';', ','))
            .Where(b => b.Length > 0)
            .Select(b => b + ".");
        var text = title.Length > 0 ? $"{title}. {string.Join(" ", bullets)}" : string.Join(" ", bullets);
        return clean(text);
    }
}

/// <summary>Kurze Prüfsummen (16 Hex-Zeichen) für Änderungserkennung.</summary>
public static class VideoHashing
{
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty)))[..16];
}

/// <summary>
/// Daten eines eingebetteten Video-Players (HTML-Export des Videos und Video-Abschnitt im HTML-Bericht): Folien, Zeitachse
/// und – nur bei aktueller Gemini-Audiodatei – die Tonspur. Ohne Ton laufen die Folien im festen Takt der geschätzten Dauer.
/// </summary>
public sealed class VideoPlayerData
{
    public required VideoDocument Video { get; init; }
    public required StudyScope.Core.Common.VideoTimeline Timeline { get; init; }

    /// <summary>Tonspur (WAV oder MP3) oder null = ohne Ton.</summary>
    public byte[]? Audio { get; init; }

    public string AudioMimeType { get; init; } = "audio/wav";

    /// <summary>Warum es keinen Ton gibt (null = Ton vorhanden).</summary>
    public string? NoAudioReason { get; init; }

    public bool HasAudio => Audio is { Length: > 0 };
}

/// <summary>Ungefähre Größe eines HTML-Exports mit eingebetteten Videos.</summary>
public sealed record VideoHtmlSizeEstimate(long TotalBytes, long AudioBytes, int VideoCount, int VideosWithAudio, bool UsesMp3)
{
    /// <summary>Megabyte (10^6 Byte, wie bei der Gemini-Schätzung).</summary>
    public double Megabytes => TotalBytes / 1_000_000.0;

    /// <summary>„ca. 12,3 MB“ (ab 10 MB ganzzahlig, sehr kleine Dateien „unter 0,1 MB“).</summary>
    public string SizeText
    {
        get
        {
            var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            if (Megabytes < 0.1) return "unter 0,1 MB";
            return Megabytes < 10 ? $"ca. {Megabytes.ToString("0.0", de)} MB" : $"ca. {Megabytes.ToString("0", de)} MB";
        }
    }
}
