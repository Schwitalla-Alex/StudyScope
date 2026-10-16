namespace StudyScope.Core.Models;

public enum FlashcardStatus
{
    New,
    Known,
    Difficult
}

/// <summary>
/// Gemäß den Anforderungen umfasst das Karteikarten-System folgende Kernbereiche:
/// Datenmodell(Flashcard / FlashcardDeck) : Frage, Antwort, Quell-Dokument/Seite, Lernstatus.
/// Generierung via LLM: Aus den aktivierten Dokumenten werden automatisch Frage - Antwort - Paare im JSON-Format erzeugt.
/// Lernmodus-UI: Ein interaktiver Modus(Karte umdrehen: Frage ➔ Antwort, Bewertung „Gewusst“ / „Nicht gewusst“).
/// Druck- & Exportansicht: Ausgabe als DIN - A4 - Druckansicht oder PDF/Markdown.
/// </summary>
public class Flashcard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string SourceDocument { get; set; } = string.Empty;
    public FlashcardStatus Status { get; set; } = FlashcardStatus.New;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}