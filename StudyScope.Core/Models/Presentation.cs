namespace StudyScope.Core.Models;

/// <summary>Art einer Folie – bestimmt die Gestaltung (Titelfolie dunkel, Inhaltsfolien hell).</summary>
public enum SlideKind
{
    Title,
    Content
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
