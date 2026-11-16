# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Projekt

StudyScope ist eine Desktop-Lernsoftware für Windows und Linux (Photino.Blazor 4) für Dokumenten-Chat (RAG) und automatische Karteikarten-Generierung aus eigenen Unterlagen (`.pdf`, `.txt`, `.csv`). KI-Anbindung wahlweise lokal (Ollama, LM Studio) oder über Cloud-Dienste (Gemini, OpenAI, Mistral, beliebige OpenAI-kompatible Endpunkte). Code, Kommentare und UI-Texte sind auf Deutsch.

## Befehle

Alle Projekte zielen auf `net10.0`. Solution-Datei ist `StudyScope.slnx`. Unter Linux braucht Photino 4 zusätzlich WebKitGTK **4.1** (`libwebkit2gtk-4.1.so.0`), GTK 3 und libnotify. Photino 3.x war gegen WebKitGTK 4.0 gelinkt, das ab Ubuntu 24.04 fehlt – nicht auf 3.x zurückgehen. Alle Photino-Projekte (UI und Setup) müssen dieselbe Photino.Blazor-Version nutzen.

```bash
dotnet build StudyScope.slnx
dotnet run --project StudyScope.UI
dotnet test StudyScope.Tests
dotnet test StudyScope.Tests --filter "FullyQualifiedName~LlmServiceDispatcherTests"   # eine Testklasse
dotnet test StudyScope.Tests --filter "Name=MethodenName"                              # ein einzelner Test
dotnet publish StudyScope.UI -c Release -r win-x64                                     # Release-Build (siehe Properties/PublishProfiles)
.\build\build-setup.ps1 -Runtime all          # Installationsprogramme nach artifacts/ (setup.exe, linux-x64.tar.gz)
./build/build-setup.sh all                     # dasselbe unter Linux
```

Tests: NUnit 4 + Moq. Die Versionsnummer steht zentral in `Directory.Build.props` (gilt für App und Setup).

Testdaten: In Tests nur eindeutig erfundene Werte verwenden, die nicht wie echte API-Keys aussehen (z. B. `"DUMMY_TEST_KEY_…"`, nie `AIza…`, `sk-…` o. Ä.), damit GitHubs Secret-Scanning nicht anschlägt. Ebenso neutrale Profilnamen wie „Testprofil“ statt Namen echter Profile; nie echte Dokumente oder Benutzerdaten ins Repository legen.

## Architektur

Schichtenaufbau mit Abhängigkeiten nur nach innen:

- **StudyScope.Core** – `Common/AppPaths` (gemeinsame Pfade, z. B. Benutzerdaten-Ordner), Interfaces (`ILlmService`, `IStorageService`, `IDocumentService`, `IEncryptionService`, …) und Modelle (`AiConfig` mit `AiProviderType`-Enum, `UserProfile`, `DocumentItem`, `Flashcard`, `ChatMessage`). Keine Abhängigkeiten.
- **StudyScope.Infrastructure** – Implementierungen. Achtung: Ordner heißt `Service/`, der Namespace aber `StudyScope.Infrastructure.Services`.
- **StudyScope.ViewModels** – ViewModels mit handgeschriebenem `INotifyPropertyChanged` (CommunityToolkit.Mvvm ist referenziert, wird aber nicht genutzt).
- **StudyScope.UI** – Photino.Blazor-Host. `Program.cs` ist der DI-Composition-Root (alles als Singleton). Razor-Komponenten in `Components/` injizieren ViewModels per `@inject`, abonnieren `PropertyChanged` und rufen `StateHasChanged` auf (Abmeldung in `Dispose`). Native Dateidialoge laufen über die statische Klasse `DesktopDialogs` (`MainWindow` wird in `Program.cs` gesetzt). Dateipfade zu Programmdateien immer über `AppContext.BaseDirectory` bilden, nie relativ zum Arbeitsverzeichnis.

### Installationsprogramm

- **StudyScope.Setup.Core** – plattformunabhängige Setup-Logik ohne Photino (vollständig in `StudyScope.Tests/Setup` getestet): Interfaces (`IInstallService`, `IPathProvider`, `IShortcutService`, `IPayloadProvider`, …), `InstallServiceBase` (Entpacken, Update, Manifest `install.json`, Deinstallation) mit `Services/Windows` und `Services/Linux` (je mit `[SupportedOSPlatform]`), ViewModels auf Basis von `ViewModelBase`.
- **StudyScope.Setup** – Photino-Host des Assistenten (`--uninstall` startet die Deinstallation); `wwwroot` ist eingebettet (`ManifestEmbeddedFileProvider`), damit die setup.exe eine einzelne Datei ist. Das Installationspaket kommt per `-p:PayloadZip=<zip>` als Ressource `payload.zip` hinein. `css/studyscope-base.css` stammt per `Link` aus StudyScope.UI. Razor-Komponenten erben von `ViewModelComponent` (PropertyChanged-Abo zentral).
- Testen ohne Eingriff ins System: Debug-Build mit Umgebungsvariable `STUDYSCOPE_SETUP_SANDBOX=<ordner>` lenkt Installation, Verknüpfungen, Benutzerdaten und Registry (`HKCU\Software\StudyScope.Sandbox`) um. Mit `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` lässt sich die Oberfläche per DevTools-Protokoll steuern.
- Fenstergröße unter Windows: `WindowSizing` rechnet logische Pixel mit Monitor-DPI **und** Windows-Textgröße (`TextScaleFactor`) um; Photinos `ScreenDpi` allein liefert nur den System-Wert.
- Regeln: Benutzerdaten (`AppPaths.UserDataDirectory`) bei Installation/Update nie anfassen; Deinstallation löscht nur Dateien aus dem Manifest. `%LOCALAPPDATA%\Photino` teilen sich alle Photino-Apps – nicht löschen. Linux-Tests (`[Platform("Linux")]`) laufen nur unter Linux.

### Photino-Eigenheiten (gelten für jedes Photino-Projekt, auch das Setup)

- Dateifilter für `ShowOpenFile`/`ShowSaveFile` immer über `FileDialogFilters.ForPhotino(filters, OperatingSystem.IsLinux())` übergeben: Photino.Native unter Linux zerlegt Filter mit mehreren Endungen falsch und zeigt sonst keine Dateien an.
- Nach `Build()` `app.MainWindow.SetLogVerbosity(0)` setzen, sonst flutet Photino unter Linux die Konsole.
- Profilnamen werden zu Dateinamen: Prüfung über `ProfileNameValidator` (Core, im ViewModel), Absicherung des Pfads in `JsonStorageService.ToSafeProfileFileName`.

### KI-Anbindung

`LlmServiceDispatcher` ist die einzige `ILlmService`-Implementierung im Betrieb: Bei jedem Aufruf lädt er die `AiConfig` des **aktiven Profils** (`last_profile.txt` → `LoadAiConfigForProfileAsync`, dieselbe Quelle wie die Statusleiste; nur wenn das Profil gar keine Einstellung hat, die globale `config.json`) und verzweigt per `switch` auf `SelectedProvider` (Ollama nativ, Gemini nativ, Mistral, sonst OpenAI-kompatibler Pfad für LM Studio/OpenAI/Custom). Ein neuer Provider braucht: Enum-Wert in `AiConfig.cs`, Fälle in `AskQuestionAsync` **und** `ValidateConnectionAsync`, sowie UI in `SettingsModal.razor`/`SettingsViewModel`. `FakeAi` ist der Standard-/Testmodus und liefert für Karteikarten-Prompts (enthält „Lernkarteikarten“ oder „JSON“) ein festes JSON-Array.

### RAG und Karteikarten

- `DocumentService` extrahiert Text (PDF via PdfPig) und zerlegt ihn in Chunks (Standard 250 Wörter).
- Retrieval ist **kein** Embedding-Verfahren: `ChatViewModel.GetMostRelevantChunks` bewertet Chunks per Stichwort-Treffer (mit deutscher Stoppwortliste) und gibt die Top 8 an das LLM.
- `FlashcardService` lässt das LLM ein JSON-Array mit `question`/`answer` erzeugen und parst es; außerdem Markdown-Export und Druck.

### Mindmap

- `MindmapService` (Infrastructure) wählt bis zu 16 gleichmäßig über die Dokumente verteilte Chunks, verlangt vom LLM ein **JSON-Array mit genau einem Wurzelobjekt** (`title`/`summary`/`children`; Array, weil der Dispatcher bei „JSON“-Prompts für einige Provider ein JSON-Array vorgibt) und parst tolerant (Codeblöcke, Begleittext, deutsche Schlüssel, mehrere Wurzeln, Grenzen `MaxDepth`/`MaxNodes`). Abgeschnittene Antworten (häufig bei lokalen Modellen) repariert `AnalyzeResponse`: Kürzen nach dem letzten vollständig geschlossenen Objekt, offene Klammern ergänzen → `WasRepaired`. Misserfolge liefern einen `MindmapFailureReason`; die Benutzertexte dazu (Hauptsatz, Tipps, rohe Antwort nur als einklappbare Details) erzeugt `MindmapStatusTexts` in ViewModels/Mindmap. Speicherung je Profil unter `Mindmaps/<Profil>.json` im Benutzerdaten-Ordner; Markdown-Export.
- Fake-KI: Mindmap-Prompts (enthalten „Mindmap“) werden **vor** dem Karteikarten-Fall erkannt (`FakeMindmapResponse`, genutzt von `FakeLlmService` und `LlmServiceDispatcher.AskFakeAi`).
- `MindmapLayout` (ViewModels/Mindmap) berechnet das horizontale Baum-Layout rein rechnerisch (testbar); `MindmapViewModel` verwaltet Auf-/Zuklappen, Zoom, Auswahl und formuliert die Chat-Frage zu einem Knoten.
- `MindmapView.razor` zeichnet reines SVG ohne JavaScript-Bibliothek. Zahlen in SVG-Attributen immer mit `CultureInfo.InvariantCulture` formatieren (deutsche Kultur schreibt Kommas). „Drucken / PDF“ nutzt `window.print` mit `@media print`-Regeln in der Komponente (nur die Zeichenfläche samt Kopfzeile, A4 quer, SVG auf die Seite eingepasst; gedruckt wird die aktuell aufgeklappte Ansicht). „Im Chat erklären lassen“ läuft über `OnAskQuestion` → `App.razor` wechselt zum Chat und ruft `ChatViewModel.SendPromptAsync` auf.

### Präsentation

- `PresentationService` (Infrastructure): KI-Erzeugung mit demselben Muster wie die Mindmap (JSON-Array mit genau einem Objekt `title`/`subtitle`/`slides[{title, bullets, notes}]`), Auswertung über `LenientJson` (gemeinsam mit `MindmapService`: Codeblöcke, Begleittext, Reparatur abgeschnittener Antworten, `MindmapFailureReason`). Der Prompt darf „Mindmap“ nicht enthalten; die Fake-KI erkennt Präsentationen an „Erstelle eine Präsentation“ + „JSON“ (`FakePresentationResponse`). `CreateFromMindmap` leitet ohne KI ab: Titelfolie, Überblick, je Hauptast eine Folie (Unterthemen als Stichpunkte, max. `MaxBulletsPerSlide`, sonst Fortsetzungsfolien), Zusammenfassung. Speicherung je Profil unter `Presentations/<Profil>.json`.
- `PptxWriter` schreibt die .pptx ohne Zusatzpaket direkt als Office-Open-XML-ZIP (16:9, Folienmaster mit leerem Layout, Textfelder ohne Platzhalter, Sprechernotizen über Notizenmaster). PowerPoint passt Text beim Öffnen nicht an, daher schätzt `FitBulletSize` die Schriftgröße vorab. Prüfbar mit python-pptx bzw. LibreOffice.
- `PresentationViewModel`: Blättern, Vortragsmodus (`HandleKey` wie PowerPoint), Statusmeldungen über `MindmapStatusTexts.ForFailure(reason, raw, "Präsentation", …)`. `PresentationView.razor` rendert jede Folie über ein gemeinsames `RenderFragment` (Vorschau, Hauptansicht, Vortrag, Druck); Maße in `cqw` (Container-Query-Einheiten), damit alle Größen gleich aussehen. Vollbild über `WindowControl.SetFullScreen` (Photino). Druck/PDF: `@media print` mit einer Folie pro Seite (297 × 167 mm).

### Quiz

- Modelle in `Core/Models/Quiz.cs`: `QuizTopic` (Name, Beschreibung, Dokumente als `QuizDocumentRef`, eigener Fragenpool), `QuizQuestion` (`SingleChoice`/`TrueFalse`/`MultipleChoice`, `CorrectIndices` ab 0), `QuizResult` (Verlauf). Alles je Profil in einer `QuizLibrary` unter `Quizzes/<Profil>.json` (atomar über `.tmp`-Datei geschrieben).
- `QuizService` (Infrastructure): Themenvorschläge (Prompt mit „Quiz-Themen“) und Fragen paketweise (`BatchSize` 10, Prompt beginnt mit „Erstelle N Quizfragen“; vorhandene Fragen werden genannt, um Wiederholungen zu vermeiden). Textstellen je Thema per Stichwort-Treffer aus Name/Beschreibung (`SelectChunksForTopic`, wechselnder Ausschnitt je Paket). `ParseQuestions` ist nachsichtig: deutsche Schlüssel, Lösung als Index (ab 0, erkennt ab 1), Buchstabe, Antworttext, true/false, Antworten als Objekte mit „richtig“-Kennzeichen; Typ folgt der Anzahl richtiger Antworten; ungültige Fragen und Wiederholungen (`Normalize`) fliegen raus. Fake-KI: `FakeQuizResponse` (vor dem Karteikarten-Fall).
- ViewModels: `QuizSession` (Ablauf ohne UI: Lernmodus prüft sofort, Prüfungsmodus erst bei `Finish`; Mehrfachauswahl zählt nur vollständig richtig; Antworten werden außer bei Wahr/Falsch gemischt), `QuizComposer` (gleichmäßige Verteilung auf Themen, Auffüllen aus anderen Themen, Vorrang: nie gestellt → zuletzt falsch → richtig), `QuizViewModel` (Themenformular, KI-Vorschläge, Start mit Nachgenerieren fehlender Fragen und Speichern nach jedem Paket, Verlauf, „Falsche wiederholen“). Meldungen über `MindmapStatusTexts.ForFailure(…, "Quiz", …, "kein Quiz", "einem Quiz", "ein Quiz")`.
- `QuizView.razor`: Ansichten Übersicht/Quiz/Ergebnis über `RenderFragment`-Methoden. Achtung Razor: zwei Ausdrücke direkt hintereinander in einem Attribut (`"@(a)@(b)"`) erzeugen ungültigen Code – stattdessen einen interpolierten String verwenden.

### Berichte

- Modelle in `Core/Models/Report.cs`: `Report` (Titel, Zusammenfassung, `ReportTemplate`, `Sections` mit Absätzen/Stichpunkten/Begriffen/Fragen/Merksatz, `ReportEmbeds`, Quelldokumente), `ReportLibrary` je Profil unter `Reports/<Profil>.json` (atomar über `.tmp`). `ReportTemplates` liefert Name, Beschreibung und Symbol je Vorlage.
- `ReportService` (Infrastructure): Prompt beginnt mit „Erstelle einen Bericht“ und verlangt ein JSON-Array mit genau einem Objekt; Auswertung über `LenientJson` (deutsche Schlüssel, Begriffe auch als Objekt-Map oder „Begriff: Erklärung“, Glossar/FAQ ohne Abschnitte, Grenzen `MaxSections`/`MaxItemsPerList`). Fake-KI: `FakeReportResponse` wird **vor allen anderen Fällen** erkannt (eine eigene Vorgabe kann „Mindmap“ o. Ä. enthalten).
- Exporte filtern die Studio-Inhalte (`ReportStudioContent.Filter(report.Embeds)`): `DocxWriter` schreibt die .docx ohne Zusatzpaket als Office-Open-XML-ZIP (Reihenfolge der Kindelemente in `rPr`/`pPr` beachten; prüfbar mit python-docx bzw. LibreOffice), `ReportTextExporter` Markdown und Text, `ReportHtmlExporter` eine eigenständige interaktive Seite (eingebettetes CSS/JS: Mindmap aufklappbar, Karteikarten zum Umdrehen, Selbsttest mit „Prüfen“, Foliennavigation; `@media print` klappt alles auf und zeigt Lösungen).
- `ReportViewModel`: Laden der Studio-Inhalte je Quelle abgesichert, Karteikarten/Quizfragen bevorzugt aus den Quelldokumenten des Berichts (Quizfragen abwechselnd aus den Themen, max. 10), Erstellen/Umbenennen/Löschen, Einbettungen umschalten, Export. `ReportView.razor` zeigt den Bericht per `<iframe srcdoc>`; „PDF“ ruft `print()` im Rahmen auf.

### Persistenz

`JsonStorageService` speichert unter `AppPaths.UserDataDirectory` (Windows `%APPDATA%\StudyScope\`, Linux `~/.config/StudyScope/`): je Profil `Profiles/<name>.json` (Dokumente + profilspezifische `AiSettings`), dazu `config.json` (globale KI-Konfiguration) und `last_profile.txt`. API-Keys werden in Profilen **und** in `config.json` beim Schreiben per `AesEncryptionService` (AES-GCM, fest abgeleiteter Schlüssel) verschlüsselt und beim Lesen entschlüsselt; im Speicher sind sie immer Klartext. Profile daher nur über `WriteProfileFileAsync` schreiben (verschlüsselt eine Kopie), nie das gelesene Objekt direkt serialisieren. Alter Klartext und früher doppelt verschlüsselte Keys bleiben lesbar (`DecryptApiKey`). `SettingsViewModel` prüft die Formularwerte per `ValidateConnectionAsync(config)` und speichert sie danach profilspezifisch (maßgeblich) und global (Rückfall). Profile ohne eigene Einstellung stehen auf dem Standard „Fake-KI (Testmodus)“ und nutzen diese auch.

Für Tests nimmt `JsonStorageService` ein Basisverzeichnis entgegen (`new JsonStorageService(tempDir)`); Tests nutzen ein temporäres Verzeichnis, nicht `%APPDATA%`. Für Oberflächentests der laufenden App lenkt die Umgebungsvariable `STUDYSCOPE_USER_DATA_DIR=<ordner>` `AppPaths.UserDataDirectory` (und damit alle Dienste samt Karteikarten) in einen Sandbox-Ordner um.
