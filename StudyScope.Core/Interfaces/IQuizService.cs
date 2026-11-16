using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

/// <summary>
/// Quiz: Themen per KI vorschlagen, Fragen paketweise erzeugen, Themen und Verlauf je Profil speichern.
/// </summary>
public interface IQuizService
{
    /// <summary>Höchstzahl Fragen je KI-Aufruf (größere Mengen werden in mehreren Paketen erzeugt).</summary>
    int BatchSize { get; }

    Task<QuizTopicSuggestionResult> SuggestTopicsAsync(IEnumerable<string> contextChunks, CancellationToken cancellationToken = default);

    /// <param name="topic">Thema; vorhandene Fragen werden der KI genannt, damit sie sich nicht wiederholen.</param>
    /// <param name="topicChunks">Textstellen der Dokumente des Themas.</param>
    /// <param name="count">Anzahl gewünschter Fragen (höchstens <see cref="BatchSize"/>).</param>
    /// <param name="allowedTypes">Erlaubte Fragetypen.</param>
    /// <param name="batchIndex">Laufende Nummer des Pakets – sorgt für wechselnde Textstellen.</param>
    Task<QuizQuestionGenerationResult> GenerateQuestionsAsync(
        QuizTopic topic,
        IReadOnlyList<string> topicChunks,
        int count,
        IReadOnlyCollection<QuizQuestionType> allowedTypes,
        int batchIndex,
        CancellationToken cancellationToken = default);

    /// <summary>Lädt Themen und Verlauf; liefert eine leere Bibliothek, wenn noch nichts gespeichert ist.</summary>
    Task<QuizLibrary> LoadLibraryAsync(string profileName);

    Task SaveLibraryAsync(string profileName, QuizLibrary library);
}
