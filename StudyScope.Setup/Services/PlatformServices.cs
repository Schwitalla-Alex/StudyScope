using System.Diagnostics;
using System.Runtime.InteropServices;
using Photino.NET;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Services;
using StudyScope.Setup.Core.Services.Linux;
using StudyScope.Setup.Core.Services.Windows;

namespace StudyScope.Setup.Services;

/// <summary>
/// Wählt die Windows- bzw. Linux-Implementierungen aus (Composition Root für alles Plattformabhängige).
/// </summary>
public sealed class PlatformServices
{
    public required IPathProvider Paths { get; init; }
    public required IShortcutService Shortcuts { get; init; }
    public required IInstallService InstallService { get; init; }
    public required IPrerequisiteChecker Prerequisites { get; init; }

    /// <summary>
    /// Nur in Debug-Builds: Ist STUDYSCOPE_SETUP_SANDBOX gesetzt, landen Installation, Verknüpfungen, Benutzerdaten
    /// und der Registry-Eintrag in einem Testordner bzw. Testschlüssel – für automatisierte Oberflächentests,
    /// ohne Startmenü, Desktop oder „Apps &amp; Features“ des Entwicklers zu verändern.
    /// </summary>
    public static string? SandboxDirectory
    {
        get
        {
#if DEBUG
            var dir = Environment.GetEnvironmentVariable("STUDYSCOPE_SETUP_SANDBOX");
            return string.IsNullOrWhiteSpace(dir) ? null : Path.GetFullPath(dir);
#else
            return null;
#endif
        }
    }

    public static PlatformServices Create(IPayloadProvider payload, IProcessService processes)
    {
        var sandbox = SandboxDirectory;

        if (OperatingSystem.IsWindows())
        {
            IPathProvider paths = new WindowsPathProvider();
            WindowsShortcutService shortcuts;
            WindowsInstallService install;

            if (sandbox != null)
            {
                paths = new SandboxPathProvider(paths, sandbox);
                shortcuts = new WindowsShortcutService(Path.Combine(sandbox, "Startmenue"), Path.Combine(sandbox, "Desktop"));
                install = new WindowsInstallService(paths, payload, shortcuts, processes, @"Software\StudyScope.Sandbox\Uninstall\StudyScope");
            }
            else
            {
                shortcuts = new WindowsShortcutService();
                install = new WindowsInstallService(paths, payload, shortcuts, processes);
            }

            return new PlatformServices { Paths = paths, Shortcuts = shortcuts, InstallService = install, Prerequisites = new WebView2Checker() };
        }

        if (OperatingSystem.IsLinux())
        {
            IPathProvider paths = new LinuxPathProvider();
            LinuxShortcutService shortcuts;
            LinuxInstallService install;

            if (sandbox != null)
            {
                paths = new SandboxPathProvider(paths, sandbox);
                var apps = Path.Combine(sandbox, "applications");
                shortcuts = new LinuxShortcutService(apps, Path.Combine(sandbox, "icons"), Path.Combine(sandbox, "Desktop"));
                install = new LinuxInstallService(paths, payload, shortcuts, processes, apps);
            }
            else
            {
                shortcuts = new LinuxShortcutService();
                install = new LinuxInstallService(paths, payload, shortcuts, processes);
            }

            return new PlatformServices { Paths = paths, Shortcuts = shortcuts, InstallService = install, Prerequisites = new LinuxPrerequisiteChecker() };
        }

        throw new PlatformNotSupportedException("Das StudyScope-Setup unterstützt Windows und Linux.");
    }

    /// <summary>Lenkt Standardordner und Benutzerdaten in den Sandbox-Ordner um (nur Debug, siehe <see cref="SandboxDirectory"/>).</summary>
    private sealed class SandboxPathProvider : IPathProvider
    {
        private readonly IPathProvider _inner;
        private readonly string _root;

        public SandboxPathProvider(IPathProvider inner, string root)
        {
            _inner = inner;
            _root = root;
        }

        public string DefaultInstallDirectory => Path.Combine(_root, "Programme", "StudyScope");
        public string UserDataDirectory => Path.Combine(_root, "Benutzerdaten", "StudyScope");
        public IReadOnlyList<string> AppCacheDirectories => Array.Empty<string>();
        public string AppExecutableName => _inner.AppExecutableName;
        public string SetupExecutableName => _inner.SetupExecutableName;
        public string? CurrentSetupExecutablePath => _inner.CurrentSetupExecutablePath;
    }
}

/// <summary>Ordnerauswahl über den nativen Photino-Dialog.</summary>
public sealed class PhotinoFolderPicker : IFolderPicker
{
    public PhotinoWindow? Window { get; set; }

    public async Task<string?> PickFolderAsync(string? initialDirectory)
    {
        if (Window == null)
            return null;

        var start = !string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory)
            ? initialDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var result = await Window.ShowOpenFolderAsync("Installationsordner wählen", start, multiSelect: false);
        return result?.FirstOrDefault();
    }
}

/// <summary>
/// Fehlermeldung vor dem Öffnen des Photino-Fensters (z. B. fehlende WebView2-Runtime), daher ohne WebView:
/// unter Windows als MessageBox, unter Linux auf der Konsole.
/// </summary>
public static class NativeMessage
{
    private const uint MbYesNo = 0x4, MbOk = 0x0, MbIconWarning = 0x30, MbIconError = 0x10;
    private const int IdYes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>Zeigt die Meldung; mit Hilfe-Link wird angeboten, die Seite im Browser zu öffnen.</summary>
    public static void ShowError(string message, string? helpUrl = null)
    {
        if (OperatingSystem.IsWindows())
        {
            if (helpUrl != null)
            {
                var answer = MessageBoxW(IntPtr.Zero, message + "\n\nDownload-Seite jetzt im Browser öffnen?\n" + helpUrl,
                    "StudyScope Setup", MbYesNo | MbIconWarning);
                if (answer == IdYes)
                    Process.Start(new ProcessStartInfo(helpUrl) { UseShellExecute = true })?.Dispose();
            }
            else
            {
                MessageBoxW(IntPtr.Zero, message, "StudyScope Setup", MbOk | MbIconError);
            }
            return;
        }

        Console.Error.WriteLine("StudyScope Setup: " + message);
        if (helpUrl != null)
            Console.Error.WriteLine(helpUrl);
    }
}

/// <summary>
/// Fenstergröße unter Windows: Photino erwartet physische Pixel, und ScreenDpi liefert nur den System-Wert.
/// Maßgeblich ist die Skalierung des Monitors, auf dem das Fenster liegt (z. B. 175 % auf Notebooks).
/// </summary>
public static class WindowSizing
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public static void ApplyLogicalSize(PhotinoWindow window, int width, int height, int minWidth, int minHeight)
    {
        if (!OperatingSystem.IsWindows())
            return;

        double scale;
        try
        {
            var dpi = GetDpiForWindow(window.WindowHandle);
            scale = dpi >= 96 ? dpi / 96.0 : 1.0;
        }
        catch (EntryPointNotFoundException)
        {
            scale = window.ScreenDpi >= 96 ? window.ScreenDpi / 96.0 : 1.0;
        }

        // Die Windows-Einstellung „Textgröße“ (Barrierefreiheit) vergrößert auch die WebView-Inhalte
        scale *= GetTextScaleFactor();

        var w = (int)(width * scale);
        var h = (int)(height * scale);

        // Höchstens 90 % der Arbeitsfläche (ohne Taskleiste), damit nichts über den Bildschirmrand ragt
        var work = window.MainMonitor.WorkArea;
        if (work.Width > 0 && work.Height > 0)
        {
            w = Math.Min(w, (int)(work.Width * 0.9));
            h = Math.Min(h, (int)(work.Height * 0.9));
        }

        window.SetMinSize(Math.Min((int)(minWidth * scale), w), Math.Min((int)(minHeight * scale), h));
        window.SetSize(w, h);
        window.Center();
    }

    /// <summary>Windows-Textgröße in Prozent (Einstellungen → Barrierefreiheit → Textgröße), als Faktor.</summary>
    private static double GetTextScaleFactor()
    {
        if (!OperatingSystem.IsWindows())
            return 1.0;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
        return key?.GetValue("TextScaleFactor") is int percent && percent is >= 100 and <= 225 ? percent / 100.0 : 1.0;
    }
}

/// <summary>Kontext des Setup-Laufs (Installation oder Deinstallation), per DI für App.razor.</summary>
public sealed record SetupContext(Core.Models.SetupMode Mode, bool IsSandbox);
