using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Photino.Blazor;
using StudyScope.Setup;
using StudyScope.Setup.Core.Interfaces;
using StudyScope.Setup.Core.Models;
using StudyScope.Setup.Core.Services;
using StudyScope.Setup.Core.ViewModels;
using StudyScope.Setup.Resources;
using StudyScope.Setup.Services;

/// <summary>
/// Startpunkt & Dependency Injection des Installationsprogramms (gleiches Muster wie StudyScope.UI).
/// Aufruf: StudyScope.Setup [--uninstall]
/// </summary>
class Program
{
    // Logische Fenstergröße (bei 100 % Skalierung)
    private const int WindowWidth = 880, WindowHeight = 660, MinWidth = 720, MinHeight = 560;

    [STAThread]
    static int Main(string[] args)
    {
        // Sprache vor dem ersten Text festlegen: Systemsprache (Deutsch, sonst Englisch), STUDYSCOPE_UI_CULTURE für
        // Prüfungen. Bewusst ohne Auswahl im Assistenten und ohne Eintrag in den Benutzerdaten.
        var culture = SetupLanguage.Apply();

        var mode = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            ? SetupMode.Uninstall
            : SetupMode.Install;

        var payload = new EmbeddedPayloadProvider(Assembly.GetExecutingAssembly());
        var processes = new ProcessService();
        var platform = PlatformServices.Create(payload, processes);

        // Ohne WebView2 (Windows) bzw. WebKitGTK (Linux) kann Photino kein Fenster öffnen → vorher prüfen
        var prerequisites = platform.Prerequisites.Check();
        if (!prerequisites.IsSatisfied)
        {
            NativeMessage.ShowError(prerequisites.Message, prerequisites.HelpUrl);
            return 1;
        }

        // wwwroot ist in die Assembly eingebettet (Single-File-Setup ohne lose Dateien)
        var webRoot = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(webRoot, args);

        var folderPicker = new PhotinoFolderPicker();

        // Services registrieren
        appBuilder.Services.AddSingleton(new SetupContext(mode, PlatformServices.SandboxDirectory != null, culture));
        appBuilder.Services.AddSingleton<IPayloadProvider>(payload);
        appBuilder.Services.AddSingleton<IProcessService>(processes);
        appBuilder.Services.AddSingleton(platform.Paths);
        appBuilder.Services.AddSingleton(platform.Shortcuts);
        appBuilder.Services.AddSingleton(platform.InstallService);
        appBuilder.Services.AddSingleton<IFolderPicker>(folderPicker);

        // ViewModels registrieren
        appBuilder.Services.AddSingleton<InstallViewModel>();
        appBuilder.Services.AddSingleton<SetupWizardViewModel>();
        appBuilder.Services.AddSingleton<UninstallViewModel>();

        // Root-Komponente setzen
        appBuilder.RootComponents.Add<App>("#app");

        var app = appBuilder.Build();
        app.MainWindow.SetLogVerbosity(0);
        folderPicker.Window = app.MainWindow;

        // Schließen-Wunsch der ViewModels beendet das Setup
        app.Services.GetRequiredService<SetupWizardViewModel>().CloseRequested += (_, _) => app.MainWindow.Close();
        app.Services.GetRequiredService<UninstallViewModel>().CloseRequested += (_, _) => app.MainWindow.Close();

        app.MainWindow.SetTitle(mode == SetupMode.Uninstall ? SetupStrings.Window_UninstallTitle : SetupStrings.Window_SetupTitle);

        // Photino erwartet für das Fenster-Icon eine echte Datei → eingebettetes Icon in den Temp-Ordner schreiben
        var iconPath = ExtractEmbeddedFile(webRoot, "favicon.ico");
        if (iconPath != null)
            app.MainWindow.SetIconFile(iconPath);

#if DEBUG
        app.MainWindow.SetDevToolsEnabled(true);
#else
        app.MainWindow.SetDevToolsEnabled(false);
#endif

        app.MainWindow.SetSize(WindowWidth, WindowHeight);
        app.MainWindow.SetMinSize(MinWidth, MinHeight);
        app.MainWindow.Center();

        // Photino arbeitet unter Windows mit physischen Pixeln. Die Skalierung des Monitors ist erst bekannt,
        // wenn das native Fenster existiert → dann auf logische Größe umrechnen (GTK unter Linux skaliert selbst).
        if (OperatingSystem.IsWindows())
        {
            app.MainWindow.RegisterWindowCreatedHandler((sender, _) =>
            {
                if (sender is Photino.NET.PhotinoWindow window)
                    WindowSizing.ApplyLogicalSize(window, WindowWidth, WindowHeight, MinWidth, MinHeight);
            });
        }

        app.Run();
        return 0;
    }

    private static string? ExtractEmbeddedFile(IFileProvider provider, string name)
    {
        try
        {
            var file = provider.GetFileInfo(name);
            if (!file.Exists) return null;

            var targetDir = Path.Combine(Path.GetTempPath(), "StudyScope.Setup");
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, name);

            using var source = file.CreateReadStream();
            using var target = File.Create(targetPath);
            source.CopyTo(target);
            return targetPath;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
