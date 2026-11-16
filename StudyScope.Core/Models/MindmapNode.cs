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
    public MindmapNode? Root { get; init; }
    public string RawResponse { get; init; } = string.Empty;
    public bool IsSuccess => Root != null;
}
