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
        appBuilder.Services.AddSingleton<IMindmapService>(sp => new MindmapService(sp.GetRequiredService<ILlmService>()));
        appBuilder.Services.AddSingleton<IPresentationService>(sp => new PresentationService(sp.GetRequiredService<ILlmService>()));
        appBuilder.Services.AddSingleton<IQuizService>(sp => new QuizService(sp.GetRequiredService<ILlmService>()));
        appBuilder.Services.AddSingleton<IReportService>(sp => new ReportService(sp.GetRequiredService<ILlmService>()));
        appBuilder.Services.AddSingleton<IDataTableService>(sp => new DataTableService(sp.GetRequiredService<ILlmService>()));
        appBuilder.Services.AddSingleton<ILocalLlmDiscoveryService, LocalLlmDiscoveryService>();
        appBuilder.Services.AddSingleton<IAudioScriptService>(sp => new AudioScriptService(sp.GetRequiredService<ILlmService>()));
        appBuilder.Services.AddSingleton<IVideoScriptService>(sp => new VideoScriptService(sp.GetRequiredService<ILlmService>()));

        // Sprachausgabe: Systemstimme (Web Speech API der WebView) und Gemini TTS, im Reiter „Audio“ je Profil wählbar
        appBuilder.Services.AddSingleton<BrowserSpeechService>();
        appBuilder.Services.AddSingleton<ITextToSpeechService>(sp => sp.GetRequiredService<BrowserSpeechService>());
        appBuilder.Services.AddSingleton<ITextToSpeechService>(_ => new GeminiTextToSpeechService());
        appBuilder.Services.AddSingleton<BrowserAudioPlayer>();
        appBuilder.Services.AddSingleton<IAudioFilePlayer>(sp => sp.GetRequiredService<BrowserAudioPlayer>());

        // ViewModels registrieren
        // Audio und Video teilen sich Player und Systemstimme: es läuft immer nur eine Wiedergabe
        appBuilder.Services.AddSingleton<PlaybackCoordinator>();
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
        appBuilder.Services.AddSingleton<MindmapViewModel>();
        appBuilder.Services.AddSingleton<PresentationViewModel>();
        appBuilder.Services.AddSingleton<QuizViewModel>(sp => new QuizViewModel(sp.GetRequiredService<IQuizService>(), sp.GetRequiredService<IDocumentService>()));
        appBuilder.Services.AddSingleton<DataTableViewModel>(sp => new DataTableViewModel(sp.GetRequiredService<IDataTableService>(), sp.GetRequiredService<IDocumentService>()));
        appBuilder.Services.AddSingleton<AudioViewModel>(sp => new AudioViewModel(
            sp.GetRequiredService<IAudioScriptService>(),
            sp.GetRequiredService<IDocumentService>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetServices<ITextToSpeechService>(),
            sp.GetRequiredService<IAudioFilePlayer>(),
            sp.GetRequiredService<PlaybackCoordinator>()));
        appBuilder.Services.AddSingleton<VideoViewModel>(sp => new VideoViewModel(
            sp.GetRequiredService<IVideoScriptService>(),
            sp.GetRequiredService<IPresentationService>(),
            sp.GetRequiredService<IDocumentService>(),
            sp.GetRequiredService<IStorageService>(),
            sp.GetServices<ITextToSpeechService>(),
            sp.GetRequiredService<IAudioFilePlayer>(),
            sp.GetRequiredService<PlaybackCoordinator>(),
            transcoder: sp.GetService<IAudioTranscoder>()));
        appBuilder.Services.AddSingleton<SpeechSettingsViewModel>(sp => new SpeechSettingsViewModel(
            sp.GetRequiredService<IStorageService>(),
            sp.GetServices<ITextToSpeechService>()));
        appBuilder.Services.AddSingleton<ReportViewModel>(sp => new ReportViewModel(
            sp.GetRequiredService<IReportService>(),
            sp.GetRequiredService<IDocumentService>(),
            sp.GetRequiredService<IMindmapService>(),
            sp.GetRequiredService<IFlashcardService>(),
            sp.GetRequiredService<IQuizService>(),
            sp.GetRequiredService<IPresentationService>(),
            sp.GetRequiredService<IDataTableService>(),
            sp.GetRequiredService<IAudioScriptService>(),
            sp.GetRequiredService<IVideoScriptService>(),
            sp.GetService<IAudioTranscoder>()));

        // Root-Komponente setzen
        appBuilder.RootComponents.Add<App>("#app");

        var app = appBuilder.Build();

        // Photino protokolliert sonst jeden Fensteraufruf und jede Oberflächen-Aktualisierung auf der Konsole
        // (unter Linux im Terminal sichtbar). Die Meldungen während Build() erscheinen weiterhin.
        app.MainWindow.SetLogVerbosity(0);
        DesktopDialogs.MainWindow = app.MainWindow;

        // 1. Fenstertitel korrekt auf StudyScope setzen
        app.MainWindow.SetTitle("StudyScope");
        // Icon-Pfad relativ zum Programmordner, unabhängig vom Arbeitsverzeichnis (z. B. Start über Verknüpfung)
        app.MainWindow.SetIconFile(Path.Combine(AppContext.BaseDirectory, "wwwroot", "favicon.ico"));
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

        // Beim Schließen eine laufende Audio- bzw. Video-Wiedergabe beenden (nicht abwarten: der Aufruf ginge über die WebView)
        var audioViewModel = app.Services.GetRequiredService<AudioViewModel>();
        var videoViewModel = app.Services.GetRequiredService<VideoViewModel>();
        app.MainWindow.RegisterWindowClosingHandler((_, _) =>
        {
            _ = audioViewModel.StopAsync();
            _ = videoViewModel.StopAsync();
            return false; // Schließen nicht verhindern
        });
                
        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            // Logging falls nötig
        };

        app.Run();
    }
}