using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using StudyScope.Core.Common;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Resources;

namespace StudyScope.Setup.Core.Services.Windows;

/// <summary>Pfade unter Windows: Installation pro Benutzer ohne Adminrechte.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPathProvider : IPathProvider
{
    public string DefaultInstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.ProductName);

    public string UserDataDirectory => AppPaths.UserDataDirectory;

    /// <summary>
    /// Leer: Photino legt die WebView2-Daten in %LOCALAPPDATA%\Photino ab – ein Ordner, den sich alle
    /// Photino-Anwendungen teilen. Er wird deshalb bewusst nicht gelöscht.
    /// </summary>
    public IReadOnlyList<string> AppCacheDirectories { get; } = Array.Empty<string>();

    public string AppExecutableName => "StudyScope.UI.exe";
    public string SetupExecutableName => "StudyScope.Setup.exe";
    public string? CurrentSetupExecutablePath => Environment.ProcessPath;
}

/// <summary>
/// Installation unter Windows: zusätzlich Eintrag in „Apps &amp; Features“ (HKCU, keine Adminrechte) und
/// verzögertes Löschen des laufenden Deinstallationsprogramms.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInstallService : InstallServiceBase
{
    public const string DefaultUninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StudyScope";

    private readonly string _uninstallKeyPath;

    /// <param name="uninstallKeyPath">Registry-Pfad unter HKCU (in Tests ein eigener Testschlüssel).</param>
    public WindowsInstallService(IPathProvider paths, IPayloadProvider payload, IShortcutService shortcuts,
        IProcessService processes, string uninstallKeyPath = DefaultUninstallKeyPath)
        : base(paths, payload, shortcuts, processes)
    {
        _uninstallKeyPath = uninstallKeyPath;
    }

    protected override string? GetRegisteredInstallDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_uninstallKeyPath);
        return key?.GetValue("InstallLocation") as string;
    }

    protected override void RegisterInstallation(InstallManifest manifest)
    {
        var dir = manifest.InstallDirectory;
        var setupExe = Path.Combine(dir, Paths.SetupExecutableName);
        var sizeKb = manifest.Files
            .Select(f => SafeCombine(dir, f))
            .Where(f => f != null && File.Exists(f))
            .Sum(f => new FileInfo(f!).Length) / 1024;

        using var key = Registry.CurrentUser.CreateSubKey(_uninstallKeyPath, writable: true);
        key.SetValue("DisplayName", AppInfo.ProductName);
        key.SetValue("DisplayVersion", manifest.Version);
        key.SetValue("Publisher", AppInfo.ProductName);
        key.SetValue("InstallLocation", dir);
        key.SetValue("InstallDate", manifest.InstalledAtUtc.ToLocalTime().ToString("yyyyMMdd"));
        key.SetValue("DisplayIcon", Path.Combine(dir, Paths.AppExecutableName));
        key.SetValue("UninstallString", $"\"{setupExe}\" --uninstall");
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeKb), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    protected override void UnregisterInstallation(string installDirectory)
    {
        Registry.CurrentUser.DeleteSubKeyTree(_uninstallKeyPath, throwOnMissingSubKey: false);
    }

    /// <summary>
    /// Ein laufendes Programm kann sich unter Windows nicht selbst löschen. Ein versteckter cmd-Prozess
    /// wartet kurz, bis das Setup beendet ist, und entfernt dann die Datei und den leeren Ordner.
    /// </summary>
    protected override void HandleLockedFiles(string installDirectory, IReadOnlyList<string> lockedFiles)
    {
        var commands = string.Join(" & ", lockedFiles.Select(f => $"del /f /q \"{f}\""));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 4 > nul & {commands} & rmdir \"{installDirectory}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        })?.Dispose();
    }
}

/// <summary>Verknüpfungen (.lnk) im Startmenü und auf dem Desktop über das COM-Objekt WScript.Shell.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShortcutService : IShortcutService
{
    public const string ShortcutFileName = "StudyScope.lnk";

    private readonly string _startMenuDirectory;
    private readonly string _desktopDirectory;

    public WindowsShortcutService(string? startMenuDirectory = null, string? desktopDirectory = null)
    {
        _startMenuDirectory = startMenuDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        _desktopDirectory = desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    public bool IsMenuShortcutOptional => true;
    public string MenuShortcutLabel => SetupCoreStrings.Shortcut_MenuWindows;

    public IReadOnlyList<string> CreateShortcuts(ShortcutRequest request)
    {
        var created = new List<string>();
        if (request.CreateMenuShortcut)
            created.Add(CreateLink(Path.Combine(_startMenuDirectory, ShortcutFileName), request));
        if (request.CreateDesktopShortcut)
            created.Add(CreateLink(Path.Combine(_desktopDirectory, ShortcutFileName), request));
        return created;
    }

    public void RemoveShortcuts(IEnumerable<string> shortcutPaths)
    {
        foreach (var path in shortcutPaths.Where(p => p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string CreateLink(string linkPath, ShortcutRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InstallException(SetupCoreStrings.Shortcut_NoWScriptShell);
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = request.AppExecutablePath;
            link.WorkingDirectory = request.InstallDirectory;
            link.IconLocation = request.AppExecutablePath + ",0";
            link.Description = SetupCoreStrings.Shortcut_Description;
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        return linkPath;
    }
}

/// <summary>
/// Prüft, ob die WebView2-Runtime installiert ist (benötigt von Photino unter Windows).
/// Schlüssel laut Microsoft-Dokumentation zur Erkennung der Evergreen-Runtime.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WebView2Checker : IPrerequisiteChecker
{
    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";
    private const string ClientKey = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public PrerequisiteResult Check()
    {
        var installed = HasVersion(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\" + ClientKey)
                        || HasVersion(Registry.LocalMachine, @"SOFTWARE\" + ClientKey)
                        || HasVersion(Registry.CurrentUser, @"Software\" + ClientKey);

        return installed
            ? PrerequisiteResult.Ok
            : new PrerequisiteResult(false, SetupCoreStrings.Prerequisite_WebView2Missing, DownloadUrl);
    }

    private static bool HasVersion(RegistryKey hive, string path)
    {
        using var key = hive.OpenSubKey(path);
        var version = key?.GetValue("pv") as string;
        return !string.IsNullOrWhiteSpace(version) && version != "0.0.0.0";
    }
}
