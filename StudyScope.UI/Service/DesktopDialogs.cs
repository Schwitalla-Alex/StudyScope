using Photino.NET;
using StudyScope.Infrastructure.Services;
using StudyScope.UI.Resources;

namespace StudyScope.UI.Services;

public static class DesktopDialogs
{
    public static PhotinoWindow? MainWindow { get; set; }

    /// <summary>
    /// Startordner der Dialoge: „Dokumente“, falls vorhanden (unter Linux oft nicht angelegt), sonst der Home-Ordner.
    /// </summary>
    private static string GetDefaultDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return !string.IsNullOrEmpty(documents) && Directory.Exists(documents)
            ? documents
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public static Task<string?> SaveFileAsync(
        string defaultName,
        string filterExtension = "md",
        string? filterDescription = null,
        string? title = null)
    {
        return Task.Run(() =>
        {
            if (MainWindow == null) return null;

            try
            {
                var defaultDirectory = GetDefaultDirectory();

                var filters = FileDialogFilters.ForPhotino(new (string, string[])[]
                {
                    (filterDescription ?? UiStrings.Dialog_MarkdownFiles, new[] { $"*.{filterExtension}" }),
                    (UiStrings.Dialog_AllFiles, new[] { "*.*" })
                }, OperatingSystem.IsLinux());

                var selectedFile = MainWindow.ShowSaveFile(
                    title ?? UiStrings.Dialog_ExportAs,
                    defaultDirectory,
                    filters);

                if (string.IsNullOrWhiteSpace(selectedFile))
                    return null;

                // Falls der Anwender keine Dateiendung angegeben hat, anhängen
                if (!Path.HasExtension(selectedFile))
                {
                    selectedFile += $".{filterExtension}";
                }

                return selectedFile;
            }
            catch (Exception)
            {
                return null;
            }
        });
    }

    public static Task<string?> OpenPdfAsync()
    {
        return Task.Run(() =>
        {
            if (MainWindow == null) return null;

            try
            {
                var defaultDirectory = GetDefaultDirectory();

                // Unter Linux umgeht FileDialogFilters einen Photino-Fehler bei Filtern mit mehreren Endungen
                var filters = FileDialogFilters.ForPhotino(new (string, string[])[]
                {
                    (UiStrings.Dialog_SupportedDocuments, new[] { "*.pdf", "*.txt", "*.csv" }),
                    (UiStrings.Dialog_PdfFiles, new[] { "*.pdf" }),
                    (UiStrings.Dialog_TextFiles, new[] { "*.txt" }),
                    (UiStrings.Dialog_CsvFiles, new[] { "*.csv" }),
                    (UiStrings.Dialog_AllFiles, new[] { "*.*" })
                }, OperatingSystem.IsLinux());

                var files = MainWindow.ShowOpenFile(UiStrings.Dialog_SelectDocument, defaultDirectory, multiSelect: false, filters: filters);
                return files?.FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        });
    }
}