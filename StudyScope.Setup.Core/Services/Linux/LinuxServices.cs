using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StudyScope.Core.Common;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;

namespace StudyScope.Setup.Core.Services.Linux;

/// <summary>Pfade unter Linux nach der XDG Base Directory Specification (Installation pro Benutzer).</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxPathProvider : IPathProvider
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string XdgDir(string variable, string fallbackRelative)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value) ? value : Path.Combine(Home, fallbackRelative);
    }

    public static string DataHome => XdgDir("XDG_DATA_HOME", Path.Combine(".local", "share"));
    public static string CacheHome => XdgDir("XDG_CACHE_HOME", ".cache");

    public string DefaultInstallDirectory => Path.Combine(DataHome, AppInfo.ProductName);

    public string UserDataDirectory => AppPaths.UserDataDirectory;

    /// <summary>WebKitGTK legt pro Programmname eigene Daten an (in WSL nachgewiesen) – sie gehören nur StudyScope.</summary>
    public IReadOnlyList<string> AppCacheDirectories =>
        new[] { Path.Combine(CacheHome, "StudyScope.UI"), Path.Combine(DataHome, "StudyScope.UI") };

    public string AppExecutableName => "StudyScope.UI";
    public string SetupExecutableName => "StudyScope.Setup";
    public string? CurrentSetupExecutablePath => Environment.ProcessPath;

    public static string ApplicationsDirectory => Path.Combine(DataHome, "applications");
    public static string IconsDirectory => Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps");
}

/// <summary>Installation unter Linux: Ausführungsrechte setzen, vorhandene Installation über die .desktop-Datei finden.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxInstallService : InstallServiceBase
{
    private readonly string _desktopEntryPath;

    public LinuxInstallService(IPathProvider paths, IPayloadProvider payload, IShortcutService shortcuts,
        IProcessService processes, string? applicationsDirectory = null)
        : base(paths, payload, shortcuts, processes)
    {
        _desktopEntryPath = Path.Combine(applicationsDirectory ?? LinuxPathProvider.ApplicationsDirectory, DesktopEntryBuilder.FileName);
    }

    protected override string? GetRegisteredInstallDirectory()
    {
        try
        {
            return File.Exists(_desktopEntryPath) ? DesktopEntryBuilder.ReadPath(File.ReadAllText(_desktopEntryPath)) : null;
        }
        catch (IOException) { return null; }
    }

    protected override void OnFilesInstalled(string installDirectory, InstallManifest manifest)
    {
        const UnixFileMode executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                                        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

        foreach (var name in new[] { Paths.AppExecutableName, Paths.SetupExecutableName, "createdump" })
        {
            var path = Path.Combine(installDirectory, name);
            if (File.Exists(path))
                File.SetUnixFileMode(path, executable);
        }
    }
}

/// <summary>Menüeintrag (.desktop) im Anwendungsmenü, optional auf dem Desktop, und das Programmsymbol.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxShortcutService : IShortcutService
{
    private readonly string _applicationsDirectory;
    private readonly string _iconsDirectory;
    private readonly string _desktopDirectory;

    public LinuxShortcutService(string? applicationsDirectory = null, string? iconsDirectory = null, string? desktopDirectory = null)
    {
        _applicationsDirectory = applicationsDirectory ?? LinuxPathProvider.ApplicationsDirectory;
        _iconsDirectory = iconsDirectory ?? LinuxPathProvider.IconsDirectory;
        _desktopDirectory = desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    public bool IsMenuShortcutOptional => false;
    public string MenuShortcutLabel => "Eintrag im Anwendungsmenü (mit „Deinstallieren“)";

    public IReadOnlyList<string> CreateShortcuts(ShortcutRequest request)
    {
        var created = new List<string>();

        // Symbol: PNG aus dem Paket ins Icon-Theme kopieren, sonst das ICO direkt referenzieren
        var icon = Path.Combine(request.InstallDirectory, "wwwroot", "favicon.ico");
        var png = Path.Combine(request.InstallDirectory, "wwwroot", "studyscope.png");
        if (File.Exists(png))
        {
            Directory.CreateDirectory(_iconsDirectory);
            var iconTarget = Path.Combine(_iconsDirectory, DesktopEntryBuilder.IconName + ".png");
            File.Copy(png, iconTarget, overwrite: true);
            created.Add(iconTarget);
            icon = DesktopEntryBuilder.IconName;
        }

        var content = DesktopEntryBuilder.Build(request.InstallDirectory, request.AppExecutablePath, request.SetupExecutablePath, icon);

        // Der Menüeintrag wird immer angelegt: Er enthält die Aktion „Deinstallieren“ und dient dem Setup zum Wiederfinden
        Directory.CreateDirectory(_applicationsDirectory);
        var menuEntry = Path.Combine(_applicationsDirectory, DesktopEntryBuilder.FileName);
        WriteEntry(menuEntry, content);
        created.Add(menuEntry);

        if (request.CreateDesktopShortcut && !string.IsNullOrEmpty(_desktopDirectory) && Directory.Exists(_desktopDirectory))
        {
            var desktopEntry = Path.Combine(_desktopDirectory, DesktopEntryBuilder.FileName);
            WriteEntry(desktopEntry, content);
            created.Add(desktopEntry);
        }

        return created;
    }

    public void RemoveShortcuts(IEnumerable<string> shortcutPaths)
    {
        foreach (var path in shortcutPaths)
        {
            var name = Path.GetFileName(path);
            if (name != DesktopEntryBuilder.FileName && name != DesktopEntryBuilder.IconName + ".png")
                continue;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void WriteEntry(string path, string content)
    {
        File.WriteAllText(path, content);
        // Desktop-Umgebungen starten .desktop-Dateien auf dem Schreibtisch nur, wenn sie ausführbar sind
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                   | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }
}

/// <summary>
/// Prüft WebKitGTK 4.1 (Photino 4). install.sh prüft das schon vor dem Start; dies ist die Absicherung,
/// falls das Setup direkt gestartet wird.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxPrerequisiteChecker : IPrerequisiteChecker
{
    public const string WebKitLibrary = "libwebkit2gtk-4.1.so.0";

    public PrerequisiteResult Check()
    {
        if (NativeLibrary.TryLoad(WebKitLibrary, out var handle))
        {
            NativeLibrary.Free(handle);
            return PrerequisiteResult.Ok;
        }

        return new PrerequisiteResult(false,
            "WebKitGTK 4.1 wurde nicht gefunden. Bitte installieren Sie es, z. B. unter Ubuntu:\n" +
            "  sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0t64 libnotify4\n" +
            "und starten Sie das Setup danach erneut (am besten über install.sh).");
    }
}
