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
```

Tests: NUnit 4 + Moq. Die Versionsnummer steht zentral in `Directory.Build.props` (gilt für App und Setup).

## Architektur

Schichtenaufbau mit Abhängigkeiten nur nach innen:

- **StudyScope.Core** – `Common/AppPaths` (gemeinsame Pfade, z. B. Benutzerdaten-Ordner), Interfaces (`ILlmService`, `IStorageService`, `IDocumentService`, `IEncryptionService`, …) und Modelle (`AiConfig` mit `AiProviderType`-Enum, `UserProfile`, `DocumentItem`, `Flashcard`, `ChatMessage`). Keine Abhängigkeiten.
- **StudyScope.Infrastructure** – Implementierungen. Achtung: Ordner heißt `Service/`, der Namespace aber `StudyScope.Infrastructure.Services`.
- **StudyScope.ViewModels** – ViewModels mit handgeschriebenem `INotifyPropertyChanged` (CommunityToolkit.Mvvm ist referenziert, wird aber nicht genutzt).
- **StudyScope.UI** – Photino.Blazor-Host. `Program.cs` ist der DI-Composition-Root (alles als Singleton). Razor-Komponenten in `Components/` injizieren ViewModels per `@inject`, abonnieren `PropertyChanged` und rufen `StateHasChanged` auf (Abmeldung in `Dispose`). Native Dateidialoge laufen über die statische Klasse `DesktopDialogs` (`MainWindow` wird in `Program.cs` gesetzt). Dateipfade zu Programmdateien immer über `AppContext.BaseDirectory` bilden, nie relativ zum Arbeitsverzeichnis.

### Photino-Eigenheiten (gelten für jedes Photino-Projekt, auch das Setup)

- Dateifilter für `ShowOpenFile`/`ShowSaveFile` immer über `FileDialogFilters.ForPhotino(filters, OperatingSystem.IsLinux())` übergeben: Photino.Native unter Linux zerlegt Filter mit mehreren Endungen falsch und zeigt sonst keine Dateien an.
- Nach `Build()` `app.MainWindow.SetLogVerbosity(0)` setzen, sonst flutet Photino unter Linux die Konsole.
- Profilnamen werden zu Dateinamen: Prüfung über `ProfileNameValidator` (Core, im ViewModel), Absicherung des Pfads in `JsonStorageService.ToSafeProfileFileName`.

### KI-Anbindung

`LlmServiceDispatcher` ist die einzige `ILlmService`-Implementierung im Betrieb: Bei jedem Aufruf lädt er die **globale** `AiConfig` (`LoadAiConfigAsync`) und verzweigt per `switch` auf `SelectedProvider` (Ollama nativ, Gemini nativ, Mistral, sonst OpenAI-kompatibler Pfad für LM Studio/OpenAI/Custom). Ein neuer Provider braucht: Enum-Wert in `AiConfig.cs`, Fälle in `AskQuestionAsync` **und** `ValidateConnectionAsync`, sowie UI in `SettingsModal.razor`/`SettingsViewModel`. `FakeAi` ist der Standard-/Testmodus und liefert für Karteikarten-Prompts (enthält „Lernkarteikarten“ oder „JSON“) ein festes JSON-Array.

### RAG und Karteikarten

- `DocumentService` extrahiert Text (PDF via PdfPig) und zerlegt ihn in Chunks (Standard 250 Wörter).
- Retrieval ist **kein** Embedding-Verfahren: `ChatViewModel.GetMostRelevantChunks` bewertet Chunks per Stichwort-Treffer (mit deutscher Stoppwortliste) und gibt die Top 8 an das LLM.
- `FlashcardService` lässt das LLM ein JSON-Array mit `question`/`answer` erzeugen und parst es; außerdem Markdown-Export und Druck.

### Persistenz

`JsonStorageService` speichert unter `AppPaths.UserDataDirectory` (Windows `%APPDATA%\StudyScope\`, Linux `~/.config/StudyScope/`): je Profil `Profiles/<name>.json` (Dokumente + profilspezifische `AiSettings`), dazu `config.json` (globale KI-Konfiguration) und `last_profile.txt`. API-Keys werden beim Schreiben per `AesEncryptionService` (AES-GCM, fest abgeleiteter Schlüssel) verschlüsselt und beim Lesen entschlüsselt. `SettingsViewModel` speichert die KI-Konfiguration sowohl profilspezifisch als auch global – der Dispatcher liest nur die globale.

Für Tests nimmt `JsonStorageService` ein Basisverzeichnis entgegen (`new JsonStorageService(tempDir)`); Tests nutzen ein temporäres Verzeichnis, nicht `%APPDATA%`.
