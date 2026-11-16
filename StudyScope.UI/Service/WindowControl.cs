namespace StudyScope.UI.Services;

/// <summary>Steuerung des Hauptfensters (z. B. Vollbild für den Vortragsmodus).</summary>
public static class WindowControl
{
    /// <summary>
    /// Schaltet das Photino-Fenster in den Vollbildmodus bzw. zurück.
    /// Fehler werden ignoriert – der Vortragsmodus füllt dann nur das Fenster aus.
    /// </summary>
    public static void SetFullScreen(bool fullScreen)
    {
        try
        {
            DesktopDialogs.MainWindow?.SetFullScreen(fullScreen);
        }
        catch
        {
            // z. B. von der Plattform nicht unterstützt
        }
    }
}
