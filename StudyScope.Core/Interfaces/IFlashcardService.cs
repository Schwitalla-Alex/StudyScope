using StudyScope.Core.Models;

namespace StudyScope.Core.Interfaces;

public interface IFlashcardService
{
    Task<List<Flashcard>> GenerateFlashcardsAsync(IEnumerable<string> contextChunks, int count = 5);
    Task<List<Flashcard>> LoadFlashcardsAsync(string profileName);
    Task SaveFlashcardsAsync(string profileName, IEnumerable<Flashcard> flashcards);
    string ExportToMarkdown(string profileName, IEnumerable<Flashcard> flashcards);
}
