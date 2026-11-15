using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Photino.Blazor;
using StudyScope.Setup;

/// <summary>
/// Startpunkt & Dependency Injection des Installationsprogramms
/// </summary>
class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // wwwroot ist in die Assembly eingebettet (Single-File-Setup ohne lose Dateien)
        var webRoot = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(webRoot, args);

        // Root-Komponente setzen
        appBuilder.RootComponents.Add<App>("#app");

        var app = appBuilder.Build();

        app.MainWindow.SetTitle("StudyScope Setup");

        // Photino erwartet für das Fenster-Icon eine echte Datei → eingebettetes Icon in den Temp-Ordner schreiben
        var iconPath = ExtractEmbeddedFile(webRoot, "favicon.ico");
        if (iconPath != null)
            app.MainWindow.SetIconFile(iconPath);

#if DEBUG
        app.MainWindow.SetDevToolsEnabled(true);
#else
        app.MainWindow.SetDevToolsEnabled(false);
#endif

        app.MainWindow.SetSize(820, 600);
        app.MainWindow.SetMinSize(700, 500);
        app.MainWindow.Center();

        app.Run();
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
