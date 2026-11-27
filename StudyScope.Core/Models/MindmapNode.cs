namespace StudyScope.Core.Models;

/// <summary>
/// Ein Knoten der Mindmap (Thema bzw. Unterthema) mit kurzer Erklärung und beliebig vielen Unterknoten.
/// </summary>
public class MindmapNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;

    /// <summary>Kurze Erklärung (ein Satz), wird in der Detailansicht angezeigt.</summary>
    public string Summary { get; set; } = string.Empty;

    public List<MindmapNode> Children { get; set; } = new();

    /// <summary>Durchläuft diesen Knoten und alle Unterknoten (Tiefensuche, Wurzel zuerst).</summary>
    public IEnumerable<MindmapNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        foreach (var node in child.DescendantsAndSelf())
            yield return node;
    }
}

/// <summary>
/// Gespeicherte Mindmap eines Profils inklusive Herkunft.
/// </summary>
public class MindmapDocument
{
    /// <summary>
    /// Sprache des Inhalts (zweistelliger Code wie „de“/„en“), in der die KI ihn erzeugt hat; null = unbekannt
    /// (ältere Daten oder ohne KI erstellt). Siehe <c>OutputLanguage</c>.
    /// </summary>
    public string? Language { get; set; }

    public MindmapNode Root { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Dateinamen der Dokumente, aus denen die Mindmap erzeugt wurde.</summary>
    public List<string> SourceDocuments { get; set; } = new();
}

/// <summary>
/// Ergebnis einer Mindmap-Erzeugung. Bei Misserfolg enthält <see cref="RawResponse"/> die Antwort der KI
/// (z. B. eine Fehlermeldung des Providers), damit sie dem Benutzer angezeigt werden kann.
/// </summary>
public class MindmapGenerationResult
{
    /// <summary>
    /// Sprache des Inhalts (zweistelliger Code wie „de“/„en“), in der die KI ihn erzeugt hat; null = unbekannt
    /// (ältere Daten oder ohne KI erstellt). Siehe <c>OutputLanguage</c>.
    /// </summary>
    public string? Language { get; init; }

    public MindmapNode? Root { get; init; }
    public string RawResponse { get; init; } = string.Empty;
    public bool IsSuccess => Root != null;

    /// <summary>
    /// true, wenn die Antwort der KI unvollständig war (z. B. abgeschnitten) und nur der lesbare Teil verwendet wurde.
    /// </summary>
    public bool WasRepaired { get; init; }

    /// <summary>Grund des Misserfolgs (bei Erfolg <see cref="MindmapFailureReason.None"/>).</summary>
    public MindmapFailureReason FailureReason { get; init; }
}

/// <summary>Warum aus der KI-Antwort keine Mindmap entstehen konnte – Grundlage für verständliche Meldungen.</summary>
public enum MindmapFailureReason
{
    None,

    /// <summary>Aus den Dokumenten kam kein Text, die KI wurde nicht gefragt.</summary>
    NoContent,

    /// <summary>Die KI hat nichts geantwortet.</summary>
    EmptyResponse,

    /// <summary>Die KI hat mit Text (oft einer Fehlermeldung des Dienstes) statt mit JSON geantwortet.</summary>
    NoJson,

    /// <summary>JSON vorhanden, aber fehlerhaft oder so stark abgeschnitten, dass nichts lesbar war.</summary>
    InvalidJson,

    /// <summary>Gültiges JSON, aber ohne verwertbare Themen.</summary>
    NoTopics
}
