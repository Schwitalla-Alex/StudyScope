namespace StudyScope.Core.Models;

/// <summary>Art einer Folie – bestimmt die Gestaltung (Titelfolie dunkel, Inhaltsfolien hell).</summary>
public enum SlideKind
{
    Title,
    Content
}

/// <summary>Wie dicht eine Folie gefüllt ist – steuert die Schriftgröße in der Anzeige (CSS-Klassen „dense“/„very-dense“).</summary>
public enum SlideDensity
{
    Normal,
    Dense,
    VeryDense
}

/// <summary>Gemeinsame Gestaltungsregeln der Folien (App, HTML-Player, Folienbilder des MP4-Exports).</summary>
public static class SlideLayout
{
    /// <summary>Dichte einer Folie anhand der Textmenge.</summary>
    public static SlideDensity GetDensity(PresentationSlide slide)
    {
        var amount = slide.Bullets.Sum(b => b.Length + 25);
        return amount switch
        {
            <= 330 => SlideDensity.Normal,
            <= 560 => SlideDensity.Dense,
            _ => SlideDensity.VeryDense
        };
    }

    /// <summary>CSS-Klassen der Folie wie in <c>SlideView.razor</c> („pp-slide title“, „pp-slide dense“ …).</summary>
    public static string CssClass(PresentationSlide slide)
    {
        if (slide.Kind == SlideKind.Title) return "pp-slide title";
        return GetDensity(slide) switch
        {
            SlideDensity.VeryDense => "pp-slide very-dense",
            SlideDensity.Dense => "pp-slide dense",
            _ => "pp-slide"
        };
    }
}

/// <summary>Woraus die Präsentation entstanden ist.</summary>
public enum PresentationOrigin
{
    Ai,
    Mindmap
}

/// <summary>Eine Folie mit Überschrift, Stichpunkten und Sprechernotizen.</summary>
public class PresentationSlide
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public SlideKind Kind { get; set; } = SlideKind.Content;
    public string Title { get; set; } = string.Empty;

    /// <summary>Nur bei Titelfolien: Untertitel.</summary>
    public string Subtitle { get; set; } = string.Empty;

    public List<string> Bullets { get; set; } = new();

    /// <summary>Sprechernotizen (werden nicht auf der Folie angezeigt).</summary>
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Gespeicherte Präsentation eines Profils.</summary>
public class PresentationDocument
{
    public string Title { get; set; } = string.Empty;
    public List<PresentationSlide> Slides { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public PresentationOrigin Origin { get; set; } = PresentationOrigin.Ai;
    public List<string> SourceDocuments { get; set; } = new();
}

/// <summary>
/// Ergebnis einer Präsentations-Erzeugung durch die KI. Die Einordnung von Misserfolgen
/// entspricht der Mindmap (<see cref="MindmapFailureReason"/>), damit dieselben Meldungstexte genutzt werden können.
/// </summary>
public class PresentationGenerationResult
{
    public PresentationDocument? Presentation { get; init; }
    public string RawResponse { get; init; } = string.Empty;
    public bool IsSuccess => Presentation != null;
    public bool WasRepaired { get; init; }
    public MindmapFailureReason FailureReason { get; init; }
}
