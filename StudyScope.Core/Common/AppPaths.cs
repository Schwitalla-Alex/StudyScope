namespace StudyScope.Core.Common;

/// <summary>
/// Zentrale Pfade von StudyScope, gemeinsam genutzt von App und Setup.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "StudyScope";

    /// <summary>
    /// Umgebungsvariable für Tests: Ist sie gesetzt und nicht leer, liegen alle
    /// Benutzerdaten in diesem Ordner statt im üblichen Benutzerdaten-Ordner
    /// (z. B. für automatische Oberflächentests in einer Sandbox).
    /// </summary>
    public const string UserDataDirectoryVariable = "STUDYSCOPE_USER_DATA_DIR";

    /// <summary>
    /// Ordner für Benutzerdaten (Profile, KI-Konfiguration):
    /// Windows %APPDATA%\StudyScope, Linux ~/.config/StudyScope.
    /// Mit gesetzter Umgebungsvariable <see cref="UserDataDirectoryVariable"/> deren Ordner (als vollständiger Pfad).
    /// Wird bei Installation und Update nie angefasst.
    /// </summary>
    public static string UserDataDirectory
    {
        get
        {
            var overrideDirectory = Environment.GetEnvironmentVariable(UserDataDirectoryVariable);
            if (!string.IsNullOrWhiteSpace(overrideDirectory))
                return Path.GetFullPath(overrideDirectory);

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
        }
    }
}
