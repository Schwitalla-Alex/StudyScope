using Microsoft.Extensions.DependencyInjection;
using Photino.Blazor;
using StudyScope.Core.Interfaces;
using StudyScope.Infrastructure.AI;
using StudyScope.Infrastructure.Services;
using StudyScope.UI;
using StudyScope.UI.Services;
using StudyScope.ViewModels;

/// <summary>
/// Startpunkt & Dependency Injection
/// </summary>
class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var appBuilder = PhotinoBlazorAppBuilder.CreateDefault(args);

        // Services registrieren
        appBuilder.Services.AddSingleton<IStorageService, JsonStorageService>();
        appBuilder.Services.AddSingleton<IDocumentService, DocumentService>();

        // Nur diese EINE Registrierung für ILlmService verwenden:
        appBuilder.Services.AddSingleton<ILlmService>(sp =>
        {
            var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10) // Timeout auf 10 Minuten erhöhen
            };
            return new LlmServiceDispatcher(sp.GetRequiredService<IStorageService>(), httpClient);
        });

        appBuilder.Services.AddSingleton<IFileOpenerService, FileOpenerService>();
        appBuilder.Services.AddSingleton<IFlashcardService, FlashcardService>();
        appBuilder.Services.AddSingleton<ILocalLlmDiscoveryService, LocalLlmDiscoveryService>();

        // ViewModels registrieren
        appBuilder.Services.AddSingleton<DocumentListViewModel>();
        //appBuilder.Services.AddSingleton<ChatViewModel>();
        appBuilder.Services.AddSingleton<ChatViewModel>(sp =>
        new ChatViewModel(
            sp.GetRequiredService<ILlmService>(),
            sp.GetRequiredService<IDocumentService>(),
            sp.GetRequiredService<DocumentListViewModel>()
        ));

        appBuilder.Services.AddSingleton<SettingsViewModel>();
        appBuilder.Services.AddSingleton<FlashcardViewModel>();

        // Root-Komponente setzen
        appBuilder.RootComponents.Add<App>("#app");

        var app = appBuilder.Build();
        DesktopDialogs.MainWindow = app.MainWindow;

        // 1. Fenstertitel korrekt auf StudyScope setzen
        app.MainWindow.SetTitle("StudyScope");
        app.MainWindow.SetIconFile("wwwroot/favicon.ico"); // <-- Pfad zu Ihrer Icon-Datei
        //app.MainWindow.SetSize(new System.Drawing.Size(1200, 800));

        // DevTools aktivieren (ermöglicht F12 und Rechtsklick -> Untersuchen)
#if DEBUG
        app.MainWindow.SetDevToolsEnabled(true);
#else
        app.MainWindow.SetDevToolsEnabled(false);
#endif

        //app.MainWindow.SetFullScreen(true);
        app.MainWindow.SetSize(1850, 1150);
        app.MainWindow.SetMinSize(1000, 700);

        app.MainWindow.Center(); // Zentriert das Fenster auf dem Bildschirm
                
        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            // Logging falls nötig
        };

        app.Run();
    }
}