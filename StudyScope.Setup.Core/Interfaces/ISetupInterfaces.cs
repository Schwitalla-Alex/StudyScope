using StudyScope.Setup.Core.Models;

namespace StudyScope.Setup.Core.Interfaces;

/// <summary>Plattformabhängige Pfade und Dateinamen.</summary>
public interface IPathProvider
{
    /// <summary>Vorschlag für den Installationsordner (Windows: %LOCALAPPDATA%\Programs\StudyScope, Linux: ~/.local/share/StudyScope).</summary>
    string DefaultInstallDirectory { get; }

    /// <summary>Profile und Einstellungen (AppPaths.UserDataDirectory) – bei Installation und Update tabu.</summary>
    string UserDataDirectory { get; }

    /// <summary>Caches, die nur StudyScope gehören und bei der Deinstallation mit entfernt werden (Linux: WebKit-Daten).</summary>
    IReadOnlyList<string> AppCacheDirectories { get; }

    /// <summary>Dateiname des StudyScope-Programms im Installationsordner (StudyScope.UI.exe bzw. StudyScope.UI).</summary>
    string AppExecutableName { get; }

    /// <summary>Dateiname, unter dem sich das Setup als Deinstallationsprogramm in den Installationsordner kopiert.</summary>
    string SetupExecutableName { get; }

    /// <summary>Pfad des gerade laufenden Setups (null in Tests oder wenn unbekannt).</summary>
    string? CurrentSetupExecutablePath { get; }
}

/// <summary>Liefert das eingebettete Installationspaket (ZIP mit den StudyScope-Programmdateien).</summary>
public interface IPayloadProvider
{
    bool IsAvailable { get; }
    string Version { get; }
    Stream OpenPayload();
}

/// <summary>Installiert, aktualisiert und entfernt StudyScope.</summary>
public interface IInstallService
{
    ExistingInstallation? FindExistingInstallation();

    /// <summary>Liefert eine verständliche Fehlermeldung oder null, wenn der Ordner verwendet werden kann.</summary>
    string? ValidateInstallDirectory(string? installDirectory);

    Task<InstallManifest> InstallAsync(InstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);

    Task UninstallAsync(UninstallOptions options, IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Legt Verknüpfungen an (Windows: Startmenü/Desktop, Linux: .desktop-Dateien).</summary>
public interface IShortcutService
{
    /// <summary>Kann der Menüeintrag abgewählt werden? (Linux: nein, er trägt die Aktion „Deinstallieren“.)</summary>
    bool IsMenuShortcutOptional { get; }

    /// <summary>Bezeichnung des Menüeintrags für die Oberfläche („Startmenü“ bzw. „Anwendungsmenü“).</summary>
    string MenuShortcutLabel { get; }

    /// <summary>Legt die gewünschten Verknüpfungen an und liefert ihre Pfade (für das Manifest).</summary>
    IReadOnlyList<string> CreateShortcuts(ShortcutRequest request);

    void RemoveShortcuts(IEnumerable<string> shortcutPaths);
}

/// <summary>Prozesse prüfen und starten.</summary>
public interface IProcessService
{
    /// <summary>Läuft StudyScope aus diesem Installationsordner gerade?</summary>
    bool IsAppRunning(string installDirectory, string appExecutableName);

    void StartApp(string executablePath, string workingDirectory);
}

/// <summary>Ordnerauswahl-Dialog (wird im Photino-Host implementiert).</summary>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string? initialDirectory);
}

/// <summary>Prüft Laufzeitvoraussetzungen, bevor das Setup-Fenster geöffnet wird.</summary>
public interface IPrerequisiteChecker
{
    PrerequisiteResult Check();
}
