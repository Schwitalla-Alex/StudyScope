using Photino.NET;

namespace StudyScope.UI.Services;

public static class DesktopDialogs
{
    public static PhotinoWindow? MainWindow { get; set; }

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
                string? defaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                var filters = new (string, string[])[]
                {
                    (filterDescription, new[] { $"*.{filterExtension}" }),
                    ("Alle Dateien (*.*)", new[] { "*.*" })
                };

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
                string? defaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                var filters = new (string, string[])[]
                {
                    ("Unterstützte Dokumente (*.pdf, *.txt, *.csv)", new[] { "*.pdf", "*.txt", "*.csv" }),
                    ("PDF-Dokumente (*.pdf)", new[] { "*.pdf" }),
                    ("Textdateien (*.txt)", new[] { "*.txt" }),
                    ("CSV-Dateien (*.csv)", new[] { "*.csv" }),
                    ("Alle Dateien (*.*)", new[] { "*.*" })
                };

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