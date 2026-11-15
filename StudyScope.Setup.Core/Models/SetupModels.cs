namespace StudyScope.Setup.Core.Models;

/// <summary>Was das Setup beim Start tun soll (über das Argument --uninstall gewählt).</summary>
public enum SetupMode
{
    Install,
    Uninstall
}

/// <summary>Benutzerentscheidungen für eine Installation oder ein Update.</summary>
public sealed class InstallOptions
{
    public required string InstallDirectory { get; init; }
    public bool CreateDesktopShortcut { get; init; } = true;
    public bool CreateMenuShortcut { get; init; } = true;
}

/// <summary>Benutzerentscheidungen für die Deinstallation.</summary>
public sealed class UninstallOptions
{
    public required string InstallDirectory { get; init; }

    /// <summary>Profile und Einstellungen (AppPaths.UserDataDirectory) mitlöschen. Standard: nein.</summary>
    public bool DeleteUserData { get; init; }
}

/// <summary>Fortschrittsmeldung eines Installationsschritts (0–100 %).</summary>
public sealed record InstallProgress(int Percent, string Message);

/// <summary>Eingaben für das Anlegen der Verknüpfungen.</summary>
public sealed class ShortcutRequest
{
    public required string InstallDirectory { get; init; }
    public required string AppExecutablePath { get; init; }
    public string? SetupExecutablePath { get; init; }
    public bool CreateDesktopShortcut { get; init; }
    public bool CreateMenuShortcut { get; init; }
}

/// <summary>Ergebnis einer Voraussetzungsprüfung (z. B. WebView2-Runtime).</summary>
public sealed record PrerequisiteResult(bool IsSatisfied, string Message = "", string? HelpUrl = null)
{
    public static PrerequisiteResult Ok { get; } = new(true);
}

/// <summary>Eine gefundene StudyScope-Installation (für Update und Deinstallation).</summary>
public sealed record ExistingInstallation(string InstallDirectory, InstallManifest Manifest);

/// <summary>
/// Inhalt von install.json im Installationsordner. Die Deinstallation entfernt ausschließlich die hier
/// aufgeführten Dateien und Verknüpfungen – fremde Dateien im Ordner bleiben unangetastet.
/// </summary>
public sealed class InstallManifest
{
    public string Version { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public DateTime InstalledAtUtc { get; set; }

    /// <summary>Installierte Dateien relativ zum Installationsordner, mit "/" getrennt.</summary>
    public List<string> Files { get; set; } = new();

    /// <summary>Angelegte Verknüpfungen (absolute Pfade, auch außerhalb des Installationsordners).</summary>
    public List<string> Shortcuts { get; set; } = new();
}

/// <summary>Fehler mit einer Meldung, die direkt in der Oberfläche angezeigt werden kann.</summary>
public sealed class InstallException : Exception
{
    public InstallException(string message, Exception? inner = null) : base(message, inner) { }
}
