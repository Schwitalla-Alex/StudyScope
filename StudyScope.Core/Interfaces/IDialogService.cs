namespace StudyScope.Core.Interfaces;

public interface IDialogService
{
    Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string filter);
    Task<string?> ShowOpenFileDialogAsync(string title, string filter);
}