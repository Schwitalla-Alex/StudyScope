namespace StudyScope.Core.Models;

/// <summary>Fragetyp im Quiz.</summary>
public enum QuizQuestionType
{
    /// <summary>Mehrere Antworten, genau eine richtig.</summary>
    SingleChoice,

    /// <summary>Aussage ist wahr oder falsch (Antworten „Wahr“/„Falsch“).</summary>
    TrueFalse,

    /// <summary>Mehrere Antworten, mindestens zwei richtig – nur vollständig richtig zählt.</summary>
    MultipleChoice
}

/// <summary>Lernmodus: Rückmeldung nach jeder Frage. Prüfungsmodus: Auswertung erst am Ende.</summary>
public enum QuizMode
{
    Learn,
    Exam
}

public enum QuizTopicOrigin
{
    Manual,
    Ai
}

/// <summary>Eine Quizfrage. <see cref="CorrectIndices"/> zeigt auf Einträge in <see cref="Options"/>.</summary>
public class QuizQuestion
{
    /// <summary>
    /// Sprache des Inhalts (zweistelliger Code wie „de“/„en“), in der die KI ihn erzeugt hat; null = unbekannt
    /// (ältere Daten oder ohne KI erstellt). Siehe <c>OutputLanguage</c>.
    /// </summary>
    public string? Language { get; set; }

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public QuizQuestionType Type { get; set; } = QuizQuestionType.SingleChoice;
    public string Text { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
    public List<int> CorrectIndices { get; set; } = new();

    /// <summary>Kurze Begründung der richtigen Antwort (aus dem Dokument).</summary>
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>Dokument, auf dem ein Thema beruht.</summary>
public class QuizDocumentRef
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// Thema (Kategorie) mit eigenem Fragenpool. Fragen werden bei Bedarf von der KI ergänzt
/// und bei späteren Quizrunden wiederverwendet.
/// </summary>
public class QuizTopic
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>Worum es im Thema geht – hilft der KI, passende Textstellen und Fragen zu finden.</summary>
    public string Description { get; set; } = string.Empty;

    public QuizTopicOrigin Origin { get; set; } = QuizTopicOrigin.Manual;
    public List<QuizDocumentRef> Documents { get; set; } = new();
    public List<QuizQuestion> Questions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Antwort auf eine Frage in einem abgeschlossenen Quiz.</summary>
public class QuizResultEntry
{
    public string QuestionId { get; set; } = string.Empty;
    public string TopicId { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

/// <summary>Ergebnis eines Quizdurchgangs (für den Verlauf).</summary>
public class QuizResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CompletedAt { get; set; } = DateTime.Now;
    public QuizMode Mode { get; set; }
    public List<string> TopicNames { get; set; } = new();
    public int Total { get; set; }
    public int Correct { get; set; }
    public List<QuizResultEntry> Entries { get; set; } = new();

    public double Percent => Total == 0 ? 0 : Math.Round(Correct * 100.0 / Total, 1);
}

/// <summary>Alle Themen und der Verlauf eines Profils.</summary>
public class QuizLibrary
{
    public List<QuizTopic> Topics { get; set; } = new();
    public List<QuizResult> History { get; set; } = new();
}

/// <summary>Themenvorschlag der KI.</summary>
public class QuizTopicSuggestion
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Ergebnis eines KI-Aufrufs für neue Fragen. Misserfolge werden wie bei der Mindmap eingeordnet.</summary>
public class QuizQuestionGenerationResult
{
    public List<QuizQuestion> Questions { get; init; } = new();
    public string RawResponse { get; init; } = string.Empty;
    public bool WasRepaired { get; init; }
    public MindmapFailureReason FailureReason { get; init; }
    public bool IsSuccess => Questions.Count > 0;
}

/// <summary>Ergebnis der Themenvorschläge durch die KI.</summary>
public class QuizTopicSuggestionResult
{
    public List<QuizTopicSuggestion> Topics { get; init; } = new();
    public string RawResponse { get; init; } = string.Empty;
    public MindmapFailureReason FailureReason { get; init; }
    public bool IsSuccess => Topics.Count > 0;
}
