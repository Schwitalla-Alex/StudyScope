using Photino.NET;
using StudyScope.Infrastructure.Services;

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
        string filterDescription = "Markdown-Dateien (*.md)",
        string title = "Exportieren als...")
    {
        return Task.Run(() =>
        {
            if (MainWindow == null) return null;

            try
            {
                var defaultDirectory = GetDefaultDirectory();

                var filters = FileDialogFilters.ForPhotino(new (string, string[])[]
                {
                    (filterDescription, new[] { $"*.{filterExtension}" }),
                    ("Alle Dateien (*.*)", new[] { "*.*" })
                }, OperatingSystem.IsLinux());

                var selectedFile = MainWindow.ShowSaveFile(
                    title,
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
                    ("Unterstützte Dokumente (*.pdf, *.txt, *.csv)", new[] { "*.pdf", "*.txt", "*.csv" }),
                    ("PDF-Dokumente (*.pdf)", new[] { "*.pdf" }),
                    ("Textdateien (*.txt)", new[] { "*.txt" }),
                    ("CSV-Dateien (*.csv)", new[] { "*.csv" }),
                    ("Alle Dateien (*.*)", new[] { "*.*" })
                }, OperatingSystem.IsLinux());

                var files = MainWindow.ShowOpenFile("Dokument auswählen", defaultDirectory, multiSelect: false, filters: filters);
                return files?.FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        });
    }
}