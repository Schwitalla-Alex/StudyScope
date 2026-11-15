namespace StudyScope.Core.Common;

/// <summary>
/// Zentrale Pfade von StudyScope, gemeinsam genutzt von App und Setup.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "StudyScope";

    /// <summary>
    /// Ordner für Benutzerdaten (Profile, KI-Konfiguration):
    /// Windows %APPDATA%\StudyScope, Linux ~/.config/StudyScope.
    /// Wird bei Installation und Update nie angefasst.
    /// </summary>
    public static string UserDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
}
